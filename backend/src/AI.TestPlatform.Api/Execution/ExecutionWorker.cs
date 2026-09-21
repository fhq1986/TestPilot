using System.Collections.Concurrent;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Hubs;
using AI.TestPlatform.Api.Modules.Defects;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 执行器：从数据库抢占待执行记录并并发执行。
///
/// 设计要点（支持水平扩展）：
/// - 任务真身是数据库里的 Pending 记录，抢占用 FOR UPDATE SKIP LOCKED，
///   多个实例同时抢占也不会拿到同一条，天然支持多实例部署；
/// - 实例内并发度由 Execution:MaxConcurrency 控制；
/// - 运行期写心跳（ClaimedBy + HeartbeatAt），启动时只清理心跳过期的僵死执行，
///   不会误杀其它实例正在跑的任务；
/// - 进程内 Channel 仅作唤醒信号，保证入队后立即响应。
/// - ClaimedBy 前缀 = 节点名（Node:Name，缺省机器名），节点看板据此统计各节点负载。
/// </summary>
public class ExecutionWorker : BackgroundService
{
    /// <summary>本实例标识，写入 Execution.ClaimedBy。节点名在构造期从注册表取（保证配置已加载）。</summary>
    private static string InstanceId =>
        $"{ExecutionNodeRegistry.NodeName}:{System.Environment.ProcessId}";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExecutionQueue _queue;
    private readonly IHubContext<ExecutionHub> _hub;
    private readonly ILogger<ExecutionWorker> _logger;
    private readonly ExecutionOptions _options;

    /// <summary>宿主停机令牌（ExecuteAsync 启动时赋值）：传递给执行任务，
    /// 停机时 DB 操作与步骤内的可取消等待（Task.Delay 等）能立即响应，不再靠 30s 硬等。</summary>
    private CancellationToken _stopping;

    /// <summary>
    /// 运行中执行的用户取消令牌源（executionId → CTS）。手动终止走协作取消：
    /// 取消端点 Cancel 令牌 → 正在跑的 Playwright 步骤立即中断 → 执行器落 Canceled 终态。
    /// 仅覆盖本实例内的执行；多实例部署时其它实例上的执行由取消端点直接落库（执行器
    /// 的终态回写带 Status=Running 条件，迟到结果不会覆盖该结论）。
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> Cancels = new();

    /// <summary>请求取消执行。返回 false 表示该执行不在本实例运行或已结束。</summary>
    public static bool RequestCancel(Guid executionId)
    {
        if (!Cancels.TryGetValue(executionId, out var cts))
            return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public ExecutionWorker(IServiceScopeFactory scopeFactory, ExecutionQueue queue,
        IHubContext<ExecutionHub> hub, ILogger<ExecutionWorker> logger, IOptions<ExecutionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _hub = hub;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        var concurrency = _options.EffectiveConcurrency;
        _logger.LogInformation("执行器启动：实例 {Instance}｜并发 {Concurrency}｜轮询 {Poll}s｜心跳 {Heartbeat}s",
            InstanceId, concurrency, _options.PollSeconds, _options.HeartbeatSeconds);

        var running = new Dictionary<Guid, Task>();
        var heartbeatSeconds = Math.Max(5, _options.HeartbeatSeconds);
        // 僵死执行回收间隔：远小于 StaleRunningMinutes，保证过期后能较快落回 Error
        var reapSeconds = Math.Max(30, Math.Min(120, _options.StaleRunningMinutes * 60 / 2));
        var lastHeartbeat = DateTime.UtcNow;
        var lastReap = DateTime.UtcNow;

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(1, Math.Min(_options.PollSeconds, heartbeatSeconds))));

