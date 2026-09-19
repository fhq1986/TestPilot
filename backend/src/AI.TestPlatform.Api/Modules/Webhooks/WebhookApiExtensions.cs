using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.CI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Webhooks;

// CI Webhook 触发：独立 X-Webhook-Token 保护（不经 JWT，token 读 DB、空回退 appsettings）
// - 用例范围：testCaseIds 或 projectId(+module/priority) 二选一
// - 构建上下文：commitSha/branch/buildNumber/source，可放宽放请求体，或走 X-Commit-Sha 等请求头
// - 同步等待：?wait=true&timeoutSeconds=600 会阻塞到全部执行结束，返回 Success 供 CI 判成败
public static class WebhookApiExtensions
{
    /// <summary>同步等待的最长等待时间（秒），超过则返回未完成状态，避免 CI 任务被无限挂起</summary>
    private const int MaxWaitSeconds = 1800;

    /// <summary>
    /// 同时挂着的同步等待（wait=true）连接上限。
    ///
    /// 每个 wait 连接每 3 秒查一次执行状态、最长挂 30 分钟——CI 风暴（几十条流水线同时收尾）
    /// 会变成一群低速长查询持续压库。超限的触发请求本身不受影响（执行照常创建），
    /// 只是让客户端改走「无 wait + 自己轮询」的路径，那是同等信息量、更低成本的用法。
    /// </summary>
    private static readonly SemaphoreSlim WaitGate = new(20);

    /// <summary>单次触发的用例上限</summary>
    private const int MaxCases = 200;

    public static RouteGroupBuilder MapWebhookApi(this RouteGroupBuilder group)
    {
        group.MapPost("/executions", async (
            WebhookTriggerRequest request,
            IValidator<WebhookTriggerRequest> validator,
            IConfiguration configuration,
            SettingsService settings,
            HttpContext http,
            TestDbContext db,
            ExecutionPlanner planner,
            ExecutionQueue queue,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;

            // ---- 鉴权：系统级 X-Webhook-Token 或项目级 Bearer atp_ 令牌（二选一）
            var config = await settings.GetAsync(ct);
            var expected = !string.IsNullOrEmpty(config.WebhookToken)
                ? config.WebhookToken
                : configuration["Webhook:Token"];
            var provided = http.Request.Headers["X-Webhook-Token"].FirstOrDefault();
            ProjectApiToken? projectToken = null;
            Guid? effectiveProjectId = request.ProjectId;
            if (!TokenMatches(expected, provided))
            {
                var bearer = GetBearerToken(http);
                projectToken = await ApiTokenService.FindActiveAsync(db, bearer, now, ct);
                if (projectToken is null)
                    return Results.Unauthorized();

                // 令牌隐含项目：请求未指定项目时补上；指定了别的项目一律 403——
                // 这是"按项目发钥匙"的核心语义，宁可拒绝也不能让 A 项目的令牌触发 B 项目
                if (effectiveProjectId.HasValue && effectiveProjectId.Value != projectToken.ProjectId)
                    return Results.Json(new { message = "该令牌无权触发其他项目的用例" }, statusCode: 403);
                effectiveProjectId ??= projectToken.ProjectId;

                // 更新最近使用时间。ExecuteUpdate 不经变更跟踪，不会与后续 SaveChanges 打架
                await db.ProjectApiTokens.Where(t => t.Id == projectToken.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), ct);
            }

            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });

            var caseIds = await ResolveTestCaseIdsAsync(db, request, effectiveProjectId, ct);
            if (caseIds.Count == 0)
                return Results.BadRequest(new { message = "没有匹配到可执行的测试用例（仅支持 Web/API 用例）" });

            // 令牌路径 + 按用例 Id 触发：显式拒绝混入其他项目的用例（静默剔除会让人以为是"用例不存在"）
            if (projectToken is not null && request.TestCaseIds is { Count: > 0 })
            {
                var hasForeign = await db.TestCases.AsNoTracking()
                    .AnyAsync(t => caseIds.Contains(t.Id) && t.ProjectId != projectToken.ProjectId, ct);
                if (hasForeign)
                    return Results.Json(new { message = "该令牌无权触发其他项目的用例" }, statusCode: 403);
            }

