using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using AI.TestPlatform.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestPlans;

// 测试计划（/api/test-plans）：带目标与多轮次的质量验收过程
public static class TestPlanApiExtensions
{
    public static RouteGroupBuilder MapTestPlanApi(this RouteGroupBuilder group)
    {
        // ---------------------------------------------------------------- 列表与详情

        group.MapGet("/", async (
            TestDbContext db, CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] TestPlanStatus? status = null,
            [FromQuery] Guid? ownerId = null,
            [FromQuery] string? releaseName = null,
            [FromQuery] string? search = null,
            [FromQuery] Guid? requirementId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.TestPlans.AsNoTracking().AsQueryable();
            if (projectId is not null) query = query.Where(p => p.ProjectId == projectId);
            if (status is not null) query = query.Where(p => p.Status == status);
            if (ownerId is not null) query = query.Where(p => p.OwnerId == ownerId);
            if (!string.IsNullOrWhiteSpace(releaseName)) query = query.Where(p => p.ReleaseName == releaseName);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(p => p.Name.Contains(keyword));
            }
            if (requirementId is not null) query = query.Where(p => p.RequirementId == requirementId);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(p => p.Status == TestPlanStatus.Active)
                .ThenByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new
                {
                    p.Id, p.ProjectId, p.Name, p.Description, p.ReleaseName, p.Status,
                    p.StartsAt, p.EndsAt, p.OwnerId,
                    // 投影里直接取项目名，EF 会翻译成 JOIN，不必再 Include
                    ProjectName = p.Project.Name,
                    OwnerName = p.Owner != null ? (p.Owner.DisplayName ?? p.Owner.Username) : null,
                    p.TargetPassRate, p.AllowErrors, p.ExcludeFlakyFromFailure, p.GateMode,
                    p.DefectGateEnabled,
                    p.EnvironmentId,
                    CaseCount = p.Items.Count,
                    p.LastRoundAt, p.LastCreatedCount, p.LastError,
                    p.CreatedAt, p.UpdatedAt,
                    p.CreatedById,
                    // 关联需求
                    p.RequirementId,
                    RequirementTitle = p.Requirement != null ? p.Requirement.Title : null,
                })
                .ToListAsync(ct);

            var ids = items.Select(i => i.Id).ToList();
            var lastRounds = await LoadLastRoundStatsAsync(db, ids, ct);
            var running = await LoadRunningRoundAsync(db, ids, ct);
            // 创建人显示名（M8 审计字段）：本页一次批量解析
            var creatorNames = await UserNameResolver.ResolveAsync(db, items.Select(p => p.CreatedById), ct);

            var dtos = items.Select(p =>
            {
                // 值元组不能用 ?. —— 字典取不到时给的是 default(元组)，必须先判存在
                var hasLast = lastRounds.TryGetValue(p.Id, out var last);
                var hasRunning = running.TryGetValue(p.Id, out var run);
                return new TestPlanSummaryDto(
                    p.Id, p.ProjectId, p.ProjectName, p.Name, p.Description, p.ReleaseName, p.Status,
                    p.StartsAt, p.EndsAt, p.OwnerId, p.OwnerName,
                    p.TargetPassRate, p.AllowErrors, p.ExcludeFlakyFromFailure, p.GateMode,
                    p.DefectGateEnabled,
                    p.EnvironmentId,
                    p.CaseCount,
                    hasRunning ? run.RoundNo : null,
                    hasRunning ? run.Total : null,
                    hasRunning ? run.Passed : null,
                    p.LastRoundAt,
                    hasLast ? last.RoundNo : null,
                    hasLast ? last.PassRate : null,
                    p.LastError, p.CreatedAt, p.UpdatedAt,
                    creatorNames.GetName(p.CreatedById),
                    p.RequirementId, p.RequirementTitle);
            }).ToList();

