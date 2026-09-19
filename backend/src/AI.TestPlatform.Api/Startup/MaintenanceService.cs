using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Modules.Artifacts;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Startup;

// 启动清理（幂等）：
// - 心跳过期的 Running 执行 → Error（原实例已崩溃/被回收，任务不会再有进展）
// - Running 状态的 Mock → Stopped（Mock 服务器随进程存活，进程没了就不可达）
// - 超出保留期的审计日志 → 按批次删除
// 注意：Pending 不再清理——它是持久化任务队列的真身，重启后由执行器继续抢占执行，
//       多实例部署时其它实例可能正在处理，绝不能在启动时误杀。
public static class MaintenanceService
{
    public static async Task CleanupOnStartupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var options = scope.ServiceProvider.GetService<IOptions<ExecutionOptions>>()?.Value ?? new ExecutionOptions();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MaintenanceService));

        var staleBefore = DateTime.UtcNow.AddMinutes(-Math.Max(1, options.StaleRunningMinutes));

        // 心跳为空的 Running 记录同样视为僵死（老数据或抢占后未及写心跳就崩溃）
        var stale = await db.Executions
            .Where(e => e.Status == ExecutionStatus.Running &&
                        (e.HeartbeatAt == null || e.HeartbeatAt < staleBefore))
            .ToListAsync();
        foreach (var execution in stale)
        {
            execution.Status = ExecutionStatus.Error;
            execution.AIDiagnosis ??= "执行器实例心跳超时，执行已中断";
            execution.EndedAt ??= DateTime.UtcNow;
            execution.DurationMs ??= 0;
            execution.ClaimedBy = null;
            execution.HeartbeatAt = null;
        }

        // 多实例安全（审查发现）：WireMock 随各自进程存活，重启本实例**不能**误标
        // 其它实例仍在服务的 Mock。归属节点还活着（心跳新鲜）就一定还在服务；
        // 归属节点已失联 → 它的进程没了，进程内的 WireMock 必然也没了，直接置 Stopped。
        // 无主（历史数据）或本节点归属的才用回环探活兜底——同机多进程探活有效，
        // 跨容器/跨机探不到对方回环端口，这正是引入 OwnerNode 的原因。
        var staleMockCount = 0;
        var nodeStaleBefore = DateTime.UtcNow.AddSeconds(-Math.Max(60, options.HeartbeatSeconds * 3));
        var nodeHeartbeats = await db.ExecutionNodes.ToDictionaryAsync(n => n.Name, n => n.LastHeartbeatAt);
        var nodeName = ExecutionNodeRegistry.NodeName;
        foreach (var mock in await db.MockDefinitions
                     .Where(m => m.Status == MockStatus.Running)
                     .ToListAsync())
        {
            if (mock.OwnerNode is string owner && owner != nodeName)
            {
                // 归属其它节点：绝不回环探活（跨容器探不到对方的端口），按节点心跳判定
                if (nodeHeartbeats.TryGetValue(owner, out var beat) && beat >= nodeStaleBefore)
                    continue;
                mock.Status = MockStatus.Stopped;
                mock.Port = null;
                staleMockCount++;
                continue;
            }

            if (mock.Port is int port && IsPortAlive(port))
                continue;
            mock.Status = MockStatus.Stopped;
            mock.Port = null;
            staleMockCount++;
        }

        if (stale.Count > 0 || staleMockCount > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("启动清理：僵死执行 {StaleExecutions} 条、失联 Mock {StaleMocks} 个",
                stale.Count, staleMockCount);
        }

        await RunRetentionCleanupAsync(services);
    }

    /// <summary>
    /// 保留期清理（幂等、可周期执行）：审计日志 / trace / 截图。
    /// 由 MaintenanceWorker 按 Maintenance:SweepIntervalHours 周期重跑——
    /// 之前只在启动时清一次，长期不重启的服务等于没有保留期（审查发现）。
    /// 启动专属的「僵死执行 / 失联 Mock」清点不在这里，那是进程生命周期语义。
    /// </summary>
    public static async Task RunRetentionCleanupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var options = scope.ServiceProvider.GetService<IOptions<ExecutionOptions>>()?.Value ?? new ExecutionOptions();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MaintenanceService));

        await CleanupAuditLogsAsync(db, services, logger);
        await CleanupTracesAsync(services, options, logger);
        await CleanupVideosAsync(services, options, logger);
        await CleanupScreenshotsAsync(services, options, logger);
    }

    /// <summary>
    /// TCP 探活：端口仍有监听（无论是谁在监听）就视为存活。
    /// WireMock 只在回环监听，探活目标固定 127.0.0.1；1s 超时足够区分「活着」和「已死」。
    /// </summary>
    private static bool IsPortAlive(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(System.Net.IPAddress.Loopback, port);
            if (!connect.Wait(TimeSpan.FromSeconds(1)))
                return false;
            return client.Connected;
        }
        catch (Exception)
        {
            // 连接被拒绝/超时/不可达都视为「没人监听」
            return false;
        }
    }

    /// <summary>
    /// 清理超出保留期的 trace 包。trace 带截图与 DOM 快照，单个几 MB，
    /// 不设保留期会迅速吃掉存储——这是它必须配清理策略的原因。
    /// 经产物存储抽象执行：本地磁盘与 MinIO 对象存储同一套逻辑。
    /// </summary>
    private static async Task CleanupTracesAsync(IServiceProvider services, ExecutionOptions options,
        ILogger logger)
    {
        if (options.TraceRetentionDays <= 0) return;
        var traces = services.GetService<TraceStorage>();
        if (traces is null) return;

        var cutoff = DateTime.UtcNow.AddDays(-options.TraceRetentionDays);
        var removed = await traces.CleanupOlderThan(cutoff);
        if (removed > 0)
            logger.LogInformation("trace 清理：删除 {Count} 个早于 {Cutoff:u} 的回放包", removed, cutoff);

        // 文件删了，数据库里的链接也得同步置空：否则老执行详情页会一直展示
        // 一个点开就 404 的「查看 trace」——链接的存在本身就是对用户的错误承诺
        await using var db = services.CreateScope().ServiceProvider.GetRequiredService<TestDbContext>();
        var nullified = await db.Executions
            .Where(e => e.TraceUrl != null && e.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.TraceUrl, (string?)null), CancellationToken.None);
        if (nullified > 0)
            logger.LogInformation("trace 清理：置空 {Count} 条过期执行的 TraceUrl 链接", nullified);
    }

    /// <summary>
    /// 清理超出保留期的失败录像，并同步置空对应的 VideoUrl 链接。
    /// 视频有独立保留期（默认 30 天，比截图短——它比截图大两个数量级），
    /// 从截图清理的排除列表里摘出来了，职责归一：这个前缀只有这条路径会动。
    /// </summary>
    private static async Task CleanupVideosAsync(IServiceProvider services, ExecutionOptions options,
        ILogger logger)
    {
        if (options.VideoRetentionDays <= 0) return;
        var videos = services.GetService<VideoStorage>();
        if (videos is null) return;

        var cutoff = DateTime.UtcNow.AddDays(-options.VideoRetentionDays);
        var removed = await videos.CleanupOlderThan(cutoff);
        if (removed > 0)
            logger.LogInformation("录像清理：删除 {Count} 个早于 {Cutoff:u} 的录像", removed, cutoff);

        await using var db = services.CreateScope().ServiceProvider.GetRequiredService<TestDbContext>();
        var nullified = await db.Executions
            .Where(e => e.VideoUrl != null && e.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.VideoUrl, (string?)null), CancellationToken.None);
        if (nullified > 0)
            logger.LogInformation("录像清理：置空 {Count} 条过期执行的 VideoUrl 链接", nullified);
    }

    /// <summary>
    /// 清理超出保留期的执行步骤截图。基线（_baselines）不在清理范围——那是持久资产，只随用例删除。
    /// </summary>
    private static async Task CleanupScreenshotsAsync(IServiceProvider services, ExecutionOptions options,
        ILogger logger)
    {
        if (options.ScreenshotRetentionDays <= 0) return;
        var store = services.GetService<IArtifactStore>();
        if (store is null) return;

        var cutoff = DateTime.UtcNow.AddDays(-options.ScreenshotRetentionDays);
        // 排除项里必须带上富文本图片前缀：那些图是**需求/缺陷正文的内容**，
        // 不是可再生的执行截图——按时间清掉等于把历史正文的图删空。
        // traces/ 与 videos/ 同样排除：它们各有自己的保留期（Trace/VideoRetentionDays），
        // 由专门的清理逻辑（连同数据库列置空）管理，不能在这里被按截图的口径顺手删掉。
        var removed = await store.DeleteRootOlderThanAsync(cutoff,
            new[] { "_baselines/", TraceStorage.Prefix, VideoStorage.Prefix, ArtifactApiExtensions.RichTextPrefix },
            CancellationToken.None);
        if (removed > 0)
            logger.LogInformation("截图清理：删除 {Count} 个早于 {Cutoff:u} 的截图对象", removed, cutoff);
    }

    /// <summary>
    /// 清理超出保留期的审计日志。
    ///
    /// 审计表只增不删，没有保留期就是一颗定时炸弹：查询越查越慢、备份越来越大。
    /// 分批删除（每批 CleanupBatchSize 行）而不是一条 DELETE 全删，
    /// 是为了避免一次持有过多行锁把并发写入阻塞住。
    /// </summary>
    private static async Task CleanupAuditLogsAsync(TestDbContext db, IServiceProvider services,
        ILogger logger)
    {
        var audit = services.GetService<IOptions<AuditOptions>>()?.Value ?? new AuditOptions();
        if (audit.RetentionDays <= 0) return;

        var cutoff = DateTime.UtcNow.AddDays(-audit.RetentionDays);
        var batchSize = Math.Clamp(audit.CleanupBatchSize, 100, 50_000);
        var removed = 0;

        while (true)
        {
            var batch = await db.AuditLogs
                .Where(l => l.CreatedAt < cutoff)
                .OrderBy(l => l.CreatedAt)
                .Take(batchSize)
                .ToListAsync();
            if (batch.Count == 0) break;

            db.AuditLogs.RemoveRange(batch);
            await db.SaveChangesAsync();
            removed += batch.Count;

            // 一批没删满说明已经清干净了
            if (batch.Count < batchSize) break;
        }

        if (removed > 0)
            logger.LogInformation("审计日志清理：删除 {Count} 条早于 {Cutoff:u} 的记录", removed, cutoff);
    }
}
