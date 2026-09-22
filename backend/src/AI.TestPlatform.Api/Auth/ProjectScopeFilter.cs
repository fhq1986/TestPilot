using System.Collections;
using System.Reflection;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

/// <summary>
/// 项目作用域端点过滤器（迭代 E·①/①-4）：把"某项目内的资源"收拢到"项目成员"这一层。
///
/// 设计（低侵入）：
/// - **不逐个端点标注**，而是挂在项目作用域的 **RouteGroup** 上（<see cref="ProjectScopeEndpointExtensions.WithProjectScope"/>）；
/// - 每个端点所需的**项目角色**由该端点**既有的** <see cref="PermissionMetadata"/> 反推
///   （查看→Viewer，改用例/跑执行→Tester，改项目设置→Manager，改成员/系统设置→Owner），也可用 <c>WithProjectRole</c> 覆盖；
/// - **项目 id 的解析按优先级**：
///   ① 路由 <c>projectId</c> ② 查询串 <c>projectId</c>（列表）
///   ③ 按资源反查（路由 <c>id</c>/<c>attemptId</c>/<c>roundId</c>/<c>testCaseId</c> 对应实体所属项目）
///   ④ 绑定后请求体 DTO 的 <c>ProjectId</c>（创建类——过滤器在模型绑定后运行，从 <c>Arguments</c> 反射取，不读原始 body）
///   ④' 请求体 <c>TestCaseId</c>/<c>testCaseIds</c>（按用例触发执行）
///   ⑤ **批量端点的 <c>ids</c> 集合逐条反查**，对**每一个**涉及的项目都要求有权限（任一不通过即 403）
///   解析不到就**不拦**，由处理器自行处理。
///
/// 判定真值在 <see cref="IProjectAuthorization.CanAccessAsync"/>（管理员放行 + 成员激活式 + 角色比较）。
/// </summary>
public sealed class ProjectScopeFilter : IEndpointFilter
{
    private readonly ILogger<ProjectScopeFilter> _logger;