            return Results.Ok(new PagedResult<TestPlanSummaryDto>(dtos, total, page, pageSize));
        }).WithPermission(Permission.ViewTestPlans);

        // 顶栏计数
        group.MapGet("/summary", async (
            TestDbContext db, Guid? projectId, CancellationToken ct) =>
        {
            var query = db.TestPlans.AsNoTracking().AsQueryable();
            if (projectId is not null) query = query.Where(p => p.ProjectId == projectId);
            var counts = await query.GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return Results.Ok(Enum.GetValues<TestPlanStatus>().ToDictionary(
                s => s.ToString(),
                s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0));
        }).WithPermission(Permission.ViewTestPlans);

        // 版本标识候选（筛选下拉用）
        group.MapGet("/releases", async (TestDbContext db, Guid? projectId, CancellationToken ct) =>
        {
            var query = db.TestPlans.AsNoTracking().Where(p => p.ReleaseName != null);
            if (projectId is not null) query = query.Where(p => p.ProjectId == projectId);
            return Results.Ok(await query.Select(p => p.ReleaseName!).Distinct()
                .OrderByDescending(r => r).Take(50).ToListAsync(ct));
        }).WithPermission(Permission.ViewTestPlans);

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, TestPlanService plans,
            CancellationToken ct) =>
        {
            var plan = await db.TestPlans.AsNoTracking()
                .Include(p => p.Project)
                .Include(p => p.Owner)
                .Include(p => p.Environment)
                .Include(p => p.Requirement)
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();

            // 范围体检随详情一起返回：进详情页就该看到范围有没有问题，不必再点一次
            var issues = await plans.ValidateScopeAsync(id, ct);
            var roundCount = await db.TestPlanRounds.CountAsync(r => r.PlanId == id, ct);
            // 哪些定时任务把这个计划纳入了执行范围——配置入口在定时任务页，
            // 计划页只做只读展示，否则同一件事会有两个入口、两个真相来源
            var schedules = await LoadReferencingSchedulesAsync(db, id, ct);

            var ownerName = plan.Owner is null ? null : plan.Owner.DisplayName ?? plan.Owner.Username;
            return Results.Ok(new TestPlanDetailDto(
                plan.Id, plan.ProjectId, plan.Project.Name, plan.Name, plan.Description, plan.ReleaseName, plan.Status,
                plan.StartsAt, plan.EndsAt, plan.OwnerId, ownerName,
                plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure, plan.GateMode,
                plan.DefectGateEnabled,
                plan.EnvironmentId, plan.Environment?.Name,
                plan.Browsers ?? [], plan.ExpandDataSets,
                plan.Items.Count, roundCount, schedules, issues,
                plan.CreatedAt, plan.UpdatedAt,
                plan.RequirementId, plan.Requirement?.Title));
        }).WithPermission(Permission.ViewTestPlans);

        // ---------------------------------------------------------------- CRUD

        group.MapPost("/", async (CreateTestPlanRequest request, TestDbContext db,
            ICurrentUser current, CancellationToken ct) =>
        {
            var error = Validate(request.Name, request.TargetPassRate, request.StartsAt, request.EndsAt);
            if (error is not null) return Results.ValidationProblem(error);
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["projectId"] = ["项目不存在"],
                });

            // 唯一性：同一项目下「名称 + 版本标识」不可重复
            var name = request.Name.Trim();
            var releaseName = Trim(request.ReleaseName);
            if (await IsDuplicatePlanAsync(db, request.ProjectId, name, releaseName, null, ct))
                return DuplicatePlan(name, releaseName);

            var plan = new TestPlan
            {
                ProjectId = request.ProjectId,
                Name = name,
                Description = Trim(request.Description),
                ReleaseName = releaseName,
                StartsAt = ToUtc(request.StartsAt),
                EndsAt = ToUtc(request.EndsAt),
                OwnerId = request.OwnerId ?? current.Id,
                TargetPassRate = request.TargetPassRate ?? 0.95,
                AllowErrors = request.AllowErrors ?? false,
                ExcludeFlakyFromFailure = request.ExcludeFlakyFromFailure ?? true,
                GateMode = request.GateMode ?? PlanGateMode.LastRound,
                DefectGateEnabled = request.DefectGateEnabled ?? false,
                EnvironmentId = request.EnvironmentId,
                Browsers = NormalizeBrowsers(request.Browsers),
                ExpandDataSets = request.ExpandDataSets ?? true,
                RequirementId = request.RequirementId,
            };

            // ⚠️ 顺序不能反：必须先 Add 主行再写明细。
            // ApplyItemsAsync 内部会 SaveChanges，若此时计划还没登记到 DbContext，
            // EF 只看得见一堆 TestPlanItem，就会把明细先插进去，撞上
            // FK_TestPlanItems_TestPlans_PlanId（23503）——「带范围创建计划」直接 500。
            // 先 Add 之后 EF 能排出「计划 → 明细」的插入顺序，一次 SaveChanges 全部落库。
            db.TestPlans.Add(plan);

            if (request.TestCaseIds is { Count: > 0 })
                await ApplyItemsAsync(db, plan.Id, request.TestCaseIds, ct);

            // 上面的 ApplyItemsAsync 已经保存过时，这里是空操作，保留是为了覆盖"没传范围"的分支
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/test-plans/{plan.Id}", plan.Id);
        }).WithPermission(Permission.ManageTestPlans).WithAudit("Create", "TestPlan");

        group.MapPut("/{id:guid}", async (Guid id, UpdateTestPlanRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var error = Validate(request.Name, request.TargetPassRate, request.StartsAt, request.EndsAt);
            if (error is not null) return Results.ValidationProblem(error);

            var plan = await db.TestPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();

            // 唯一性：改名/改版本时也要挡住重复（排除自身，否则「只改描述」会被自己判重）
            var name = request.Name.Trim();
            var releaseName = Trim(request.ReleaseName);
            if (await IsDuplicatePlanAsync(db, plan.ProjectId, name, releaseName, id, ct))
                return DuplicatePlan(name, releaseName);

            plan.Name = name;
            plan.Description = Trim(request.Description);
            plan.ReleaseName = releaseName;
            plan.StartsAt = ToUtc(request.StartsAt);
            plan.EndsAt = ToUtc(request.EndsAt);
            plan.OwnerId = request.OwnerId;
            plan.TargetPassRate = request.TargetPassRate ?? plan.TargetPassRate;
            plan.AllowErrors = request.AllowErrors ?? plan.AllowErrors;
            plan.ExcludeFlakyFromFailure = request.ExcludeFlakyFromFailure ?? plan.ExcludeFlakyFromFailure;
            plan.GateMode = request.GateMode ?? plan.GateMode;
            plan.DefectGateEnabled = request.DefectGateEnabled ?? plan.DefectGateEnabled;
            plan.EnvironmentId = request.EnvironmentId;
            plan.Browsers = NormalizeBrowsers(request.Browsers);
            plan.ExpandDataSets = request.ExpandDataSets ?? plan.ExpandDataSets;
            plan.RequirementId = request.RequirementId;
            plan.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(plan.Id);
        }).WithPermission(Permission.ManageTestPlans).WithAudit("Update", "TestPlan");

        // 状态流转（开始 / 完成 / 归档）
        group.MapPost("/{id:guid}/status", async (Guid id, SetPlanStatusRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var plan = await db.TestPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();

            var allowed = AllowedTransitions(plan.Status);
            if (!allowed.Contains(request.Status))
                return Results.Json(new
                {
                    message = $"不允许从「{StatusLabel(plan.Status)}」直接转为「{StatusLabel(request.Status)}」",
                }, statusCode: StatusCodes.Status400BadRequest);

            plan.Status = request.Status;
            plan.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = $"已转为「{StatusLabel(request.Status)}」" });
        }).WithPermission(Permission.ManageTestPlans).WithAudit("ChangeStatus", "TestPlan");

        group.MapPost("/{id:guid}/copy", async (Guid id, TestDbContext db,
            ICurrentUser current, CancellationToken ct) =>
        {
            var source = await db.TestPlans.AsNoTracking().Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (source is null) return Results.NotFound();

            // 复制出来的名称可能撞上既有计划（同名同版本）——自动加序号直到唯一，
            // 否则「复制」这个动作会被唯一性校验拦下，用户还得手动改名才能复制
            var baseName = $"{source.Name} 副本";
            var copyName = baseName;
            var seq = 2;
            while (await IsDuplicatePlanAsync(db, source.ProjectId, copyName, source.ReleaseName, null, ct))
                copyName = $"{baseName}{seq++}";

            // 复制刻意**不带轮次**：新版本要从干净的历史开始，把上一版的轮次带过来只会误导
            var copy = new TestPlan
            {
                ProjectId = source.ProjectId,
                Name = copyName,
                Description = source.Description,
                ReleaseName = source.ReleaseName,
                Status = TestPlanStatus.Draft,
                StartsAt = source.StartsAt,
                EndsAt = source.EndsAt,
                OwnerId = source.OwnerId ?? current.Id,
                TargetPassRate = source.TargetPassRate,
                AllowErrors = source.AllowErrors,
                ExcludeFlakyFromFailure = source.ExcludeFlakyFromFailure,
                GateMode = source.GateMode,
                DefectGateEnabled = source.DefectGateEnabled,
                EnvironmentId = source.EnvironmentId,
                Browsers = source.Browsers,
                ExpandDataSets = source.ExpandDataSets,
                RequirementId = source.RequirementId,
            };
            var order = 0;
            foreach (var item in source.Items.OrderBy(i => i.Order))
                copy.Items.Add(new TestPlanItem { TestCaseId = item.TestCaseId, Order = order++ });

            db.TestPlans.Add(copy);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/test-plans/{copy.Id}", copy.Id);
        }).WithPermission(Permission.ManageTestPlans).WithAudit("Copy", "TestPlan");

        // 删除口径：未归档且有轮次 → 拒绝（轮次与报告是验收材料，提示先归档）；
        // 已归档 → 允许删除（归档即封存完成，之后应可清理）。关联执行记录的 PlanRoundId
        // 置空保留（执行记录同时属于用例维度，删掉会扰动用例统计）；轮次/范围随数据库级联消失。
        group.MapPost("/batch-delete", async (BatchDeleteRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var ids = request.Ids.Distinct().Take(200).ToList();
            var plans = await db.TestPlans
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Status, RoundCount = p.Rounds.Count })
                .ToListAsync(ct);

            var skipped = new List<BatchDeleteSkippedItem>();
            var deleted = new List<Guid>();
            foreach (var plan in plans)
            {
                if (plan.RoundCount > 0 && plan.Status != TestPlanStatus.Archived)
                {
                    skipped.Add(new BatchDeleteSkippedItem(plan.Id, plan.Name,
                        $"已有 {plan.RoundCount} 轮执行记录（验收材料），请先归档后再删除"));
                    continue;
                }
                deleted.Add(plan.Id);
            }
            var found = plans.Select(p => p.Id).ToHashSet();
            foreach (var missing in ids.Where(i => !found.Contains(i)))
                skipped.Add(new BatchDeleteSkippedItem(missing, null, "计划不存在"));

            if (deleted.Count > 0)
            {
                // 关联执行记录降级保留：PlanRoundId 置空（执行仍挂在用例维度，统计不受损）
                var roundIds = await db.TestPlanRounds
                    .Where(r => deleted.Contains(r.PlanId)).Select(r => r.Id).ToListAsync(ct);
                if (roundIds.Count > 0)
                {
                    await db.Executions
                        .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId.Value))
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.PlanRoundId, (Guid?)null), ct);
                }
                await db.TestPlans.Where(p => deleted.Contains(p.Id)).ExecuteDeleteAsync(ct);
            }
            return Results.Ok(new BatchDeleteResultDto(deleted.Count, skipped));
        }).WithPermission(Permission.ManageTestPlans).WithAudit("BatchDelete", "TestPlan");

        // ---------------------------------------------------------------- 范围

        group.MapPut("/{id:guid}/items", async (Guid id, SetPlanItemsRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            if (!await db.TestPlans.AsNoTracking().AnyAsync(p => p.Id == id, ct))
                return Results.NotFound();

            // 先删干净再插。用 DbSet 上的显式 RemoveRange/Add 而不是操作导航集合：
            // 依赖 EF 去猜子实体状态的做法已经在范围编辑上踩过一次坑
            // （主键非空 → 被当成「已存在」→ 发 UPDATE → 影响 0 行 → 并发异常），
            // 显式操作 DbSet 不依赖这个推断。TestSuiteCases 用的也是这套写法。
            var existing = await db.TestPlanItems.Where(i => i.PlanId == id).ToListAsync(ct);
            db.TestPlanItems.RemoveRange(existing);
            await db.SaveChangesAsync(ct);

            var count = await ApplyItemsAsync(db, id, request.TestCaseIds, ct);
            await db.TestPlans.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);

            return Results.Ok(new { count });
        }).WithPermission(Permission.ManageTestPlans).WithAudit("SetScope", "TestPlan");

        group.MapGet("/{id:guid}/items", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var items = await db.TestPlanItems.AsNoTracking()
                .Where(i => i.PlanId == id)
                .OrderBy(i => i.Order)
                .Select(i => new
                {
                    i.TestCaseId, i.Order,
                    Name = i.TestCase != null ? i.TestCase.Name : "(用例已删除)",
                    Module = i.TestCase != null ? i.TestCase.Module : null,
                    Priority = i.TestCase != null ? i.TestCase.Priority : null,
                    Type = i.TestCase != null ? i.TestCase.Type : TestType.Web,
                    // 用例已删除时给 null（原来用 Deprecated 当哨兵值，见 TestPlanItemDto 注释）
                    Status = i.TestCase != null ? i.TestCase.Status : (TestCaseStatus?)null,
                    IsFlaky = i.TestCase != null && i.TestCase.IsFlaky,
                    Deleted = i.TestCase == null,
                })
                .ToListAsync(ct);

            return Results.Ok(items.Select(i => new TestPlanItemDto(
                i.TestCaseId, i.Name, i.Module, i.Priority, i.Type, i.Status, i.IsFlaky,
                i.Order, i.Deleted)).ToList());
        }).WithPermission(Permission.ViewTestPlans);

        group.MapPost("/{id:guid}/items/from-suite", async (Guid id, ImportPlanItemsRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            if (!await db.TestPlans.AsNoTracking().AnyAsync(p => p.Id == id, ct))
                return Results.NotFound();

            var suite = await db.TestSuites.AsNoTracking().Include(s => s.Cases)
                .FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);
            if (suite is null) return Results.NotFound();

            var incoming = suite.Cases.OrderBy(c => c.Order).Select(c => c.TestCaseId).ToList();
            var replace = string.Equals(request.Mode, "replace", StringComparison.OrdinalIgnoreCase);

            if (replace)
            {
                var all = await db.TestPlanItems.Where(i => i.PlanId == id).ToListAsync(ct);
                db.TestPlanItems.RemoveRange(all);
                await db.SaveChangesAsync(ct);
            }

            // 追加模式下要去重：计划与套件的用例范围常有交集，重复叠加会让同一用例跑两遍
            var current = await db.TestPlanItems.AsNoTracking()
                .Where(i => i.PlanId == id)
                .Select(i => new { i.TestCaseId, i.Order })
                .ToListAsync(ct);
            var existing = current.Select(c => c.TestCaseId).ToHashSet();

            var order = current.Count == 0 ? 0 : current.Max(c => c.Order) + 1;
            var added = 0;
            foreach (var caseId in incoming.Where(c => !existing.Contains(c)))
            {
                db.TestPlanItems.Add(new TestPlanItem { PlanId = id, TestCaseId = caseId, Order = order++ });
                added++;
            }

            await db.SaveChangesAsync(ct);
            await db.TestPlans.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);

            var total = await db.TestPlanItems.CountAsync(i => i.PlanId == id, ct);
            return Results.Ok(new
            {
                added,
                skipped = incoming.Count - added,
                total,
                message = added == incoming.Count
                    ? $"已从「{suite.Name}」导入 {added} 个用例"
                    : $"已导入 {added} 个用例，{incoming.Count - added} 个已在范围内被跳过",
            });
        }).WithPermission(Permission.ManageTestPlans).WithAudit("ImportScope", "TestPlan");

        // 范围体检（独立端点：详情页已内联返回，这里供「导入后立即复查」用）
        group.MapGet("/{id:guid}/items/validate", async (Guid id, TestPlanService plans,
            CancellationToken ct) =>
            Results.Ok(await plans.ValidateScopeAsync(id, ct)))
            .WithPermission(Permission.ViewTestPlans);

        // 轮次 / 达标判定 / 报告（同一路由分组下的另一组端点）
        group.MapTestPlanRoundApi();

        return group;
    }

    // ==================================================================== 辅助

    /// <summary>
    /// 覆盖写入计划范围。返回实际写入的条数。
    /// 走 DbSet 显式 Add，不依赖 EF 对导航集合里子实体的状态推断（见调用处的注释）。
    /// </summary>
    private static async Task<int> ApplyItemsAsync(TestDbContext db, Guid planId,
        IReadOnlyList<Guid> testCaseIds, CancellationToken ct)
    {
        if (testCaseIds.Count == 0) return 0;

        // 只保留项目中真实存在的用例（软删除的用例默认查询已过滤），并保序去重
        var valid = await db.TestCases.AsNoTracking()
            .Where(t => testCaseIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);
        var validSet = valid.ToHashSet();

        var order = 0;
        var seen = new HashSet<Guid>();
        foreach (var caseId in testCaseIds)
        {
            if (!validSet.Contains(caseId) || !seen.Add(caseId)) continue;
            db.TestPlanItems.Add(new TestPlanItem
            {
                PlanId = planId,
                TestCaseId = caseId,
                Order = order++,
            });
        }
        await db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>
    /// 列表页需要「最后一轮通过率」与「进行中轮次进度」，各用一条聚合查询批量取，避免 N+1。
    /// 通过率一律走 <see cref="TestPlanService.ToStats"/>，与达标判定保持同一口径——
    /// 列表说 60%、点进去判定说 75% 这种不一致会让数字失去可信度。
    /// </summary>
    private static async Task<Dictionary<Guid, (int RoundNo, double PassRate)>>
        LoadLastRoundStatsAsync(TestDbContext db, List<Guid> planIds, CancellationToken ct)
    {
        if (planIds.Count == 0) return new();

        var planRows = await db.TestPlans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ExcludeFlakyFromFailure, TreatAgentHealed = p.Project.TreatAgentHealedAsPass })
            .ToListAsync(ct);
        var planSettings = planRows.ToDictionary(p => p.Id, p => p.ExcludeFlakyFromFailure);
        // Agent 自愈通过是否计入达标（项目级，默认不计入）——与轮次页/达标判定同口径
        var treatByPlan = planRows.ToDictionary(p => p.Id, p => p.TreatAgentHealed);

        var rounds = await db.TestPlanRounds.AsNoTracking()
            .Where(r => planIds.Contains(r.PlanId) && r.Status != PlanRoundStatus.Running)
            .Select(r => new { r.Id, r.PlanId, r.RoundNo })
            .ToListAsync(ct);
        if (rounds.Count == 0) return new();

        // 每个计划取 RoundNo 最大的一轮
        var lastByPlan = rounds.GroupBy(r => r.PlanId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RoundNo).First());

        var roundIds = lastByPlan.Values.Select(r => r.Id).ToList();
        var stats = await db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
            .Select(e => new
            {
                RoundId = e.PlanRoundId!.Value,
                e.Status,
                e.AgentHealed,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
            })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, (int, double)>();
        foreach (var (planId, round) in lastByPlan)
        {
            var rows = stats.Where(x => x.RoundId == round.Id).ToList();
            var excludeFlaky = planSettings.GetValueOrDefault(planId, true);
            var counts = new RoundCounts(
                rows.Count,
                rows.Count(x => x.Status == ExecutionStatus.Passed),
                rows.Count(x => x.Status == ExecutionStatus.Failed),
                rows.Count(x => x.Status == ExecutionStatus.Error),
                rows.Count(x => x.Status == ExecutionStatus.Skipped),
                0,
                rows.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Failed),
                rows.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Error),
                PassedViaAgent: rows.Count(x => x.AgentHealed && x.Status == ExecutionStatus.Passed));
            result[planId] = (round.RoundNo,
                TestPlanService.ToStats(counts, excludeFlaky, treatByPlan.GetValueOrDefault(planId)).PassRate);
        }
        return result;
    }

    private static async Task<Dictionary<Guid, (int RoundNo, int Total, int Passed)>>
        LoadRunningRoundAsync(TestDbContext db, List<Guid> planIds, CancellationToken ct)
    {
        if (planIds.Count == 0) return new();

        var rounds = await db.TestPlanRounds.AsNoTracking()
            .Where(r => planIds.Contains(r.PlanId) && r.Status == PlanRoundStatus.Running)
            .Select(r => new { r.Id, r.PlanId, r.RoundNo })
            .ToListAsync(ct);
        if (rounds.Count == 0) return new();

        var roundIds = rounds.Select(r => r.Id).ToList();
        var stats = await db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
            .GroupBy(e => e.PlanRoundId!.Value)
            .Select(g => new
            {
                RoundId = g.Key,
                Total = g.Count(),
                Passed = g.Count(x => x.Status == ExecutionStatus.Passed),
            })
            .ToListAsync(ct);

        return rounds.ToDictionary(r => r.PlanId, r =>
        {
            var s = stats.FirstOrDefault(x => x.RoundId == r.Id);
            return (r.RoundNo, s?.Total ?? 0, s?.Passed ?? 0);
        });
    }

    /// <summary>
    /// JSON 绑定的时间是 Kind=Unspecified（前端只传「2026-09-16T10:00」这种无时区值），
    /// Npgsql 写 timestamptz 拒绝 Unspecified——按本地时间解释后转 UTC。
    /// 与报告服务的 ToUtc 同一套约定，此模块创建/更新计划都要过这一步，否则保存即 500。
    /// </summary>
    private static DateTime? ToUtc(DateTime? value)
    {
        if (value is null) return null;
        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime(),
        };
    }

    private static Dictionary<string, string[]>? Validate(
        string? name, double? targetPassRate, DateTime? startsAt, DateTime? endsAt)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["名称不能为空"];
        else if (name.Length > 200) errors["name"] = ["名称长度不能超过 200 位"];
        if (targetPassRate is < 0 or > 1)
            errors["targetPassRate"] = ["目标通过率必须在 0 到 1 之间"];
        if (startsAt is not null && endsAt is not null && endsAt < startsAt)
            errors["endsAt"] = ["结束时间不能早于开始时间"];
        return errors.Count == 0 ? null : errors;
    }

    /// <summary>状态单向流转：草稿 → 进行中 → 已完成 → 已归档，允许原地停留</summary>
    private static IReadOnlyList<TestPlanStatus> AllowedTransitions(TestPlanStatus current) => current switch
    {
        TestPlanStatus.Draft => [TestPlanStatus.Draft, TestPlanStatus.Active, TestPlanStatus.Archived],
        TestPlanStatus.Active => [TestPlanStatus.Active, TestPlanStatus.Completed, TestPlanStatus.Archived],
        TestPlanStatus.Completed => [TestPlanStatus.Completed, TestPlanStatus.Active, TestPlanStatus.Archived],
        _ => [TestPlanStatus.Archived],
    };

    private static string StatusLabel(TestPlanStatus status) => status switch
    {
        TestPlanStatus.Draft => "草稿",
        TestPlanStatus.Active => "进行中",
        TestPlanStatus.Completed => "已完成",
        _ => "已归档",
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 计划查重：同一项目下「计划名称 + 版本标识」必须唯一。
    ///
    /// ReleaseName 可空：Trim 后空串已归一为 null，此处用 <c>== releaseName</c> 比较，
    /// EF 会翻译成 <c>IS NULL</c>，因此「无版本号」也构成同一个键——
    /// 否则建两个同名且都不带版本的计划会被放行。
    /// <paramref name="excludeId"/> 用于更新时排除自身（只改描述、名称不动不该被判重复）。
    /// </summary>
    private static async Task<bool> IsDuplicatePlanAsync(
        TestDbContext db, Guid projectId, string name, string? releaseName,
        Guid? excludeId, CancellationToken ct) =>
        await db.TestPlans.AsNoTracking().AnyAsync(p =>
            p.ProjectId == projectId
            && p.Name == name
            && p.ReleaseName == releaseName
            && (excludeId == null || p.Id != excludeId), ct);

    /// <summary>计划重复的统一 409 响应（带出名称与版本标识，便于用户定位是哪一条）</summary>
    private static IResult DuplicatePlan(string name, string? releaseName)
    {
        var version = string.IsNullOrWhiteSpace(releaseName) ? "（无版本标识）" : $"「{releaseName}」";
        return Results.Json(
            new { message = $"该项目下已存在名称为「{name}」、版本标识为{version}的测试计划" },
            statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// 反查引用该计划的定时任务。
    ///
    /// TestPlanIds 以逗号分隔存储（与 TestCaseIds 同构），SQL 里没法精确按元素匹配，
    /// 因此在内存里过滤。规模假设：定时任务是个位到百位量级，且只查 ScopeKind=TestPlan 的那些；
    /// 若将来这个反查变频繁，可把该列改为 uuid[] 原生数组。
    ///
    /// 这里只做**只读展示**：配置入口在定时任务页（执行范围选择「测试计划」），
    /// 计划侧不再持有绑定，否则同一件事会有两个入口、两个真相来源。
    /// </summary>
    private static async Task<IReadOnlyList<PlanScheduleRefDto>> LoadReferencingSchedulesAsync(
        TestDbContext db, Guid planId, CancellationToken ct)
    {
        var candidates = await db.Schedules.AsNoTracking()
            .Where(s => s.ScopeKind == ScheduleScopeKind.TestPlan && s.TestPlanIds != null)
            .Select(s => new { s.Id, s.Name, s.CronExpression, s.Enabled, s.TestPlanIds })
            .ToListAsync(ct);

        return candidates
            .Where(s => s.TestPlanIds!.Contains(planId))
            .OrderBy(s => s.Name)
            .Select(s => new PlanScheduleRefDto(s.Id, s.Name, s.CronExpression, s.Enabled))
            .ToList();
    }

    private static List<string>? NormalizeBrowsers(List<string>? browsers)
    {
        if (browsers is null || browsers.Count == 0) return null;
        var normalized = browsers.Select(Application.Executions.BrowserCatalog.Normalize)
            .Distinct().ToList();
        return normalized.Count == 0 ? null : normalized;
    }
}
