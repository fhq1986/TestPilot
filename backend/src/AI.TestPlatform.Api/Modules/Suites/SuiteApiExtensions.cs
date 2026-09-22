using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Suites;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Suites;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Suites;

// 测试套件端点（/api/suites）：CRUD + 成员维护 + 运行套件 + 历史运行
public static class SuiteApiExtensions
{
    public static RouteGroupBuilder MapSuiteApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] SuiteKind? kind = null,
            [FromQuery] string? keyword = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.TestSuites.AsNoTracking();
            if (projectId.HasValue) query = query.Where(s => s.ProjectId == projectId.Value);
            if (kind.HasValue) query = query.Where(s => s.Kind == kind.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(s => EF.Functions.ILike(s.Name, $"%{k}%"));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SuiteSummaryDto(
                    s.Id, s.ProjectId, s.Name, s.Description, s.Kind,
                    s.Cases.Count,
                    s.EnvironmentId, s.Environment != null ? s.Environment.Name : null,
                    s.LastRunAt, s.LastSuiteRunId, s.LastCreatedCount, s.LastError,
                    s.CreatedAt, s.UpdatedAt, s.FailurePolicy,
                    // 创建人显示名（M8 审计字段）：EF 翻译成相关子查询
                    db.Users.Where(u => u.Id == s.CreatedById)
                        .Select(u => u.DisplayName != null && u.DisplayName != "" ? u.DisplayName : u.Username)
                        .FirstOrDefault()))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<SuiteSummaryDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewTestCases).Produces<PagedResult<SuiteSummaryDto>>();

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var detail = await LoadDetailAsync(db, id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).WithPermission(Permission.ViewTestCases).Produces<SuiteDetailDto>();

        group.MapPost("/", async (
            CreateSuiteRequest request,
            IValidator<CreateSuiteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });
            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });

            var suite = new TestSuite
            {
                ProjectId = request.ProjectId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Kind = request.Kind,
                EnvironmentId = request.EnvironmentId,
                FailurePolicy = request.FailurePolicy,
            };
            db.TestSuites.Add(suite);
            await db.SaveChangesAsync(ct);

            var casesError = await ReplaceCasesAsync(db, suite, request.Cases, ct);
            if (casesError is not null)
            {
                // 成员非法（依赖成环 / 指向套件外）时不留半个套件：套件本身没有意义
                db.TestSuites.Remove(suite);
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { message = casesError });
            }

            var created = await LoadDetailAsync(db, suite.Id, ct);
            return Results.Created($"/api/suites/{suite.Id}", created);
        }).WithPermission(Permission.ManageTestCases).WithAudit("Create", "Suite");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateSuiteRequest request,
            IValidator<UpdateSuiteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var suite = await db.TestSuites.Include(s => s.Cases).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (suite is null) return Results.NotFound();
            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });

            suite.Name = request.Name.Trim();
            suite.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            suite.Kind = request.Kind;
            suite.EnvironmentId = request.EnvironmentId;
            suite.FailurePolicy = request.FailurePolicy;
            suite.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var casesError = await ReplaceCasesAsync(db, suite, request.Cases, ct);
            if (casesError is not null) return Results.BadRequest(new { message = casesError });

            var updated = await LoadDetailAsync(db, id, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithPermission(Permission.ManageTestCases).WithAudit("Update", "Suite");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var suite = await db.TestSuites.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (suite is null) return Results.NotFound();
            db.TestSuites.Remove(suite);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageTestCases).WithAudit("Delete", "Suite");

        // 批量删除：成员用例关联（TestSuiteCase）由级联清理；测试计划对套件的引用是快照，不受影响
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
            var suites = await db.TestSuites.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            db.TestSuites.RemoveRange(suites);

            var found = suites.Select(s => s.Id).ToHashSet();
            var skipped = ids.Where(id => !found.Contains(id))
                .Select(id => new BatchDeleteSkippedItem(id, null, "测试套件不存在"))
                .ToList();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new BatchDeleteResultDto(suites.Count, skipped));
        }).WithPermission(Permission.ManageTestCases).WithAudit("BatchDelete", "Suite");

        // 运行套件：创建一批执行（TriggerSource 记为套件名，便于在列表里区分）
        group.MapPost("/{id:guid}/run", async (
            Guid id,
            RunSuiteRequest? request,
            IValidator<RunSuiteRequest> validator,
            SuiteRunner runner,
            HttpContext http,
            CancellationToken ct) =>
        {
            var payload = request ?? new RunSuiteRequest();
            var validation = await validator.ValidateAsync(payload, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await runner.RunAsync(id, payload, TriggerType.Manual,
                null, http.User.GetUserId(), ct);
            return result.Error is null ? Results.Ok(result) : Results.BadRequest(result);
        }).WithPermission(Permission.RunExecutions).WithAudit("Run", "Suite");

        // 历史运行（按 SuiteRunId 聚合的通过率趋势）
        group.MapGet("/{id:guid}/runs", async (
            Guid id, SuiteRunner runner, CancellationToken ct, [FromQuery] int take = 20) =>
        {
            take = take is < 1 ? 20 : take > 100 ? 100 : take;
            return Results.Ok(await runner.HistoryAsync(id, take, ct));
        }).WithPermission(Permission.ViewTestCases).Produces<List<SuiteRunSummaryDto>>();

        // 全量替换套件成员（界面拖拽排序后一次性保存，含每条用例的前置依赖）
        group.MapPut("/{id:guid}/cases", async (
            Guid id,
            SetSuiteCasesRequest request,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var suite = await db.TestSuites.Include(s => s.Cases).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (suite is null) return Results.NotFound();
            if (request.Cases is { Count: > 500 })
                return Results.BadRequest(new { message = "单个套件最多包含 500 条用例" });

            var casesError = await ReplaceCasesAsync(db, suite, request.Cases, ct);
            if (casesError is not null) return Results.BadRequest(new { message = casesError });

            var count = await db.TestSuiteCases.AsNoTracking().CountAsync(c => c.SuiteId == id, ct);
            return Results.Ok(new { suiteId = id, caseCount = count });
        }).WithPermission(Permission.ManageTestCases).WithAudit("UpdateCases", "Suite");

        return group;
    }

    /// <summary>
    /// 全量替换套件成员：顺序即列表顺序（只保留项目内、非移动端用例）。
    ///
    /// 前置依赖在这里做两道处理：
    /// 1. 面向用户的校验（自依赖 / 指向套件外的用例 / 环形依赖）由 SuiteDependencyGraph 给出可读原因，
    ///    校验的是**提交上来的集合**——用户看到的清单就是他提交的那份，报错才对得上；
    /// 2. 用例因不属于本项目/是移动端而被服务端剔除时，指向它的依赖一并清空（只记日志，不报错）：
    ///    报错会说「前置不在套件内」，可用户明明选了它，原因其实在服务端过滤，容易误导。
    /// 返回非 null 表示校验失败，调用方直接 400。
    /// </summary>
    private static async Task<string?> ReplaceCasesAsync(TestDbContext db, TestSuite suite,
        List<SuiteCaseSpec>? cases, CancellationToken ct)
    {
        var submitted = cases ?? new List<SuiteCaseSpec>();
        var nameOf = await BuildCaseNameLookupAsync(db, submitted.Select(c => c.TestCaseId).ToList(), ct);
        if (!SuiteDependencyGraph.TryNormalize(submitted, nameOf, out var normalized, out var error))
            return error;

        var existing = await db.TestSuiteCases.Where(c => c.SuiteId == suite.Id).ToListAsync(ct);
        db.TestSuiteCases.RemoveRange(existing);
        await db.SaveChangesAsync(ct);

        if (normalized.Count == 0) return null;

        // 只接受本项目内、支持执行的用例
        var requested = normalized.Select(c => c.TestCaseId).ToList();
        var valid = await db.TestCases.AsNoTracking()
            .Where(t => requested.Contains(t.Id) &&
                        t.ProjectId == suite.ProjectId &&
                        t.Type != TestType.Mobile)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var order = 0;
        foreach (var spec in normalized.Where(s => valid.Contains(s.TestCaseId)))
        {
            var dependsOn = spec.DependsOnTestCaseId;
            if (dependsOn is { } dep && !valid.Contains(dep))
                dependsOn = null; // 前置用例被服务端剔除（跨项目/移动端）→ 该依赖无法成立

            db.TestSuiteCases.Add(new TestSuiteCase
            {
                SuiteId = suite.Id,
                TestCaseId = spec.TestCaseId,
                Order = order++,
                DependsOnTestCaseId = dependsOn,
            });
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>用例名查询（含软删除：报错文案里要能显示被删用例的名字）</summary>
    private static async Task<Func<Guid, string>?> BuildCaseNameLookupAsync(
        TestDbContext db, List<Guid> caseIds, CancellationToken ct)
    {
        var ids = caseIds.Distinct().ToList();
        if (ids.Count == 0) return null;

        var names = await db.TestCases.IgnoreQueryFilters().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return id => names.TryGetValue(id, out var name) ? name : id.ToString("N")[..8];
    }

    /// <summary>
    /// 套件详情：用投影查询而不是 Include/ThenInclude。
    /// 用例是软删除（全局查询过滤器）的，而 TestSuiteCase.TestCase 是必需导航，
    /// Include 一个被过滤掉的必需导航会拿到 null，投影 + Left Join 语义更稳（并统一兜底显示名）。
    ///
    /// 前置用例名单独查一次（同一批用例名复用），而不是在投影里再嵌一层子查询：
    /// 面板里一次最多几十条用例，一次 IN 查询比让 SQL 生成器拼嵌套子查询更好读也更好调。
    /// </summary>
    private static async Task<SuiteDetailDto?> LoadDetailAsync(TestDbContext db, Guid id, CancellationToken ct)
    {
        var suite = await db.TestSuites.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new
            {
                s.Id, s.ProjectId, s.Name, s.Description, s.Kind,
                s.EnvironmentId,
                EnvironmentName = s.Environment != null ? s.Environment.Name : null,
                s.LastRunAt, s.LastSuiteRunId, s.LastCreatedCount, s.LastError,
                s.CreatedAt, s.UpdatedAt, s.FailurePolicy,
                Cases = s.Cases.OrderBy(c => c.Order).Select(c => new
                {
                    c.TestCaseId,
                    Name = c.TestCase != null ? c.TestCase.Name : "(用例已删除)",
                    Module = c.TestCase != null ? c.TestCase.Module : null,
                    Priority = c.TestCase != null ? c.TestCase.Priority : null,
                    Type = c.TestCase != null ? c.TestCase.Type : TestType.Web,
                    IsFlaky = c.TestCase != null && c.TestCase.IsFlaky,
                    VisualEnabled = c.TestCase != null && c.TestCase.VisualEnabled,
                    DataSetId = c.TestCase != null ? c.TestCase.DataSetId : (Guid?)null,
                    DataRowCount = c.TestCase != null && c.TestCase.DataSet != null ? c.TestCase.DataSet.RowCount : 0,
                    c.Order,
                    c.DependsOnTestCaseId,
                }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (suite is null) return null;

        var dependsOnIds = suite.Cases
            .Where(c => c.DependsOnTestCaseId is not null)
            .Select(c => c.DependsOnTestCaseId!.Value)
            .Distinct()
            .ToList();
        var dependsOnNames = dependsOnIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.TestCases.IgnoreQueryFilters().AsNoTracking()
                .Where(t => dependsOnIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var cases = suite.Cases.Select(c => new SuiteCaseItemDto(
            c.TestCaseId, c.Name, c.Module, c.Priority, c.Type, c.IsFlaky, c.VisualEnabled,
            c.DataSetId, c.DataRowCount, c.Order,
            c.DependsOnTestCaseId,
            c.DependsOnTestCaseId is { } dep
                ? (dependsOnNames.TryGetValue(dep, out var depName) ? depName : "(用例已删除)")
                : null)).ToList();

        return new SuiteDetailDto(
            suite.Id, suite.ProjectId, suite.Name, suite.Description, suite.Kind,
            suite.EnvironmentId, suite.EnvironmentName, cases,
            suite.LastRunAt, suite.LastSuiteRunId, suite.LastCreatedCount, suite.LastError,
            suite.CreatedAt, suite.UpdatedAt, suite.FailurePolicy);
    }
}
