using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Startup;

/// <summary>周期清理参数，见 appsettings.json 的 Maintenance 节。</summary>
public class MaintenanceOptions
{
    /// <summary>保留期清理的执行间隔（小时）。默认 6 小时一轮。</summary>
    public int SweepIntervalHours { get; set; } = 6;
}

/// <summary>
/// 保留期清理守护：按固定间隔重跑审计日志 / trace / 截图的保留期清理。
///
/// 为什么需要它：这三类清理之前只在**启动时**跑一次——服务一旦长期不重启，
/// RetentionDays 就形同虚设，截图/trace/审计表无限增长（审查发现）。
/// 每轮只做幂等的保留期清理，启动专属的「僵死执行 / 失联 Mock」清点仍留在
/// MaintenanceService.CleanupOnStartupAsync（那是进程生命周期语义，放这里会误杀其它实例）。
/// </summary>
public sealed class MaintenanceWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceWorker> _logger;

    public MaintenanceWorker(IServiceProvider services, IOptions<MaintenanceOptions> options,
        ILogger<MaintenanceWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 间隔至少 1 小时：清理是重 IO 操作（MinIO 全前缀枚举、批量 DELETE），不宜频繁
        var interval = TimeSpan.FromHours(Math.Max(1, _options.SweepIntervalHours));
        using var timer = new PeriodicTimer(interval);
        _logger.LogInformation("保留期清理守护启动：每 {Hours}h 一轮", interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await MaintenanceService.RunRetentionCleanupAsync(_services);
            }
            catch (Exception ex)
            {
                // 单轮失败不影响下一轮：清理是尽力而为的幂等操作
                _logger.LogWarning(ex, "周期保留期清理失败，{Hours}h 后重试", interval.TotalHours);
            }
        }
    }
}