        // PeriodicTimer 与 Channel 都只允许「同一时刻存在一个等待者」：必须复用同一个等待 Task，
        // 一旦在同一轮里重复调用（上一轮遗留的等待尚未完成），WaitForNextTickAsync 会抛
        // InvalidOperationException，进而让宿主按 StopHost 策略整体退出（已在联调中复现）。
        // 这里用 SafeWaitAsync 包一层，并把等待任务提升到循环外，仅在完成后重新挂载。
        Task<bool> tick = SafeWaitAsync(timer.WaitForNextTickAsync(stoppingToken).AsTask());
        Task<bool> signal = SafeWaitAsync(_queue.WaitToReadAsync(stoppingToken).AsTask());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1) 回收已结束的任务槽位
                foreach (var id in running.Where(kv => kv.Value.IsCompleted).Select(kv => kv.Key).ToList())
                    running.Remove(id);

                // 2) 有并发余量时从数据库抢占待执行记录
                var free = concurrency - running.Count;
                if (free > 0)
                {
                    foreach (var id in await ClaimPendingAsync(free, stoppingToken))
                    {
                        running[id] = Task.Run(() => RunSafelyAsync(id, _stopping), CancellationToken.None);
                        _logger.LogInformation("抢占执行 {ExecutionId}（运行中 {Running}/{Max}）",
                            id, running.Count, concurrency);
                    }
                }

                // 3) 刷新本实例运行中任务的心跳
                if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= heartbeatSeconds)
                {
                    await RefreshHeartbeatAsync(stoppingToken);
                    lastHeartbeat = DateTime.UtcNow;
                }

                // 3.5) 回收其它实例崩溃后遗留的僵死执行
                if ((DateTime.UtcNow - lastReap).TotalSeconds >= reapSeconds)
                {
                    await ReapStaleAsync(stoppingToken);
                    lastReap = DateTime.UtcNow;
                }

                // 4) 等待：轮询到点 / 有任务结束 / 新任务入队
                var waiters = new List<Task> { tick, signal };
                if (running.Count > 0)
                    waiters.Add(Task.WhenAny(running.Values));
                await Task.WhenAny(waiters);

                // 已完成的等待任务需要重新挂载，未完成的保持复用
                if (tick.IsCompleted)
                {
                    await tick;
                    tick = SafeWaitAsync(timer.WaitForNextTickAsync(stoppingToken).AsTask());
                }
                if (signal.IsCompleted)
                {
                    await signal;
                    _queue.Drain();
                    signal = SafeWaitAsync(_queue.WaitToReadAsync(stoppingToken).AsTask());
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单轮异常不能让执行器停摆（StopHost 会连带终止整个服务）
                _logger.LogError(ex, "执行器循环异常，{Seconds}s 后重试", Math.Max(1, _options.PollSeconds));
                if (tick.IsCompleted)
                    tick = SafeWaitAsync(timer.WaitForNextTickAsync(stoppingToken).AsTask());
                if (signal.IsCompleted)
                    signal = SafeWaitAsync(_queue.WaitToReadAsync(stoppingToken).AsTask());
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // 停止：给运行中的任务最多 30s 收尾（避免留下僵死 Running 记录）
        if (running.Count > 0)
        {
            _logger.LogInformation("执行器停止中，等待 {Count} 个执行结束（最多 30s）", running.Count);
            await Task.WhenAny(Task.WhenAll(running.Values), Task.Delay(TimeSpan.FromSeconds(30)));
        }
    }

    /// <summary>观察等待任务的异常并转为 false，避免等待任务进入故障态后无法重新挂载</summary>
    private static async Task<bool> SafeWaitAsync(Task<bool> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 抢占待执行记录：UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING，保证多实例安全。
    ///
    /// 执行编排的门禁也在这里：带前置用例的执行，只有等「同一次套件运行内」的前置用例
    /// **全部通过**（Status = 2）才可被抢占。写成一条 NOT EXISTS 而不是在进程里排队，
    /// 是因为执行队列本身就是数据库队列——门禁下沉到 SQL，重启与多实例都不需要额外状态。
    /// 前置用例没进这次运行（例如被判为不支持执行）时子查询无行，NOT EXISTS 成立，按无前置放行。
    /// </summary>
    private async Task<List<Guid>> ClaimPendingAsync(int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        try
        {
            const string sql = """
                UPDATE "Executions"
                SET "Status" = 1, "StartedAt" = now(), "ClaimedBy" = {0}, "HeartbeatAt" = now()
                WHERE "Id" IN (
                    SELECT e."Id" FROM "Executions" e
                    WHERE e."Status" = 0
                      AND NOT EXISTS (
                          SELECT 1 FROM "Executions" dep
                          WHERE dep."SuiteRunId" = e."SuiteRunId"
                            AND dep."SuiteRunId" IS NOT NULL
                            AND dep."TestCaseId" = e."DependsOnTestCaseId"
                            AND dep."Status" <> 2
                      )
                    ORDER BY e."CreatedAt"
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING "Id"
                """;
            return await db.Database.SqlQueryRaw<Guid>(sql, InstanceId, take).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抢占待执行记录失败");
            return new List<Guid>();
        }
    }

    /// <summary>刷新本实例运行中任务的心跳</summary>
    private async Task RefreshHeartbeatAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Executions" SET "HeartbeatAt" = now() WHERE "ClaimedBy" = {0} AND "Status" = 1""",
                new object[] { InstanceId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新执行心跳失败");
        }
    }

    /// <summary>
    /// 回收僵死执行：心跳超过 StaleRunningMinutes 仍未更新的 Running 记录说明持有它的实例已经消失
    /// （进程崩溃 / 实例被下线），标记为 Error，避免永久挂在「执行中」。
    /// 用 SQL 直接更新，多实例同时跑也只会生效一次（状态已是 Running 才会被更新）。
    ///
    /// 顺带做一次编排结算：被回收的批次里，等这条的用例现在能判定「前置未通过」了，
    /// 否则整批会停在「等一个永远不会完成的执行」上——无人触发扫描就没人来跳过它。
    /// </summary>
    private async Task ReapStaleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var staleMinutes = Math.Max(1, _options.StaleRunningMinutes);
        try
        {
            var affected = await db.Database.SqlQueryRaw<Guid?>(
                """
                UPDATE "Executions"
                SET "Status" = 4,
                    "AIDiagnosis" = COALESCE("AIDiagnosis", '执行器实例心跳超时，执行已中断'),
                    "EndedAt" = COALESCE("EndedAt", now()),
                    "DurationMs" = COALESCE("DurationMs", 0),
                    "ClaimedBy" = NULL,
                    "HeartbeatAt" = NULL
                WHERE "Status" = 1
                  AND ("HeartbeatAt" IS NULL OR "HeartbeatAt" < now() - make_interval(mins => {0}))
                RETURNING "SuiteRunId"
                """,
                staleMinutes).ToListAsync(ct);
            if (affected.Count > 0)
                _logger.LogWarning("回收僵死执行 {Count} 条（心跳超过 {Minutes} 分钟未更新）", affected.Count, staleMinutes);

            var orchestrator = scope.ServiceProvider.GetRequiredService<ExecutionOrchestrator>();
            foreach (var suiteRunId in affected.Where(id => id is not null).Select(id => id!.Value).Distinct())
                await orchestrator.SweepSuiteRunAsync(suiteRunId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "回收僵死执行失败");
        }
    }

    private async Task RunSafelyAsync(Guid executionId, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(executionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行 {ExecutionId} 处理异常", executionId);
        }
    }

    private async Task ProcessAsync(Guid executionId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<TestRunner>();

        var execution = await db.Executions
            .Include(e => e.TestCase)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution is null)
            return;

        // 🔧 强制从 DB 加载 Environment——Include 在某些竞态下可能漏载，
        // 这里用 FindAsync 确保拿到最新值，避免 Worker 运行时 BaseUrl=null
        // （显式写全名：Environment 与 System.Environment 同名，裸写会歧义）
        global::AI.TestPlatform.Domain.Entities.Environment? environment = null;
        if (execution.EnvironmentId.HasValue)
        {
            environment = await db.Environments.FindAsync(execution.EnvironmentId.Value, ct);
        }

        // 🔍 诊断日志：确认 Environment 真的被正确载进来了
        _logger.LogInformation(
            "执行 {ExecutionId}: EnvironmentId={EnvId}, EnvironmentLoaded={EnvLoaded}, BaseUrl={BaseUrl}, LoginUrl={LoginUrl}, AutoLogin={AutoLogin}, PwSet={PwSet}",
            executionId,
            execution.EnvironmentId,
            environment is not null,
            environment?.BaseUrl ?? "(null)",
            environment?.LoginUrl ?? "(null)",
            environment?.AutoLogin,
            !string.IsNullOrEmpty(environment?.LoginPassword));

        if (execution.TestCase is null)
        {
            execution.Status = ExecutionStatus.Skipped;
            execution.StartedAt ??= DateTime.UtcNow;
            execution.EndedAt = DateTime.UtcNow;
            execution.DurationMs = 0;
            execution.ClaimedBy = null;
            execution.HeartbeatAt = null;
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("执行 {ExecutionId} 因用例不存在而跳过", executionId);
            return;
        }

        // TestCase 已随 Include 加载，此处只需补载其 Steps 集合
        await db.Entry(execution.TestCase).Collection(t => t.Steps).LoadAsync(ct);

        // 🔧 用 FindAsync 查到的 environment（而不是 Include 可能漏载的 execution.Environment）
        // 注意：EnvironmentSnapshot.BaseUrl 在 Migration 里 IsRequired，不能为 null！
        if (environment is not null && !string.IsNullOrWhiteSpace(environment.BaseUrl))
            execution.EnvironmentSnapshot = new EnvironmentSnapshot
            {
                Name = environment.Name,
                BaseUrl = environment.BaseUrl,
            };

        // 状态与 StartedAt 已在抢占时写入，此处补齐心跳
        execution.Status = ExecutionStatus.Running;
        execution.StartedAt ??= DateTime.UtcNow;
        execution.HeartbeatAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var group = executionId.ToString("N");
        await TryPushAsync(() => _hub.Clients.Group(group).SendAsync(
            "StatusChanged", (int)ExecutionStatus.Running, ct), executionId, "StatusChanged");

        // 手动终止的协作取消令牌：与宿主停机令牌 linked，两者任一触发都中断执行
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Cancels[executionId] = linked;
        var totalStepsSaved = false;
        try
        {
            try
            {
                // 🔧 不再直接赋值 execution.Results（会覆盖 EF 跟踪集合）；
                // 改为先跑 RunAsync，再把返回的 result 逐个添加到 db 跟踪的集合里
                var runResults = await runner.RunAsync(execution, environment, linked.Token,
                    onStepCompleted: async r =>
                    {
                        // 增量落库：每完成一步立即写入。执行中途刷新页面、或 SignalR 不可用走轮询时，
                        // 也能看到已完成的步骤结果，而不是等整条执行结束后一次性出现。
                        // 落库成功后 r.Id 才被填充，推送的 DTO 里 Id 也是真实值。
                        try
                        {
                            db.ExecutionResults.Add(r);
                            await db.SaveChangesAsync(linked.Token);
                        }
                        catch (Exception ex)
                        {
                            // 手动终止后的增量落库失败属预期：结果行仍被上下文跟踪，
                            // 由下方终态 SaveChanges 统一入库
                            _logger.LogWarning(ex, "步骤结果增量落库失败 步骤 {Order}", r.StepOrder);
                        }
                        await TryPushAsync(() => _hub.Clients.Group(group).SendAsync(
                            "StepCompleted", r.ToDto(), linked.Token), executionId, "StepCompleted");
                    },
                    // 步骤开始只推送、不落库：Running 行一旦残留（进程崩溃/取消）会永久挂着，
                    // 前端只把它当瞬时占位，StepCompleted 到达后用真实结果替换。
                    // 首步开始时顺手把 TotalSteps 快照落库，让详情页的「共 x 步」分母立即可见。
                    onStepStarted: async (order, snapshot) =>
                    {
                        if (!totalStepsSaved)
                        {
                            totalStepsSaved = true;
                            try
                            {
                                await db.SaveChangesAsync(linked.Token);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "TotalSteps 快照落库失败（终态时兜底重存）");
                            }
                        }
                        await TryPushAsync(() => _hub.Clients.Group(group).SendAsync(
                            "StepStarted", new StepStartedDto(order, snapshot), linked.Token),
                        executionId, "StepStarted");
                    });
                // 🔧 RunAsync 成功返回：用返回的列表推导 status（onStepCompleted 已增量落库）
                execution.Status = runResults.Any(r => r.Status != ExecutionStatus.Passed)
                    ? ExecutionStatus.Failed
                    : ExecutionStatus.Passed;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && linked.IsCancellationRequested)
            {
                // 终止发生在步骤开始前（如解析步骤阶段）：无崩溃语义，直接按 Canceled 收尾
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 {ExecutionId} 崩溃", executionId);
                execution.Status = ExecutionStatus.Error;
                // 🔧 用 db.ExecutionResults.Add 确保被 EF 跟踪，SaveChanges 才能入库
                db.ExecutionResults.Add(new ExecutionResult
                {
                    ExecutionId = executionId,
                    StepOrder = -1,
                    Status = ExecutionStatus.Error,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace,
                });
            }

            // 手动终止 → 终态 Canceled，覆盖按结果推导的 Failed。
            // 三种到达路径统一在此判定：① OCE 直接逸出；② 步骤被中断打上 Canceled 行；
            // ③ 终止发生在步骤边界，被循环顶的 Skipped 补行逻辑消化后 RunAsync 正常返回。
            if ((linked.IsCancellationRequested && !ct.IsCancellationRequested))
                execution.Status = ExecutionStatus.Canceled;
        }
        finally
        {
            Cancels.TryRemove(executionId, out _);
            linked.Dispose();
        }

        execution.EndedAt = DateTime.UtcNow;
        execution.DurationMs = (int)(execution.EndedAt.Value - execution.StartedAt!.Value).TotalMilliseconds;
        // ClaimedBy 刻意**保留**：它是「这条执行由哪个节点跑的」的溯源记录（节点看板的今日跑量依据）。
        // 心跳刷新只作用于 Status=Running，完成后保留不影响任何活性判断。
        execution.HeartbeatAt = null;

        // 以下收尾一律用 CancellationToken.None：手动终止后令牌已取消，收尾写库仍必须完成

        // 终态回写用条件更新（安全/正确性审查）：仅当 DB 里仍是 Running(1) 才允许落终态。
        // 竞态场景：本执行心跳停更后已被 ReapStale 回收为 Error（回收 UPDATE 自带 Status=1 条件），
        // 或被用户从另一实例/直接落库方式终止为 Canceled——迟到的回写都不允许覆盖。
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Executions"
            SET "Status" = {(int)execution.Status},
                "EndedAt" = {execution.EndedAt},
                "DurationMs" = {execution.DurationMs},
                "HeartbeatAt" = NULL
            WHERE "Id" = {executionId} AND "Status" = 1
            """, CancellationToken.None);
        if (claimed == 0)
        {
            // 已被回收/终止：结果行照常入库（保留证据），但内存状态对齐库里的真实终态，
            // 避免下方 SaveChanges 把外部写入的 Canceled/Error 覆盖回本实例推导的状态
            _logger.LogWarning("执行 {ExecutionId} 的终态回写被跳过：执行已被回收或终止", executionId);
            var dbStatus = await db.Executions.AsNoTracking()
                .Where(e => e.Id == executionId)
                .Select(e => e.Status)
                .FirstOrDefaultAsync(CancellationToken.None);
            if (dbStatus != ExecutionStatus.Pending)
                execution.Status = dbStatus;
        }
        await db.SaveChangesAsync(CancellationToken.None);
        await TryPushAsync(() => _hub.Clients.Group(group).SendAsync(
            "StatusChanged", (int)execution.Status, CancellationToken.None), executionId, "StatusChanged");

        // M8：失败执行的 Agent 自愈闭环。
        // 位置刻意在「终态回写之后、下游 Passed 判定逻辑之前」——自愈成功会把状态改写为 Passed，
        // 从而让随后的套件结算 / 缺陷自动验证 / 用例激活按"通过"处理（与设计 §4.3 一致）。
        // 开关（系统级 + 项目级双层与门）与场景跳过（Suite/CI）都在服务内部判定，此处仅按状态触发。
        if (execution.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
        {
            try
            {
                var agentLoop = scope.ServiceProvider.GetRequiredService<AgentLoopService>();
                await agentLoop.RunAsync(executionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "执行 {ExecutionId} 的 Agent 自愈闭环异常", executionId);
            }
        }

        // 执行编排：本条刚落地，立刻结算同一批次里在等它的执行
        // （前置未通过 → 跳过；快停策略 → 收尾剩余待执行）。放在通知之前是为了让下游尽快解锁。
        if (execution.SuiteRunId is { } suiteRunId)
        {
            try
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<ExecutionOrchestrator>();
                await orchestrator.SweepSuiteRunAsync(suiteRunId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "执行 {ExecutionId} 的编排结算异常", executionId);
            }
        }

        // 不稳定用例（flaky）识别
        try
        {
            var flake = scope.ServiceProvider.GetRequiredService<FlakeDetectionService>();
            if (execution.TestCaseId is { } caseId)
                await flake.RefreshAsync(caseId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行 {ExecutionId} flake 统计异常", executionId);
        }

        // 缺陷自动闭环（P2）：整条执行通过时，把它关联的 Fixed 缺陷自动置为 Verified。
        // 只在 Passed 时触发；Fixed → Verified 是回归验证的语义，带着说明可追溯，必要时人工 reopen。
        if (execution.Status == ExecutionStatus.Passed && execution.TestCaseId is { } passedCaseId)
        {
            try
            {
                var defects = scope.ServiceProvider.GetRequiredService<DefectService>();
                var verified = await defects.AutoVerifyOnRegressionPassAsync(passedCaseId, executionId, CancellationToken.None);
                if (verified > 0)
                    _logger.LogInformation("执行 {ExecutionId} 通过，自动验证 {Count} 个关联缺陷", executionId, verified);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "执行 {ExecutionId} 缺陷自动验证异常", executionId);
            }
        }

        // 用例状态自动流转：草稿 → 启用。
        //
        // 「启用」的定义就是**至少成功跑通过一次**——不给它这个含义的话，这个字段就是死的：
        // 原来全代码只有 AI 生成用例那一条路会写 Active，手工新建 / Excel 导入 / 脚本录制
        // 全都永远停在草稿，列表里那列状态也就没有信息量。
        //
        // 只在 Passed 时提升；已是 Active 的连查都查不出来（带条件查询），所以是幂等的、也不白写库。
        if (execution.Status == ExecutionStatus.Passed && execution.TestCaseId is { } activatedCaseId)
        {
            try
            {
                var draft = await db.TestCases.FirstOrDefaultAsync(
                    t => t.Id == activatedCaseId && t.Status == TestCaseStatus.Draft, CancellationToken.None);
                if (draft is not null)
                {
                    draft.Status = TestCaseStatus.Active;
                    draft.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogInformation("用例 {TestCaseId} 首次执行通过，状态由草稿提升为启用", activatedCaseId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "执行 {ExecutionId} 的用例状态流转异常", executionId);
            }
        }

        // 失败/错误：AI 诊断
        if (execution.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
        {
            try
            {
                var diagnosis = scope.ServiceProvider.GetRequiredService<DiagnosisService>();
                await diagnosis.DiagnoseAsync(executionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "执行 {ExecutionId} 自动诊断异常", executionId);
            }
        }

        // 测试计划轮次完成回填。
        // 刻意**不**放在 NotifyExecutionFinishedAsync 里面——那个方法在「通知未开启」时会提前返回，
        // 挂上去会导致关掉通知后轮次永远停在 Running。
        try
        {
            var roundId = execution.PlanRoundId;
            if (roundId is not null)
            {
                var plans = scope.ServiceProvider.GetRequiredService<TestPlanService>();
                var planId = await plans.TryCompleteRoundAsync(roundId.Value, CancellationToken.None);
                // 轮次刚完成 → 推一条轮次汇总（而不是逐执行推，否则一批 200 条用例会刷 200 条消息）
                if (planId is not null)
                {
                    var notifier = scope.ServiceProvider.GetRequiredService<NotificationService>();
                    await notifier.NotifyPlanRoundFinishedAsync(planId.Value, roundId.Value,
                        CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行 {ExecutionId} 的轮次完成回填异常", executionId);
        }

        // 通知推送（企微/钉钉/飞书/邮件）
        try
        {
            var notifier = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notifier.NotifyExecutionFinishedAsync(executionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行 {ExecutionId} 通知推送异常", executionId);
        }
    }

    private async Task TryPushAsync(Func<Task> send, Guid executionId, string eventName)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行 {ExecutionId} 推送 {Event} 失败", executionId, eventName);
        }
    }
}
