using System.Text;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.LoadTesting;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.LoadTesting;
using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Modules.LoadTests;

/// <summary>
/// 压测场景（k6，迭代 F·P2-9）。独立模块，不并入功能执行流水线。
/// </summary>
public static class LoadTestApiExtensions
{
    public static RouteGroupBuilder MapLoadTestApi(this RouteGroupBuilder group)
    {
        // ---------------------------------------------------------------- 场景 CRUD

        group.MapGet("/", async (TestDbContext db, CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] string? keyword = null,
            [FromQuery] int? source = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.LoadTestScenarios.AsNoTracking();
            if (projectId is { } pid) query = query.Where(s => s.ProjectId == pid);
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(s => s.Name.Contains(keyword) ||
                    (s.Description != null && s.Description.Contains(keyword)));
            if (source is { } src) query = query.Where(s => (int)s.Source == src);

            var total = await query.CountAsync(ct);

            // 先投影成匿名对象再内存映射：record 的位置参数直接进 SQL 投影会踩
            // 「属性名与列名不匹配」的坑（见项目既有约定）
            var rows = await query
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.ProjectId,
                    ProjectName = s.Project != null ? s.Project.Name : null,
                    s.Name,
                    s.Description,
                    Source = (int)s.Source,
                    s.VirtualUsers,
                    s.DurationSeconds,
                    CaseCount = s.Cases.Count,
                    s.ScriptHash,
                    s.ScriptGeneratedAt,
                    s.CreatedById,
                    s.CreatedAt,
                    s.UpdatedAt,
                    LastRun = s.Runs.OrderByDescending(r => r.CreatedAt)
                        .Select(r => new { Status = (int)r.Status, r.CreatedAt, r.P95Ms, r.ErrorRate })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var items = rows.Select(s => new LoadTestScenarioSummaryDto(
                s.Id, s.ProjectId, s.ProjectName, s.Name, s.Description, s.Source,
                s.VirtualUsers, s.DurationSeconds, s.CaseCount, s.ScriptHash, s.ScriptGeneratedAt,
                s.LastRun != null ? s.LastRun.Status : null,
                s.LastRun != null ? s.LastRun.CreatedAt : null,
                s.LastRun != null ? s.LastRun.P95Ms : null,
                s.LastRun != null ? s.LastRun.ErrorRate : null,
                s.CreatedById, null, s.CreatedAt, s.UpdatedAt)).ToList();

            // 创建人显示名：静态表达式树里解析不了，物化后批量回填
            var names = await UserNameResolver.ResolveAsync(db, items.Select(i => i.CreatedById), ct);
            var withNames = items.Select(i => i with { CreatedByName = names.GetName(i.CreatedById) }).ToList();

            return Results.Ok(new PagedResult<LoadTestScenarioSummaryDto>(withNames, total, page, pageSize));
        }).WithPermission(Permission.ViewLoadTests).Produces<PagedResult<LoadTestScenarioSummaryDto>>();

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var detail = await LoadDetailAsync(db, id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).WithPermission(Permission.ViewLoadTests).Produces<LoadTestScenarioDetailDto>();