    public ProjectScopeFilter(ILogger<ProjectScopeFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var resource = http.GetEndpoint()?.Metadata.OfType<ProjectScopeMetadata>().Select(m => m.Resource).FirstOrDefault()
            ?? ProjectResource.None;

        TestDbContext? db = null;
        TestDbContext GetDb() => db ??= http.RequestServices.GetRequiredService<TestDbContext>();

        var projectIds = await ResolveProjectIdsAsync(context, resource, GetDb);
        if (projectIds.Count == 0)
            return await next(context); // 无项目上下文，交给处理器

        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        if (user.Id is not { } uid)
            return await next(context); // 未登录由上游 RequireAuthorization 处理

        var required = ResolveRequiredRole(http);
        var authz = http.RequestServices.GetRequiredService<IProjectAuthorization>();

        // 对**每一个**涉及的项目逐一校验：批量端点里混入无权项目的 id 也会被拦下（不是只看第一个）。
        foreach (var pid in projectIds)
        {
            if (await authz.CanAccessAsync(uid, user.Role, pid, required, http.RequestAborted))
                continue;

            var actual = await authz.GetRoleAsync(uid, pid, http.RequestAborted);
            _logger.LogWarning("项目授权拒绝：用户 {Username}({Uid}) 访问项目 {ProjectId} 需要项目角色 {Required}，实际 {Actual}",
                user.Username ?? "(匿名)", uid, pid, required, actual?.ToString() ?? "非成员");

            return Results.Json(new
            {
                message = "当前账号不是该项目成员（或其项目角色不足），无权访问该项目的资源",
                projectId = pid,
                required = required.ToString(),
                actual = actual?.ToString(),
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    /// <summary>解析本次请求涉及的全部项目 id（单项目=1 个；批量=N 个去重；解析不到=空）。</summary>
    private static async Task<IReadOnlyList<Guid>> ResolveProjectIdsAsync(EndpointFilterInvocationContext context,
        ProjectResource resource, Func<TestDbContext> getDb)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;

        // ① 路由 projectId
        if (ReadGuid(http.Request.RouteValues, ProjectIdRouteKeys) is { } routeId)
            return new[] { routeId };

        // ② 查询串 projectId
        if (Guid.TryParse(http.Request.Query["projectId"].ToString(), out var qid) && qid != Guid.Empty)
            return new[] { qid };

        // ③ 按资源反查（by-id 端点）
        if (resource != ProjectResource.None &&
            await ResolveByResourceAsync(resource, http, getDb(), ct) is { } rp)
            return new[] { rp };

        // ④ 请求体 DTO 的 ProjectId（创建类端点）
        if (FindArgGuid(context, "ProjectId") is { } bodyPid)
            return new[] { bodyPid };

        // ④' 请求体 TestCaseId / testCaseIds（按用例触发执行）
        if (FindArgGuid(context, "TestCaseId") is { } bodyCaseId)
            return await MapOrEmptyAsync(ProjectResource.TestCase, new[] { bodyCaseId }, getDb(), ct);
        if (CollectArgGuids(context, "testCaseIds") is { Count: > 0 } caseIds)
            return await MapOrEmptyAsync(ProjectResource.TestCase, caseIds, getDb(), ct);

        // ⑤ 批量端点的 ids：逐条反查，对每个涉及项目都要有权限
        if (resource != ProjectResource.None && CollectArgGuids(context, "ids") is { Count: > 0 } ids)
            return await MapOrEmptyAsync(resource, ids, getDb(), ct);

        return Array.Empty<Guid>();
    }

    /// <summary>把一组实体 id 映射成去重后的项目 id 集合（查不到/无项目归属的 id 忽略）。</summary>
    private static async Task<IReadOnlyList<Guid>> MapOrEmptyAsync(ProjectResource resource, IReadOnlyList<Guid> ids,
        TestDbContext db, CancellationToken ct)
    {
        var set = new HashSet<Guid>();
        foreach (var id in ids)
            if (await MapEntityToProjectAsync(resource, id, db, ct) is { } pid)
                set.Add(pid);
        return set.ToList();
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> values, string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var raw) && Guid.TryParse(raw?.ToString(), out var id) && id != Guid.Empty)
                return id;
        return null;
    }

    private static readonly string[] ProjectIdRouteKeys = ["projectId"];
    private static readonly string[] ResourceIdRouteKeys = ["id"];

    /// <summary>按"资源种类 + 路由/参数 id"反查所属项目；查不到（id 非法/实体不存在）返回 null。</summary>
    private static async Task<Guid?> ResolveByResourceAsync(ProjectResource resource, HttpContext http,
        TestDbContext db, CancellationToken ct)
    {
        // A. 多数 by-id 端点的路由 id
        if (ReadGuid(http.Request.RouteValues, ResourceIdRouteKeys) is { } id)
            return await MapEntityToProjectAsync(resource, id, db, ct);

        // B. 资源特有的路由键（间接层级）
        switch (resource)
        {
            // 审批端点用 {attemptId}：AgentAttempt → Execution → TestCase.ProjectId
            case ProjectResource.Execution when ReadGuid(http.Request.RouteValues, AttemptIdRouteKeys) is { } attemptId:
            {
                var execId = await db.AgentAttempts.AsNoTracking().Where(a => a.Id == attemptId)
                    .Select(a => (Guid?)a.ExecutionId).FirstOrDefaultAsync(ct);
                return execId is { } eid ? await MapEntityToProjectAsync(ProjectResource.Execution, eid, db, ct) : null;
            }
            // 轮次端点用 {roundId}：TestPlanRound → PlanId → TestPlan.ProjectId
            case ProjectResource.TestPlan when ReadGuid(http.Request.RouteValues, RoundIdRouteKeys) is { } roundId:
            {
                var planId = await db.TestPlanRounds.AsNoTracking().Where(r => r.Id == roundId)
                    .Select(r => (Guid?)r.PlanId).FirstOrDefaultAsync(ct);
                return planId is { } pid ? await MapEntityToProjectAsync(ProjectResource.TestPlan, pid, db, ct) : null;
            }
            // 数据集 check/attach 用 {testCaseId}
            case ProjectResource.DataSet when ReadGuid(http.Request.RouteValues, TestCaseIdRouteKeys) is { } tcId:
                return await MapEntityToProjectAsync(ProjectResource.TestCase, tcId, db, ct);
            // 视觉 /cases/{testCaseId}
            case ProjectResource.VisualBaseline when ReadGuid(http.Request.RouteValues, TestCaseIdRouteKeys) is { } tcId2:
                return await MapEntityToProjectAsync(ProjectResource.TestCase, tcId2, db, ct);
        }
        return null;
    }

    /// <summary>把一个"实体 id"映射成它所属的项目 id。资源不存在/无项目归属时返回 null。</summary>
    private static async Task<Guid?> MapEntityToProjectAsync(ProjectResource resource, Guid id,
        TestDbContext db, CancellationToken ct) => resource switch
    {
        ProjectResource.Project => id,
        // 用例：直接查；查不到再试"用例版本 id"（/api/testcases 组内 /versions/{id} 与 /versions/batch-delete 用的是版本 id）
        ProjectResource.TestCase => await db.TestCases.AsNoTracking().Where(t => t.Id == id)
                                      .Select(t => (Guid?)t.ProjectId).FirstOrDefaultAsync(ct)
                                  ?? await db.TestCaseVersions.AsNoTracking().Where(v => v.Id == id)
                                      .Join(db.TestCases, v => v.TestCaseId, t => t.Id, (v, t) => (Guid?)t.ProjectId)
                                      .FirstOrDefaultAsync(ct),
        ProjectResource.Execution => await db.Executions.AsNoTracking().Where(e => e.Id == id)
            .Select(e => e.TestCase != null ? (Guid?)e.TestCase.ProjectId : null).FirstOrDefaultAsync(ct),
        ProjectResource.TestPlan => await db.TestPlans.AsNoTracking().Where(p => p.Id == id)
            .Select(p => (Guid?)p.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.Requirement => await db.Requirements.AsNoTracking().Where(r => r.Id == id)
            .Select(r => (Guid?)r.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.Defect => await db.Defects.AsNoTracking().Where(d => d.Id == id)
            .Select(d => (Guid?)d.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.DataSet => await db.DataSets.AsNoTracking().Where(d => d.Id == id)
            .Select(d => (Guid?)d.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.TestSuite => await db.TestSuites.AsNoTracking().Where(s => s.Id == id)
            .Select(s => (Guid?)s.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.Schedule => await db.Schedules.AsNoTracking().Where(s => s.Id == id)
            .Select(s => (Guid?)s.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.Environment => await db.Environments.AsNoTracking().Where(e => e.Id == id)
            .Select(e => (Guid?)e.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.SharedStep => await db.SharedStepGroups.AsNoTracking().Where(s => s.Id == id)
            .Select(s => (Guid?)s.ProjectId).FirstOrDefaultAsync(ct),
        ProjectResource.VisualBaseline => await db.VisualBaselines.AsNoTracking().Where(b => b.Id == id)
            .Join(db.TestCases, b => b.TestCaseId, t => t.Id, (b, t) => (Guid?)t.ProjectId)
            .FirstOrDefaultAsync(ct),
        _ => null,
    };

    private static readonly string[] AttemptIdRouteKeys = ["attemptId"];
    private static readonly string[] RoundIdRouteKeys = ["roundId"];
    private static readonly string[] TestCaseIdRouteKeys = ["testCaseId"];

    /// <summary>在**已绑定**的处理器参数里找名为 <paramref name="propertyName"/> 的 Guid 属性值。</summary>
    private static Guid? FindArgGuid(EndpointFilterInvocationContext context, string propertyName)
    {
        foreach (var arg in context.Arguments)
        {
            var prop = arg?.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop?.GetValue(arg) is Guid g && g != Guid.Empty)
                return g;
        }
        return null;
    }

    /// <summary>在**已绑定**的参数里找名为 <paramref name="propertyName"/> 的集合，收集其中的 Guid 元素。</summary>
    private static List<Guid> CollectArgGuids(EndpointFilterInvocationContext context, string propertyName)
    {
        var result = new List<Guid>();
        foreach (var arg in context.Arguments)
        {
            var prop = arg?.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop?.GetValue(arg) is not IEnumerable items || items is string)
                continue;
            foreach (var item in items)
                if (item is Guid g && g != Guid.Empty)
                    result.Add(g);
        }
        return result;
    }

    /// <summary>决定端点所需的项目角色：显式元数据（<see cref="ProjectRoleMetadata"/>）优先，否则从全局权限反推。</summary>
    private static ProjectRole ResolveRequiredRole(HttpContext http)
    {
        var metadata = http.GetEndpoint()?.Metadata;

        // 显式覆盖优先（如成员管理：全局 ManageProjects 即可进入，但项目内必须 Owner）
        var explicitRole = metadata?
            .OfType<ProjectRoleMetadata>()
            .Select(m => (ProjectRole?)m.Required)
            .FirstOrDefault();
        if (explicitRole is { } er)
            return er;

        var required = metadata?
            .OfType<PermissionMetadata>()
            .Select(m => m.Required)
            .Aggregate(Permission.None, (acc, p) => acc | p) ?? Permission.None;

        // 映射规则抽在纯函数里（可单测），见 ProjectScopeRules
        return ProjectScopeRules.MinRoleFor(required);
    }
}

/// <summary>项目作用域端点所属的资源种类：用于 by-id/批量端点把"资源 id"反查成"所属项目 id"。</summary>
public enum ProjectResource
{
    /// <summary>不反查（仅靠路由/查询串/请求体 projectId）</summary>
    None = 0,
    TestCase = 1,
    Execution = 2,
    TestPlan = 3,
    Requirement = 4,
    Defect = 5,
    DataSet = 6,
    TestSuite = 7,
    Schedule = 8,
    /// <summary>路由 <c>id</c> 本身就是项目 id（/api/projects/{id}）</summary>
    Project = 9,
    /// <summary>测试环境（/api/environments）</summary>
    Environment = 10,
    /// <summary>共享步骤组（/api/shared-steps）</summary>
    SharedStep = 11,
    /// <summary>视觉基线（/api/visual/baselines，经 TestCase 归属项目）</summary>
    VisualBaseline = 12,
}

/// <summary>端点元数据：声明该组按哪种资源反查项目 id。</summary>
public sealed record ProjectScopeMetadata(ProjectResource Resource);

/// <summary>
/// 端点元数据：**显式覆盖**该端点所需的项目角色（优先于从 <see cref="PermissionMetadata"/> 反推）。
/// 用于「全局权限与该端点所需项目角色不对应」的场景，例如成员管理（全局 ManageProjects 即可进入，
/// 但项目内必须是 Owner）。
/// </summary>
public sealed record ProjectRoleMetadata(ProjectRole Required);

public static class ProjectScopeEndpointExtensions
{
    /// <summary>
    /// 给项目作用域的端点组挂上 <see cref="ProjectScopeFilter"/>。
    /// <paramref name="resource"/> 声明该组 by-id / 批量端点按哪种资源反查项目（None=不反查）。
    /// 只对"能解析出 projectId"的端点生效；其余自动放行，由处理器兜底。
    /// </summary>
    public static RouteGroupBuilder WithProjectScope(this RouteGroupBuilder group,
        ProjectResource resource = ProjectResource.None)
    {
        group.WithMetadata(new ProjectScopeMetadata(resource));
        // 用 DI 解析过滤器（ILogger 由容器注入）；组级挂载，组内每个端点自动生效。
        group.AddEndpointFilter<ProjectScopeFilter>();
        return group;
    }

    /// <summary>显式声明端点所需的项目角色（覆盖从全局权限反推的结果）。</summary>
    public static RouteHandlerBuilder WithProjectRole(this RouteHandlerBuilder builder, ProjectRole required)
    {
        builder.WithMetadata(new ProjectRoleMetadata(required));
        return builder;
    }
}
