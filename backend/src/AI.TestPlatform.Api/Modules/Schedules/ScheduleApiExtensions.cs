using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Schedules;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Schedules;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AI.TestPlatform.Api.Common;

namespace AI.TestPlatform.Api.Modules.Schedules;

// 定时任务端点（/api/schedules）：CRUD + 启停 + 立即试跑 + Cron 预览 + 批量删除
public static class ScheduleApiExtensions
{
    public static RouteGroupBuilder MapScheduleApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            TestDbContext db,
            ScheduleService service,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] bool? enabled = null,
            [FromQuery] string? keyword = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.Schedules.AsNoTracking();
            if (projectId.HasValue) query = query.Where(s => s.ProjectId == projectId.Value);
            if (enabled.HasValue) query = query.Where(s => s.Enabled == enabled.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(s => EF.Functions.ILike(s.Name, $"%{k}%"));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id, s.ProjectId, s.Name, s.CronExpression, s.Enabled,
                    s.Module, s.Priority, s.TestCaseIds, s.EnvironmentId,
                    EnvironmentName = s.Environment != null ? s.Environment.Name : null,
                    s.LastRunAt, s.NextRunAt, s.LastCreatedCount, s.LastError,
                    s.CreatedAt, s.UpdatedAt, s.Browsers, s.ExpandDataSets,
                    s.ScopeKind, s.TestPlanIds,
                    s.CreatedById,
                })
                .ToListAsync(ct);

            // 创建人显示名（M8 审计字段）：本页一次批量解析
            var creatorNames = await UserNameResolver.ResolveAsync(db, rows.Select(r => r.CreatedById), ct);

            // 计划范围要先批量取出计划名，否则前端只能显示一串 ID
            var allPlanIds = rows
                .Where(r => r.ScopeKind == ScheduleScopeKind.TestPlan && r.TestPlanIds != null)
                .SelectMany(r => r.TestPlanIds!)
                .Distinct().ToList();
            var planRefs = allPlanIds.Count == 0
                ? new Dictionary<Guid, SchedulePlanRefDto>()
                : (await db.TestPlans.AsNoTracking()
                        .Where(p => allPlanIds.Contains(p.Id))
                        .Select(p => new { p.Id, p.Name, p.ReleaseName, p.Status })
                        .ToListAsync(ct))
                    .ToDictionary(p => p.Id,
                        p => new SchedulePlanRefDto(p.Id, p.Name, p.ReleaseName, p.Status));

            var items = new List<ScheduleSummaryDto>(rows.Count);
            foreach (var s in rows)
            {
                var isPlanScope = s.ScopeKind == ScheduleScopeKind.TestPlan;
                // 计划范围下用例数没有意义，保持 0 而不是去统计（也就不必解引用计划的范围）
                var count = isPlanScope
                    ? 0
                    : s.TestCaseIds is { Count: > 0 }
                        ? await db.TestCases.AsNoTracking()
                            .CountAsync(t => s.TestCaseIds.Contains(t.Id) && t.Type != TestType.Mobile, ct)
                        : await CountByScopeAsync(db, s.ProjectId, s.Module, s.Priority, ct);

                var planList = isPlanScope
                    ? (s.TestPlanIds ?? []).Where(planRefs.ContainsKey)
                        .Select(id => planRefs[id]).ToList()
                    : null;

                items.Add(new ScheduleSummaryDto(
                    s.Id, s.ProjectId, s.Name, s.CronExpression, s.Enabled,
                    s.Module, s.Priority, count, s.EnvironmentId, s.EnvironmentName,
                    s.LastRunAt, s.NextRunAt, s.LastCreatedCount, s.LastError,
                    CronUtils.Describe(s.CronExpression),
                    s.CreatedAt, s.UpdatedAt, s.Browsers, s.ExpandDataSets,
                    s.ScopeKind, planList,
                    creatorNames.GetName(s.CreatedById)));
            }

            return Results.Ok(new PagedResult<ScheduleSummaryDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ManageSchedules);

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var s = await db.Schedules.AsNoTracking()
                .Include(x => x.Environment)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();

            return Results.Ok(new ScheduleDetailDto(
                s.Id, s.ProjectId, s.Name, s.CronExpression, s.Enabled,
                s.Module, s.Priority, s.TestCaseIds ?? new List<Guid>(), s.EnvironmentId,
                s.LastRunAt, s.NextRunAt, s.LastCreatedCount, s.LastError,
                CronUtils.Describe(s.CronExpression), s.CreatedAt, s.UpdatedAt));
        }).WithPermission(Permission.ManageSchedules);

        group.MapPost("/", async (
            CreateScheduleRequest request,
            IValidator<CreateScheduleRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });
            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });

            var planError = await ValidatePlanScopeAsync(db, request.ProjectId,
                request.ScopeKind, request.TestPlanIds, ct);
            if (planError is not null) return Results.BadRequest(new { message = planError });

            var schedule = new Schedule
            {
                ProjectId = request.ProjectId,
                Name = request.Name.Trim(),
                CronExpression = request.CronExpression.Trim(),
                Enabled = request.Enabled,
                Module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim(),
                Priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(),
                TestCaseIds = request.TestCaseIds is { Count: > 0 } ? request.TestCaseIds.Distinct().ToList() : null,
                EnvironmentId = request.EnvironmentId,
                Browsers = NormalizeBrowsers(request.Browsers),
                ExpandDataSets = request.ExpandDataSets,
                ScopeKind = request.ScopeKind,
                TestPlanIds = request.ScopeKind == ScheduleScopeKind.TestPlan
                    ? request.TestPlanIds?.Distinct().ToList()
                    : null,
            };
            schedule.NextRunAt = request.Enabled
                ? CronUtils.GetNextOccurrence(schedule.CronExpression)?.ToUniversalTime()
                : null;

            db.Schedules.Add(schedule);
            await db.SaveChangesAsync(ct);

            var created = await db.Schedules.AsNoTracking()
                .Include(x => x.Environment)
                .FirstAsync(x => x.Id == schedule.Id, ct);
            return Results.Created($"/api/schedules/{schedule.Id}", ToDetail(created));
        }).WithPermission(Permission.ManageSchedules).WithAudit("Create", "Schedule");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateScheduleRequest request,
            IValidator<UpdateScheduleRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (schedule is null) return Results.NotFound();

            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });

            var planError = await ValidatePlanScopeAsync(db, schedule.ProjectId,
                request.ScopeKind, request.TestPlanIds, ct);
            if (planError is not null) return Results.BadRequest(new { message = planError });

            var cronChanged = !string.Equals(schedule.CronExpression, request.CronExpression.Trim(), StringComparison.Ordinal);
            var wasDisabled = !schedule.Enabled;

            schedule.Name = request.Name.Trim();
            schedule.CronExpression = request.CronExpression.Trim();
            schedule.Enabled = request.Enabled;
            schedule.Module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim();
            schedule.Priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim();
            schedule.TestCaseIds = request.TestCaseIds is { Count: > 0 } ? request.TestCaseIds.Distinct().ToList() : null;
            schedule.EnvironmentId = request.EnvironmentId;
            schedule.Browsers = NormalizeBrowsers(request.Browsers);
            schedule.ExpandDataSets = request.ExpandDataSets;
            schedule.ScopeKind = request.ScopeKind;
            // 切回用例范围时清空计划列表，避免两种范围的数据同时留在行里引发歧义
            schedule.TestPlanIds = request.ScopeKind == ScheduleScopeKind.TestPlan
                ? request.TestPlanIds?.Distinct().ToList()
                : null;
            schedule.UpdatedAt = DateTime.UtcNow;

            // 停用清空下次执行时间；启用/改 cron 后重算
            if (!request.Enabled)
                schedule.NextRunAt = null;
            else if (cronChanged || wasDisabled || schedule.NextRunAt is null)
                schedule.NextRunAt = CronUtils.GetNextOccurrence(schedule.CronExpression)?.ToUniversalTime();

            await db.SaveChangesAsync(ct);

            var updated = await db.Schedules.AsNoTracking()
                .Include(x => x.Environment)
                .FirstAsync(x => x.Id == id, ct);
            return Results.Ok(ToDetail(updated));
        }).WithPermission(Permission.ManageSchedules).WithAudit("Update", "Schedule");

        // 启用 / 停用
        group.MapPost("/{id:guid}/toggle", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (schedule is null) return Results.NotFound();

            schedule.Enabled = !schedule.Enabled;
            schedule.NextRunAt = schedule.Enabled
                ? CronUtils.GetNextOccurrence(schedule.CronExpression)?.ToUniversalTime()
                : null;
            schedule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var result = await db.Schedules.AsNoTracking().Include(x => x.Environment)
                .FirstAsync(x => x.Id == id, ct);
            return Results.Ok(ToDetail(result));
        }).WithPermission(Permission.ManageSchedules).WithAudit("Toggle", "Schedule");

        // 立即试跑：不改变既有排期，只额外创建一批执行
        group.MapPost("/{id:guid}/run", async (Guid id, ScheduleService service, CancellationToken ct) =>
        {
            var exists = await service.ExecuteAsync(id, ct);
            return exists.Error is null ? Results.Ok(exists) : Results.BadRequest(exists);
        }).WithPermission(Permission.ManageSchedules).WithAudit("Run", "Schedule");

        group.MapPost("/cron-preview", async (
            CronPreviewRequest request,
            IValidator<CronPreviewRequest> validator,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.Ok(new CronPreviewResult(false,
                    validation.Errors.FirstOrDefault()?.ErrorMessage, request.CronExpression, new List<DateTime>()));

            var occurrences = CronUtils.NextOccurrences(request.CronExpression, request.Count);
            return Results.Ok(new CronPreviewResult(true, null,
                CronUtils.Describe(request.CronExpression), occurrences));
        }).WithPermission(Permission.ManageSchedules);

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (schedule is null) return Results.NotFound();
            db.Schedules.Remove(schedule);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageSchedules).WithAudit("Delete", "Schedule");

        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ids = request.Ids.Distinct().ToList();
            var schedules = await db.Schedules.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            db.Schedules.RemoveRange(schedules);

            var found = schedules.Select(s => s.Id).ToHashSet();
            var skipped = ids.Where(id => !found.Contains(id))
                .Select(id => new BatchDeleteSkippedItem(id, null, "定时任务不存在"))
                .ToList();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new BatchDeleteResultDto(schedules.Count, skipped));
        }).WithPermission(Permission.ManageSchedules).WithAudit("BatchDelete", "Schedule");

        return group;
    }

    private static Task<int> CountByScopeAsync(TestDbContext db, Guid projectId, string? module, string? priority, CancellationToken ct)
    {
        var query = db.TestCases.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.Type != TestType.Mobile);
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(t => t.Module == module);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(t => t.Priority == priority);
        return query.CountAsync(ct);
    }

    private static ScheduleDetailDto ToDetail(Schedule s) => new(
        s.Id, s.ProjectId, s.Name, s.CronExpression, s.Enabled,
        s.Module, s.Priority, s.TestCaseIds ?? new List<Guid>(), s.EnvironmentId,
        s.LastRunAt, s.NextRunAt, s.LastCreatedCount, s.LastError,
        CronUtils.Describe(s.CronExpression), s.CreatedAt, s.UpdatedAt,
        s.Browsers, s.ExpandDataSets, s.ScopeKind, s.TestPlanIds);

    /// <summary>
    /// 计划范围校验：必须选计划，且计划要属于同一项目。
    ///
    /// 跨项目的计划必须挡住——定时任务挂的是项目级的执行配置（环境、用例过滤），
    /// 让它去驱动别的项目的计划，报告里的项目归属和权限边界都会变得说不清。
    /// </summary>
    private static async Task<string?> ValidatePlanScopeAsync(TestDbContext db, Guid projectId,
        ScheduleScopeKind scopeKind, List<Guid>? testPlanIds, CancellationToken ct)
    {
        if (scopeKind != ScheduleScopeKind.TestPlan) return null;
        if (testPlanIds is not { Count: > 0 })
            return "执行范围选择「测试计划」时，至少要选择一个计划";

        var ids = testPlanIds.Distinct().ToList();
        var plans = await db.TestPlans.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.ProjectId })
            .ToListAsync(ct);

        var missing = ids.Where(id => plans.All(p => p.Id != id)).ToList();
        if (missing.Count > 0)
            return $"有 {missing.Count} 个计划不存在，请重新选择";

        var foreign = plans.Where(p => p.ProjectId != projectId).ToList();
        if (foreign.Count > 0)
            return $"计划「{foreign[0].Name}」属于其它项目，定时任务不能跨项目执行";

        return null;
    }

    /// <summary>浏览器矩阵归一化：去重、剔除空值、无法识别则忽略</summary>
    private static List<string>? NormalizeBrowsers(List<string>? browsers)
    {
        if (browsers is null) return null;
        var result = browsers
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(BrowserCatalog.Normalize)
            .Distinct()
            .Take(BrowserCatalog.All.Count)
            .ToList();
        return result.Count > 0 ? result : null;
    }
}
