using System.Collections.Concurrent;
using System.Text;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Hubs;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 压测运行器（迭代 F·P2-9）。
///
/// 结构**刻意照抄 <see cref="ExecutionWorker"/>**：同样是「DB 抢占队列 + 心跳 + 僵尸回收」，
/// 因为这套机制已经被多实例、进程重启、任务丢失等场景验证过。压测只是换了执行体（k6 子进程），
/// 队列语义完全一致——另起一套机制只会多一处会出问题的地方。
/// </summary>
public class LoadTestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LoadTestOptions _options;
    private readonly LoadTestQueue _queue;
    private readonly IK6ProcessRunner _runner;
    private readonly IHubContext<ExecutionHub> _hub;
    private readonly ILogger<LoadTestWorker> _logger;

    /// <summary>本进程内「正在跑的运行 → 取消源」。跨进程取消靠 DB 兜底（见 RequestCancel 注释）</summary>
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> Cancels = new();

    public LoadTestWorker(IServiceScopeFactory scopeFactory, IOptions<LoadTestOptions> options,
        LoadTestQueue queue, IK6ProcessRunner runner, IHubContext<ExecutionHub> hub,
        ILogger<LoadTestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _queue = queue;
        _runner = runner;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// 实例标识：与执行器同一套约定（"{NodeName}:{Pid}"），节点看板按冒号前的前缀统计。
    /// 压测的 ClaimedBy 也用它，便于在同一个节点视图里看到两类负载来自哪个进程。
    /// </summary>
    private static string InstanceId => $"{ExecutionNodeRegistry.NodeName}:{System.Environment.ProcessId}";

    /// <summary>
    /// 请求取消本进程内正在跑的压测。返回 false 表示该运行不在本进程
    /// （多实例部署时它可能被别的实例持有）——调用方据此回退到「直接改库落 Canceled」。
    /// </summary>
    public static bool RequestCancel(Guid runId)
    {
        if (!Cancels.TryGetValue(runId, out var cts)) return false;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* 刚好跑完并释放了，等价于取消失败，交给 DB 兜底 */ }
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("压测已禁用（LoadTest:Enabled=false），运行器不启动");
            return;
        }

        _logger.LogInformation("压测运行器启动：实例 {Instance}｜并发 {Concurrency}｜轮询 {Poll}s",
            InstanceId, _options.EffectiveConcurrency, _options.PollSeconds);

        var running = new List<Task>();
        var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.HeartbeatSeconds)));
        var pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds)));

        try
        {
            await ReapStaleAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(t => t.IsCompleted);

                var slots = _options.EffectiveConcurrency - running.Count;
                if (slots > 0)
                {
                    var claimed = await ClaimPendingAsync(slots, stoppingToken);
                    foreach (var runId in claimed)
                    {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        Cancels[runId] = cts;
                        running.Add(Task.Run(() => RunSafelyAsync(runId, cts), CancellationToken.None));
                    }
                }

                // 有槽位就等轮询（新任务可能刚被别的实例写入），满载则等任一任务结束
                var tick = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds)), stoppingToken);
                var wake = _queue.WaitToReadAsync(stoppingToken).AsTask();
                var done = running.Count > 0 ? Task.WhenAny(running) : Task.Delay(Timeout.Infinite, stoppingToken);

                var finished = await Task.WhenAny(tick, wake, done);
                if (finished == wake)
                {
                    _queue.Drain();
                }
                else if (finished == done)
                {
                    // 等心跳周期再扫一次，避免任务结束后立刻空转
                    await heartbeatTimer.WaitForNextTickAsync(stoppingToken);
                    await RefreshHeartbeatAsync(stoppingToken);
                    await ReapStaleAsync(stoppingToken);
                }
                else
                {
                    await RefreshHeartbeatAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }

        // 停机时把在跑的子进程收掉，否则它们会变成孤儿继续压被测系统
        foreach (var (runId, cts) in Cancels)
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            _logger.LogWarning("停机：已请求终止压测运行 {RunId}", runId);
        }

        try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { /* 超时就算了，进程会带走子进程 */ }
    }

    /// <summary>抢占待运行记录：与执行器同一套 UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING</summary>
    private async Task<List<Guid>> ClaimPendingAsync(int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        try
        {
            const string sql = """
                UPDATE "LoadTestRuns"
                SET "Status" = 1, "StartedAt" = now(), "ClaimedBy" = {0}, "HeartbeatAt" = now()
                WHERE "Id" IN (
                    SELECT r."Id" FROM "LoadTestRuns" r
                    WHERE r."Status" = 0
                    ORDER BY r."CreatedAt"
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING "Id"
                """;
            return await db.Database.SqlQueryRaw<Guid>(sql, InstanceId, take).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抢占待运行压测失败");
            return new List<Guid>();
        }
    }

    private async Task RefreshHeartbeatAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "LoadTestRuns" SET "HeartbeatAt" = now() WHERE "ClaimedBy" = {0} AND "Status" = 1""",
                new object[] { InstanceId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新压测心跳失败");
        }
    }

    /// <summary>
    /// 回收僵尸运行。这里比执行器**更必要**：k6 是子进程，执行器重启会直接带走它，
    /// 但库里那条记录仍停在 Running，不回收就永远挂在「运行中」。
    /// </summary>
    private async Task ReapStaleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var staleMinutes = Math.Max(1, _options.StaleRunningMinutes);
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "LoadTestRuns"
                SET "Status" = 4,
                    "ErrorMessage" = COALESCE("ErrorMessage", '执行器实例心跳超时，运行状态丢失'),
                    "EndedAt" = COALESCE("EndedAt", now()),
                    "ClaimedBy" = NULL,
                    "HeartbeatAt" = NULL
                WHERE "Status" = 1
                  AND ("HeartbeatAt" IS NULL OR "HeartbeatAt" < now() - make_interval(mins => {0}))
                """,
                new object[] { staleMinutes }, ct);
            if (affected > 0)
                _logger.LogWarning("回收僵尸压测运行 {Count} 条（心跳超过 {Minutes} 分钟未更新）", affected, staleMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "回收僵尸压测运行失败");
        }
    }

    private async Task RunSafelyAsync(Guid runId, CancellationTokenSource cts)
    {
        try
        {
            await ProcessAsync(runId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            await MarkTerminalAsync(runId, ExecutionStatus.Canceled, "已取消", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "压测运行 {RunId} 处理失败", runId);
            await MarkTerminalAsync(runId, ExecutionStatus.Error, null, ex.Message);
        }
        finally
        {
            Cancels.TryRemove(runId, out _);
            cts.Dispose();
        }
    }

    private async Task ProcessAsync(Guid runId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IArtifactStore>();

        var run = await db.LoadTestRuns.Include(r => r.Scenario)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        if (run.Status != ExecutionStatus.Running)
        {
            _logger.LogWarning("压测运行 {RunId} 状态为 {Status}，跳过处理", runId, run.Status);
            return;
        }

        var workDir = Path.Combine(LoadTestStorage.WorkRoot(_options.WorkPath), runId.ToString("N"));
        Directory.CreateDirectory(workDir);
        var scriptPath = Path.Combine(workDir, "script.js");
        var summaryExportPath = Path.Combine(workDir, "summary-export.json");
        var summaryPath = Path.Combine(workDir, "summary.json");

        try
        {
            // 脚本优先取本次运行冻结的那份（场景可能已被重新生成，历史运行必须可复现）
            var script = await LoadFrozenScriptAsync(store, run, ct);
            if (string.IsNullOrWhiteSpace(script))
            {
                await MarkTerminalAsync(runId, ExecutionStatus.Error, "脚本为空，请先生成脚本", null);
                return;
            }
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), ct);

            await NotifyAsync(runId, run.ProjectId, ExecutionStatus.Running);

            var outcome = await _runner.RunAsync(new K6RunRequest(
                runId, workDir, scriptPath, summaryExportPath, summaryPath,
                run.TargetBaseUrl,
                run.Scenario?.Variables ?? new Dictionary<string, string>(),
                Math.Max(10, run.TimeoutSeconds)), ct);

            // 日志落盘（对象存储），失败排查要用
            var log = new StringBuilder()
                .AppendLine("=== k6 stdout ===").AppendLine(outcome.StdOut)
                .AppendLine("=== k6 stderr ===").AppendLine(outcome.StdErr)
                .ToString();
            run.LogArtifactKey = await SaveArtifactAsync(store, LoadTestStorage.LogKey(runId), log, "text/plain", ct);
            if (outcome.SummaryJson is not null)
                run.SummaryArtifactKey = await SaveArtifactAsync(store, LoadTestStorage.SummaryKey(runId),
                    outcome.SummaryJson, "application/json", ct);

            run.K6Version = outcome.K6Version;
            run.ExitCode = outcome.ExitCode;
            run.EndedAt = DateTime.UtcNow;
            run.DurationMs = run.StartedAt is { } started
                ? (int)Math.Max(0, (run.EndedAt.Value - started).TotalMilliseconds)
                : null;

            var parsed = K6SummaryParser.Parse(outcome.SummaryJson);
            if (parsed is not null)
            {
                run.TotalRequests = parsed.TotalRequests;
                run.Rps = parsed.Rps;
                run.AvgMs = parsed.AvgMs;
                run.P50Ms = parsed.P50Ms;
                run.P95Ms = parsed.P95Ms;
                run.P99Ms = parsed.P99Ms;
                run.MaxMs = parsed.MaxMs;
                run.ErrorRate = parsed.ErrorRate;
                run.ChecksRate = parsed.ChecksRate;
                run.Iterations = parsed.Iterations;
                run.VusMax = parsed.VusMax;
                run.ThresholdsPassed = parsed.ThresholdsPassed;
                run.ThresholdTotal = parsed.ThresholdTotal;
                run.ThresholdFailed = parsed.ThresholdFailed;
                run.ThresholdResults = parsed.ThresholdResults;
            }

            // 结论：超时 / 拿不到 summary / 退出码异常 → Error；阈值未过 → Failed；否则 Passed
            if (outcome.TimedOut || outcome.Error is not null)
            {
                run.Status = ExecutionStatus.Error;
                run.ErrorMessage = Truncate(outcome.Error ?? "运行失败", 2000);
            }
            else if (parsed is null)
            {
                // 宁可少指标也不要假指标：没有 summary 就不编数据，把日志留给用户看
                run.Status = ExecutionStatus.Error;
                run.ErrorMessage = "未获取到 k6 summary，无法解析指标（详见运行日志）";
            }
            else
            {
                run.Status = parsed.ThresholdsPassed == false ? ExecutionStatus.Failed : ExecutionStatus.Passed;
            }

            await db.SaveChangesAsync(ct);
            await NotifyAsync(runId, run.ProjectId, run.Status);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    /// <summary>读取本次运行冻结的脚本；没有（历史数据/异常）时回退到场景当前脚本</summary>
    private static async Task<string?> LoadFrozenScriptAsync(IArtifactStore store, LoadTestRun run, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(run.ScriptArtifactKey))
        {
            var bytes = await store.ReadAsync(run.ScriptArtifactKey, ct);
            if (bytes is { Length: > 0 }) return Encoding.UTF8.GetString(bytes);
        }
        return run.Scenario?.ScriptText;
    }

    private static async Task<string> SaveArtifactAsync(IArtifactStore store, string key, string content,
        string contentType, CancellationToken ct)
    {
        await store.SaveAsync(key, Encoding.UTF8.GetBytes(content), contentType, ct);
        return key;
    }

    private async Task MarkTerminalAsync(Guid runId, ExecutionStatus status, string? error, string? message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var run = await db.LoadTestRuns.FirstOrDefaultAsync(r => r.Id == runId);
            if (run is null) return;

            run.Status = status;
            run.ErrorMessage = Truncate(error ?? message, 2000);
            run.EndedAt ??= DateTime.UtcNow;
            run.ClaimedBy = null;
            run.HeartbeatAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(runId, run.ProjectId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "落压测运行 {RunId} 终态失败", runId);
        }
    }

    /// <summary>执行进度推送：复用 ExecutionHub 的组广播（组名 = runId），前端不必再认一个新 Hub</summary>
    private async Task NotifyAsync(Guid runId, Guid projectId, ExecutionStatus status)
    {
        try
        {
            await _hub.Clients.Group(runId.ToString())
                .SendAsync("StatusChanged", new { runId, projectId, status = (int)status });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送压测状态失败 runId={RunId}", runId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 清理失败不影响结论，留给维护任务兜底
        }
    }
}