            // ---- 构建上下文：请求体优先，其次请求头，最后给默认值
            var commitSha = Pick(request.CommitSha, Header(http, "X-Commit-Sha", "X-Git-Sha", "X-Gitlab-Ci-Commit-Sha"));
            var branch = CleanBranch(Pick(request.Branch, Header(http, "X-Branch", "X-Git-Ref", "X-Gitlab-Ci-Ref-Name")));
            var buildNumber = Pick(request.BuildNumber,
                Header(http, "X-Build-Number", "X-Github-Run-Id", "X-Gitlab-Ci-Pipeline-Id", "X-Build-Id"));
            var source = Pick(request.Source,
                Header(http, "X-CI-Source", "X-CI-Provider"))
                // 项目令牌触发且未显式声明来源时，把令牌名记进执行来源——审计时能对上"哪把钥匙触发的"
                ?? (projectToken is not null ? $"ci:{projectToken.Name}" : "ci");

            var executionIds = new List<Guid>();
            var plan = await planner.PlanAsync(
                caseIds, request.EnvironmentId, request.Browsers,
                expandDataSets: true, request.Variables,
                TriggerType.CIWebhook, Trim(source, 100), triggeredById: null,
                commitSha: Trim(commitSha, 64),
                branch: Trim(branch, 200),
                buildNumber: Trim(buildNumber, 100),
                ct: ct);
            db.Executions.AddRange(plan.Executions);
            await db.SaveChangesAsync(ct);
            foreach (var execution in plan.Executions)
            {
                await queue.EnqueueAsync(execution.Id, ct);
                executionIds.Add(execution.Id);
            }

