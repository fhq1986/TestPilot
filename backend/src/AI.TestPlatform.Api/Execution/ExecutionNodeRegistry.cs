using System.Reflection;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 节点注册/心跳服务（分布式执行可观测层）。
///
/// 每个运行执行器的进程都会在 ExecutionNodes 表里登记一行并周期性刷新心跳；
/// 主 API 实例和远程 worker 实例（同一份二进制、不同 Node:Name）一视同仁。
/// 离线判定是动态的：看板按 LastHeartbeatAt 距今是否超过阈值计算，无需回写状态。
/// </summary>
public class ExecutionNodeRegistry : BackgroundService
{
    /// <summary>本节点名：Node:Name 优先，缺省机器名。同时是 ClaimedBy 的前缀（见 ExecutionWorker）。</summary>
    public static string NodeName { get; private set; } = System.Environment.MachineName;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExecutionOptions _options;
    private readonly ILogger<ExecutionNodeRegistry> _logger;

    public ExecutionNodeRegistry(IServiceScopeFactory scopeFactory, IOptions<ExecutionOptions> options,
        ILogger<ExecutionNodeRegistry> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;

        var configured = _options.NodeName?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            NodeName = configured;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
        var instanceId = $"{NodeName}:{System.Environment.ProcessId}";
        var interval = Math.Max(5, _options.HeartbeatSeconds);

        _logger.LogInformation("节点登记：{Node}（实例 {Instance}｜版本 {Version}｜并发 {Max}）",
            NodeName, instanceId, version, _options.EffectiveConcurrency);

        // 首跳立即登记，让看板在进程启动后立刻能看到节点
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await HeartbeatAsync(instanceId, version, stoppingToken);
            }
            catch (Exception ex)
            {
                // 数据库暂不可用等场景：节点掉线就掉线，不影响执行器本体
                _logger.LogWarning(ex, "节点心跳失败，{Interval}s 后重试", interval);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Upsert 本节点行并统计运行中执行数（ClaimedBy 前缀 = 节点名）。</summary>
    private async Task HeartbeatAsync(string instanceId, string version, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var running = await db.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::int AS "Value" FROM "Executions" WHERE "Status" = 1 AND "ClaimedBy" LIKE {0} || ':%'""",
            NodeName).ToListAsync(ct);

        var node = await db.ExecutionNodes.FirstOrDefaultAsync(n => n.Name == NodeName, ct);
        if (node is null)
        {
            node = new ExecutionNode
            {
                Id = Guid.NewGuid(),
                Name = NodeName,
                MachineName = System.Environment.MachineName,
                InstanceId = instanceId,
                Version = version,
                MaxConcurrency = _options.EffectiveConcurrency,
                RunningCount = running.FirstOrDefault(),
                StartedAt = DateTime.UtcNow,
                LastHeartbeatAt = DateTime.UtcNow,
            };
            db.ExecutionNodes.Add(node);
        }
        else
        {
            node.MachineName = System.Environment.MachineName;
            node.InstanceId = instanceId;
            node.Version = version;
            node.MaxConcurrency = _options.EffectiveConcurrency;
            node.RunningCount = running.FirstOrDefault();
            node.LastHeartbeatAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
