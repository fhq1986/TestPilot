using AI.TestPlatform.Infrastructure.Data;

namespace AI.TestPlatform.Api.Recorder;

/// <summary>
/// 录制会话维护后台服务。
///
/// 单独起一个 worker 而不是在启动清理里做一次，是因为录制会话的失效是**运行期持续发生**的：
/// 用户关掉页面、浏览器被外部结束、实例重启留下"数据库说在录、实际没进程"的脏数据。
/// 定期回收才能保证浏览器窗口不会在桌面上越积越多。
///
/// 与 ScheduleWorker 的差别：这里只做回收，不承担业务调度，
/// 因此即使多实例同时跑也不会互相干扰（各自只能杀掉自己进程表里的会话）。
/// </summary>
public class RecorderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecorderWorker> _logger;

    public RecorderWorker(IServiceScopeFactory scopeFactory, ILogger<RecorderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后先等一小会：让 MaintenanceService 与迁移先跑完，避免和启动流程抢数据库
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var recorder = scope.ServiceProvider.GetRequiredService<RecorderService>();
                await recorder.ReapStaleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 回收失败不能拖垮后台服务：下一轮继续尝试
                _logger.LogError(ex, "录制会话回收失败");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