            var wait = string.Equals(http.Request.Query["wait"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            if (!wait)
            {
                var snapshot = await BuildResponseAsync(db, executionIds, completed: false, 0, ct);
                return Results.Accepted(null, snapshot);
            }

            // 同步等待是"长连接低速轮询"，要有并发上限（见 WaitGate 注释）。
            // TryWait(0)：拿不到名额立即 429，绝不让 CI 请求排队干等——排队意味着构建白白多挂 30 分钟。
            if (!await WaitGate.WaitAsync(0, ct))
                return Results.Json(new
                {
                    message = "同步等待并发已达上限，请改用无 wait 触发 + 自行轮询执行状态",
                }, statusCode: 429);

            try
            {
                var timeoutSeconds = ParseTimeout(http.Request.Query["timeoutSeconds"].FirstOrDefault());
                var startedAt = DateTime.UtcNow;
                // 释放跟踪，确保每轮都能读到执行器写入的最新状态
                db.ChangeTracker.Clear();

                while (!ct.IsCancellationRequested)
                {
                    var elapsed = DateTime.UtcNow - startedAt;
                    if (elapsed.TotalSeconds >= timeoutSeconds)
                        return Results.Ok(await BuildResponseAsync(db, executionIds, completed: false, (int)elapsed.TotalMilliseconds, ct));

                    var statuses = await db.Executions.AsNoTracking()
                        .Where(e => executionIds.Contains(e.Id))
                        .Select(e => e.Status)
                        .ToListAsync(ct);
                    if (statuses.Count == executionIds.Count &&
                        statuses.All(s => s is not (ExecutionStatus.Pending or ExecutionStatus.Running)))
                        return Results.Ok(await BuildResponseAsync(db, executionIds, completed: true, (int)elapsed.TotalMilliseconds, ct));

                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }

                return Results.Ok(await BuildResponseAsync(db, executionIds, completed: false,
                    (int)(DateTime.UtcNow - startedAt).TotalMilliseconds, ct));
            }
            finally
            {
                WaitGate.Release();
            }
        });
        return group;
    }

    // ---------------------------------------------------------------- 辅助

    private static async Task<List<Guid>> ResolveTestCaseIdsAsync(
        TestDbContext db, WebhookTriggerRequest request, Guid? projectId, CancellationToken ct)
    {
        if (request.TestCaseIds is { Count: > 0 })
        {
            var requested = request.TestCaseIds.Distinct().Take(MaxCases).ToList();
            var query = db.TestCases.AsNoTracking()
                .Where(t => requested.Contains(t.Id) && t.Type != TestType.Mobile);
            if (request.ExcludeFlaky) query = query.Where(t => !t.IsFlaky);
            return await query.Select(t => t.Id).ToListAsync(ct);
        }

        if (projectId is null) return new List<Guid>();

        var scoped = db.TestCases.AsNoTracking()
            .Where(t => t.ProjectId == projectId.Value && t.Type != TestType.Mobile);
        if (!string.IsNullOrWhiteSpace(request.Module))
            scoped = scoped.Where(t => t.Module == request.Module);
        if (!string.IsNullOrWhiteSpace(request.Priority))
            scoped = scoped.Where(t => t.Priority == request.Priority);
        if (request.ExcludeFlaky)
            scoped = scoped.Where(t => !t.IsFlaky);

        return await scoped.OrderBy(t => t.Name).Select(t => t.Id).Take(MaxCases).ToListAsync(ct);
    }

    private static async Task<CiTriggerResponse> BuildResponseAsync(
        TestDbContext db, List<Guid> executionIds, bool completed, int durationMs, CancellationToken ct)
    {
            var rows = await db.Executions.AsNoTracking()
            .Where(e => executionIds.Contains(e.Id))
            .Select(e => new CiExecutionResult(
                e.Id,
                e.TestCaseId,
                e.TestCase != null ? e.TestCase.Name : "(用例已删除)",
                e.Status,
                e.DurationMs,
                e.AIDiagnosis,
                e.TestCase != null && e.TestCase.IsFlaky,
                e.BrowserName,
                e.DataSetRowLabel))
            .ToListAsync(ct);

        int Count(ExecutionStatus s) => rows.Count(r => r.Status == s);
        var passed = Count(ExecutionStatus.Passed);
        var failed = Count(ExecutionStatus.Failed);
        var error = Count(ExecutionStatus.Error);
        var skipped = Count(ExecutionStatus.Skipped);
        var pending = Count(ExecutionStatus.Pending) + Count(ExecutionStatus.Running);

        return new CiTriggerResponse(
            Success: completed && failed == 0 && error == 0 && pending == 0,
            Completed: completed,
            Created: executionIds.Count,
            Passed: passed, Failed: failed, Error: error, Skipped: skipped, Pending: pending,
            DurationMs: durationMs,
            Executions: rows);
    }

    private static string? Pick(string? body, string? header)
        => !string.IsNullOrWhiteSpace(body) ? body : (string.IsNullOrWhiteSpace(header) ? null : header);

    private static string? Header(HttpContext http, params string[] names)
    {
        foreach (var name in names)
        {
            var value = http.Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>取 Authorization: Bearer 后面的令牌（仅认 Bearer 方案）</summary>
    private static string? GetBearerToken(HttpContext http)
    {
        var raw = http.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        const string scheme = "Bearer ";
        return raw.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? raw[scheme.Length..].Trim()
            : null;
    }

    /// <summary>GitLab 的 ref 形如 refs/heads/main，统一裁剪为短分支名</summary>
    private static string? CleanBranch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var branch = value.Trim();
        const string heads = "refs/heads/";
        const string tags = "refs/tags/";
        if (branch.StartsWith(heads, StringComparison.OrdinalIgnoreCase)) branch = branch[heads.Length..];
        else if (branch.StartsWith(tags, StringComparison.OrdinalIgnoreCase)) branch = branch[tags.Length..];
        else if (branch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase)) branch = branch.Split('/').Last();
        return branch;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static int ParseTimeout(string? raw)
    {
        const int fallback = 600;
        if (!int.TryParse(raw, out var seconds)) return fallback;
        return Math.Clamp(seconds, 5, MaxWaitSeconds);
    }

    // 常量时间比较，避免时序侧信道泄露 token
    private static bool TokenMatches(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            return false;
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(provided);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