        group.MapPost("/", async (CreateLoadTestScenarioRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["名称不能为空"] });

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["projectId"] = ["项目不存在"] });

            if (await db.LoadTestScenarios.AsNoTracking()
                    .AnyAsync(s => s.ProjectId == request.ProjectId && s.Name == name, ct))
                return DuplicateName(name);

            var scenario = new LoadTestScenario
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                Name = name,
                Description = Trim(request.Description, 1000),
                Source = request.Source,
                EnvironmentId = request.EnvironmentId,
                TargetBaseUrl = Trim(request.TargetBaseUrl, 500),
                ApiDefinitionId = request.ApiDefinitionId,
                Operations = request.Operations ?? new List<string>(),
                Profile = request.Profile ?? new LoadTestProfile(),
                Thresholds = request.Thresholds ?? new List<LoadTestThreshold>(),
                Variables = request.Variables,
            };
            ApplyProfileSummary(scenario);
            await SyncCasesAsync(db, scenario, request.CaseIds, ct);

            db.LoadTestScenarios.Add(scenario);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/loadtests/{scenario.Id}",
                await LoadDetailAsync(db, scenario.Id, ct));
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("Create", "LoadTestScenario")
          .Produces<LoadTestScenarioDetailDto>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateLoadTestScenarioRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var scenario = await db.LoadTestScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (scenario is null) return Results.NotFound();

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["名称不能为空"] });
            if (await db.LoadTestScenarios.AsNoTracking()
                    .AnyAsync(s => s.ProjectId == scenario.ProjectId && s.Name == name && s.Id != id, ct))
                return DuplicateName(name);

            scenario.Name = name;
            scenario.Description = Trim(request.Description, 1000);
            scenario.EnvironmentId = request.EnvironmentId;
            scenario.TargetBaseUrl = Trim(request.TargetBaseUrl, 500);
            scenario.ApiDefinitionId = request.ApiDefinitionId;
            scenario.Operations = request.Operations ?? new List<string>();
            scenario.Profile = request.Profile ?? scenario.Profile;
            scenario.Thresholds = request.Thresholds ?? new List<LoadTestThreshold>();
            scenario.Variables = request.Variables;
            ApplyProfileSummary(scenario);
            await SyncCasesAsync(db, scenario, request.CaseIds, ct);

            // 选择集/负载变了，旧脚本就过期了——清掉哈希让界面提示重新生成
            scenario.ScriptHash = null;
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDetailAsync(db, id, ct));
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("Update", "LoadTestScenario")
          .Produces<LoadTestScenarioDetailDto>();

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db,
            IArtifactStore store, CancellationToken ct) =>
        {
            var scenario = await db.LoadTestScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (scenario is null) return Results.NotFound();

            // 产物按 runId 分目录，先取出来再删记录，否则删完就找不到 key 了
            var runIds = await db.LoadTestRuns.AsNoTracking()
                .Where(r => r.ScenarioId == id).Select(r => r.Id).ToListAsync(ct);

            db.LoadTestScenarios.Remove(scenario);
            await db.SaveChangesAsync(ct);

            foreach (var runId in runIds)
            {
                try { await store.DeletePrefixAsync(LoadTestStorage.RunPrefix(runId), ct); }
                catch { /* 产物清理失败不该让删除接口失败 */ }
            }

            return Results.NoContent();
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("Delete", "LoadTestScenario");

        group.MapPost("/batch-delete", async (BatchDeleteRequest request, TestDbContext db,
            IArtifactStore store, CancellationToken ct) =>
        {
            var ids = request.Ids;
            if (ids.Count == 0) return Results.Ok(new BatchDeleteResultDto(0, new List<BatchDeleteSkippedItem>()));

            var runIds = await db.LoadTestRuns.AsNoTracking()
                .Where(r => ids.Contains(r.ScenarioId)).Select(r => r.Id).ToListAsync(ct);
            var scenarios = await db.LoadTestScenarios.Where(s => ids.Contains(s.Id)).ToListAsync(ct);

            db.LoadTestScenarios.RemoveRange(scenarios);
            await db.SaveChangesAsync(ct);

            foreach (var runId in runIds)
            {
                try { await store.DeletePrefixAsync(LoadTestStorage.RunPrefix(runId), ct); }
                catch { /* 同上 */ }
            }

            return Results.Ok(new BatchDeleteResultDto(scenarios.Count, new List<BatchDeleteSkippedItem>()));
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("BatchDelete", "LoadTestScenario")
          .Produces<BatchDeleteResultDto>();

        // ---------------------------------------------------------------- 脚本

        group.MapPost("/{id:guid}/generate", async (Guid id, TestDbContext db,
            LoadTestScriptBuilder builder, CancellationToken ct) =>
        {
            var scenario = await db.LoadTestScenarios
                .Include(s => s.Environment)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (scenario is null) return Results.NotFound();

            var generated = await RegenerateAsync(db, scenario, builder, ct);
            return Results.Ok(new GenerateScriptResultDto(generated.Script, generated.Hash,
                generated.Warnings.ToList()));
        }).WithPermission(Permission.ManageLoadTests)
          .Produces<GenerateScriptResultDto>();

        group.MapGet("/{id:guid}/script", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var script = await db.LoadTestScenarios.AsNoTracking()
                .Where(s => s.Id == id).Select(s => s.ScriptText).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(script)) return Results.NotFound();
            return Results.Text(script, "text/javascript", Encoding.UTF8);
        }).WithPermission(Permission.ViewLoadTests);

        // ---------------------------------------------------------------- 运行

        group.MapPost("/{id:guid}/run", async (Guid id, TestDbContext db, ICurrentUser user,
            LoadTestScriptBuilder builder, LoadTestQueue queue, IArtifactStore store,
            IOptions<LoadTestOptions> options, CancellationToken ct) =>
        {
            var opts = options.Value;
            if (!opts.Enabled)
                return Results.Problem("压测执行已禁用（LoadTest:Enabled=false）",
                    statusCode: StatusCodes.Status409Conflict);

            var scenario = await db.LoadTestScenarios
                .Include(s => s.Environment)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (scenario is null) return Results.NotFound();

            // 上限校验放在入队前：事后发现「跑飞了」没有意义
            var peakVus = PeakVus(scenario.Profile);
            if (peakVus > opts.MaxVus)
                return Results.Problem($"虚拟用户数 {peakVus} 超过上限 {opts.MaxVus}",
                    statusCode: StatusCodes.Status400BadRequest);
            if (scenario.DurationSeconds > opts.MaxDurationSeconds)
                return Results.Problem($"时长 {scenario.DurationSeconds}s 超过上限 {opts.MaxDurationSeconds}s",
                    statusCode: StatusCodes.Status400BadRequest);

            // 同项目已有进行中的运行：并发压测会让两边的数字都不可解释
            if (await db.LoadTestRuns.AsNoTracking().AnyAsync(r =>
                    r.ProjectId == scenario.ProjectId &&
                    (r.Status == ExecutionStatus.Pending || r.Status == ExecutionStatus.Running), ct))
                return Results.Problem("该项目已有进行中的压测运行，请等待其结束",
                    statusCode: StatusCodes.Status409Conflict);

            // 脚本为空或已被改动（哈希为空表示选择集变过）就自动重新生成，
            // 省掉「点了运行才发现没脚本」的一次往返
            if (string.IsNullOrWhiteSpace(scenario.ScriptText) || string.IsNullOrWhiteSpace(scenario.ScriptHash))
                await RegenerateAsync(db, scenario, builder, ct);

            var runId = Guid.NewGuid();

            // 冻结脚本：场景后续被重新生成也不影响这次运行的可复现性
            var scriptKey = LoadTestStorage.ScriptKey(runId);
            await store.SaveAsync(scriptKey, Encoding.UTF8.GetBytes(scenario.ScriptText!),
                "text/javascript", ct);

            var run = new LoadTestRun
            {
                Id = runId,
                ScenarioId = scenario.Id,
                ProjectId = scenario.ProjectId,
                Status = ExecutionStatus.Pending,
                TriggerType = TriggerType.Manual,
                TriggeredById = user.Id,
                TargetBaseUrl = builder.ResolveBaseUrl(scenario),
                TimeoutSeconds = scenario.DurationSeconds + opts.OverheadSeconds,
                ScriptHash = scenario.ScriptHash,
                ScriptArtifactKey = scriptKey,
            };
            db.LoadTestRuns.Add(run);
            await db.SaveChangesAsync(ct);

            await queue.EnqueueAsync(runId, ct);
            return Results.Accepted($"/api/loadtests/runs/{runId}", new { runId });
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("Run", "LoadTestRun");

        group.MapGet("/{id:guid}/runs", async (Guid id, TestDbContext db, CancellationToken ct,
            [FromQuery] int take = 20) =>
        {
            take = take is < 1 ? 20 : take > 100 ? 100 : take;
            var items = await db.LoadTestRuns.AsNoTracking()
                .Where(r => r.ScenarioId == id)
                .OrderByDescending(r => r.CreatedAt)
                .Take(take)
                .Select(r => new
                {
                    r.Id, r.ScenarioId, r.Status, r.TargetBaseUrl, r.StartedAt, r.EndedAt,
                    r.DurationMs, r.TotalRequests, r.Rps, r.P95Ms, r.P99Ms, r.ErrorRate,
                    r.ThresholdsPassed, r.ThresholdTotal, r.ThresholdFailed, r.ErrorMessage,
                    r.CreatedAt,
                    ScenarioName = r.Scenario != null ? r.Scenario.Name : "",
                })
                .ToListAsync(ct);

            return Results.Ok(items.Select(r => new LoadTestRunSummaryDto(
                r.Id, r.ScenarioId, r.ScenarioName, (int)r.Status, r.TargetBaseUrl,
                r.StartedAt, r.EndedAt, r.DurationMs, r.TotalRequests, r.Rps, r.P95Ms, r.P99Ms,
                r.ErrorRate, r.ThresholdsPassed, r.ThresholdTotal, r.ThresholdFailed,
                r.ErrorMessage, r.CreatedAt)).ToList());
        }).WithPermission(Permission.ViewLoadTests).Produces<List<LoadTestRunSummaryDto>>();

        group.MapGet("/runs/{runId:guid}", async (Guid runId, TestDbContext db, CancellationToken ct) =>
        {
            var r = await db.LoadTestRuns.AsNoTracking()
                .Include(x => x.Scenario)
                .FirstOrDefaultAsync(x => x.Id == runId, ct);
            if (r is null) return Results.NotFound();

            return Results.Ok(new LoadTestRunDetailDto(
                r.Id, r.ScenarioId, r.Scenario?.Name ?? "", r.ProjectId, (int)r.Status,
                r.TargetBaseUrl, r.StartedAt, r.EndedAt, r.DurationMs, r.K6Version, r.ExitCode,
                r.ErrorMessage, r.TotalRequests, r.Rps, r.AvgMs, r.P50Ms, r.P95Ms, r.P99Ms,
                r.MaxMs, r.ErrorRate, r.ChecksRate, r.Iterations, r.VusMax,
                r.ThresholdsPassed, r.ThresholdTotal, r.ThresholdFailed, r.ThresholdResults,
                !string.IsNullOrWhiteSpace(r.SummaryArtifactKey),
                !string.IsNullOrWhiteSpace(r.LogArtifactKey),
                r.CreatedAt));
        }).WithPermission(Permission.ViewLoadTests).Produces<LoadTestRunDetailDto>();

        // 原始 summary / 日志：受权端点流式返回（与 GET /executions/{id}/trace 同一写法）
        group.MapGet("/runs/{runId:guid}/summary", async (Guid runId, TestDbContext db,
            IArtifactStore store, CancellationToken ct) =>
        {
            var key = await db.LoadTestRuns.AsNoTracking()
                .Where(r => r.Id == runId).Select(r => r.SummaryArtifactKey).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(key)) return Results.NotFound();
            var bytes = await store.ReadAsync(key, ct);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "application/json", "summary.json");
        }).WithPermission(Permission.ViewLoadTests);

        group.MapGet("/runs/{runId:guid}/log", async (Guid runId, TestDbContext db,
            IArtifactStore store, CancellationToken ct) =>
        {
            var key = await db.LoadTestRuns.AsNoTracking()
                .Where(r => r.Id == runId).Select(r => r.LogArtifactKey).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(key)) return Results.NotFound();
            var bytes = await store.ReadAsync(key, ct);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "text/plain", "k6.log");
        }).WithPermission(Permission.ViewLoadTests);

        group.MapPost("/runs/{runId:guid}/cancel", async (Guid runId, TestDbContext db,
            CancellationToken ct) =>
        {
            var run = await db.LoadTestRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null) return Results.NotFound();
            if (run.Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
                return Results.Problem("该运行已结束", statusCode: StatusCodes.Status409Conflict);

            // 先试进程内取消（正在跑的那条会走协作取消并落 Canceled）；
            // 不在本进程（多实例/未抢占）时返回 false，直接改库落终态——
            // 否则一条 Pending 记录会永远等不到它的执行器
            if (!LoadTestWorker.RequestCancel(runId))
            {
                run.Status = ExecutionStatus.Canceled;
                run.ErrorMessage = "已取消";
                run.EndedAt ??= DateTime.UtcNow;
                run.ClaimedBy = null;
                run.HeartbeatAt = null;
                await db.SaveChangesAsync(ct);
            }

            return Results.Accepted();
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("Cancel", "LoadTestRun");

        // ---------------------------------------------------------------- OpenAPI 导入

        group.MapPost("/import-openapi", async (ImportOpenApiRequest request, TestDbContext db,
            SwaggerImporter importer, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Spec))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["spec"] = ["文档内容不能为空"] });

            string apiName;
            string? baseUrl;
            List<ApiEndpointSpec> endpoints;
            try
            {
                (apiName, baseUrl, endpoints) = importer.Parse(request.Spec);
            }
            catch (Exception ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["spec"] = [$"OpenAPI 文档解析失败：{ex.Message}"],
                });
            }

            var definition = new ApiDefinition
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? apiName : request.Name.Trim(),
                Spec = request.Spec,
            };
            db.ApiDefinitions.Add(definition);
            await db.SaveChangesAsync(ct);

            var operations = endpoints
                .Select(e => new OpenApiOperationDto(e.Method.ToUpperInvariant(), e.Path,
                    $"{e.Method.ToUpperInvariant()} {e.Path}"))
                .ToList();

            return Results.Ok(new ImportOpenApiResultDto(definition.Id, definition.Name,
                baseUrl ?? string.Empty, operations));
        }).WithPermission(Permission.ManageLoadTests)
          .WithAudit("ImportOpenApi", "LoadTestScenario")
          .Produces<ImportOpenApiResultDto>();

        return group;
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>生成脚本并回写场景（hash 为空表示"选择集变过、脚本已过期"）</summary>
    private static async Task<K6GenerateResult> RegenerateAsync(TestDbContext db,
        LoadTestScenario scenario, LoadTestScriptBuilder builder, CancellationToken ct)
    {
        var input = await builder.BuildAsync(scenario, ct);
        var generated = K6ScriptGenerator.Generate(input);

        scenario.ScriptText = generated.Script;
        scenario.ScriptHash = generated.Hash;
        scenario.ScriptGeneratedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return generated;
    }

    /// <summary>负载配置的峰值 VU 数（用于入队前的上限校验）</summary>
    private static int PeakVus(LoadTestProfile p) => p.Kind switch
    {
        "constant-vus" => Math.Max(1, p.Vus),
        "constant-arrival-rate" => Math.Max(1, p.MaxVUs),
        _ => p.Stages.Count > 0 ? Math.Max(1, p.Stages.Max(s => s.Target)) : 1,
    };

    private static IResult DuplicateName(string name) =>
        Results.Problem($"已存在同名压测场景「{name}」", statusCode: StatusCodes.Status409Conflict);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>把负载配置里的两个关键值冗余到实体列上（列表展示 + 上限校验都要用）</summary>
    private static void ApplyProfileSummary(LoadTestScenario scenario)
    {
        var p = scenario.Profile;
        scenario.VirtualUsers = PeakVus(p);
        scenario.DurationSeconds = ParseDurationSeconds(p.Kind == "ramping-vus"
            ? $"{Math.Max(1, p.Stages.Sum(s => ParseDurationSeconds(s.Duration)))}s"
            : p.Duration);
    }

    /// <summary>解析 k6 duration 字面量（如 30s / 1m / 1m30s）成秒数；解析不出按 60s 兜底</summary>
    internal static int ParseDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return 60;
        var matches = System.Text.RegularExpressions.Regex.Matches(duration, @"(\d+)([smh])");
        if (matches.Count == 0) return 60;

        var seconds = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var value = int.Parse(m.Groups[1].Value);
            seconds += m.Groups[2].Value switch
            {
                "s" => value,
                "m" => value * 60,
                "h" => value * 3600,
                _ => 0,
            };
        }
        return Math.Max(1, seconds);
    }

    /// <summary>整体覆盖场景的用例选择（差异比对在几十条量级上没有收益）</summary>
    private static async Task SyncCasesAsync(TestDbContext db, LoadTestScenario scenario,
        List<Guid>? caseIds, CancellationToken ct)
    {
        var existing = await db.LoadTestScenarioCases
            .Where(c => c.ScenarioId == scenario.Id).ToListAsync(ct);
        if (existing.Count > 0) db.LoadTestScenarioCases.RemoveRange(existing);

        var order = 0;
        foreach (var caseId in (caseIds ?? new List<Guid>()).Distinct())
            db.LoadTestScenarioCases.Add(new LoadTestScenarioCase
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                TestCaseId = caseId,
                Order = order++,
            });
    }

    private static async Task<LoadTestScenarioDetailDto?> LoadDetailAsync(TestDbContext db, Guid id,
        CancellationToken ct)
    {
        var s = await db.LoadTestScenarios.AsNoTracking()
            .Include(x => x.Project)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;

        var caseIds = await db.LoadTestScenarioCases.AsNoTracking()
            .Where(c => c.ScenarioId == id).OrderBy(c => c.Order)
            .Select(c => c.TestCaseId).ToListAsync(ct);

        var names = await UserNameResolver.ResolveAsync(db, new[] { s.CreatedById }, ct);

        return new LoadTestScenarioDetailDto(
            s.Id, s.ProjectId, s.Project?.Name, s.Name, s.Description, (int)s.Source,
            s.EnvironmentId, s.TargetBaseUrl, s.ApiDefinitionId,
            s.Operations, s.Profile, s.Thresholds,
            s.Variables ?? new Dictionary<string, string>(),
            s.VirtualUsers, s.DurationSeconds, s.ScriptText, s.ScriptHash, s.ScriptGeneratedAt,
            caseIds, s.CreatedById, names.GetName(s.CreatedById), s.CreatedAt, s.UpdatedAt);
    }
}
