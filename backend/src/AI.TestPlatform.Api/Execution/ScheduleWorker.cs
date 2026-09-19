using AI.TestPlatform.Api.Schedules;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 定时任务调度器：周期性扫描到点的 Schedule 并展开成执行记录。
///
/// 单实例内由一个循环驱动即可；多实例同时运行时靠 ScheduleService 里的
/// NextRunAt 比较并交换（CAS）保证同一时刻只有一个实例真正触发。
/// </summary>
public class ScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleWorker> _logger;

    public ScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<ScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("定时任务调度器启动");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ScheduleService>();
                await service.BackfillNextRunAsync(stoppingToken);
                var executed = await service.RunDueAsync(stoppingToken);
                if (executed.Count > 0)
                    _logger.LogInformation("本轮触发 {Count} 个定时任务：{Ids}",
                        executed.Count, string.Join(", ", executed));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单轮失败不影响后续轮次
                _logger.LogError(ex, "定时任务扫描异常");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("定时任务调度器已停止");
    }
}
