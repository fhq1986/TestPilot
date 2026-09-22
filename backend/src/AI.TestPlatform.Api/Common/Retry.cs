namespace AI.TestPlatform.Api.Common;

/// <summary>
/// 轻量重试助手（迭代 E·④ 韧性）。
///
/// 用途：给**没有内建弹性策略**的外部依赖兜底——MinIO 对象存储、Playwright 浏览器启动。
/// HTTP 调用不走这里：统一用 <c>Microsoft.Extensions.Http.Resilience</c> 的标准弹性处理器
/// （重试 + 熔断 + 超时 + 并发限制），避免两套重试叠加。
///
/// 策略：指数退避（base × 2^(n-1)）+ 0~100ms 抖动，避免多个实例同时重试把下游打爆。
/// 取消令牌触发的中断**不重试**（用户主动中止 / 请求超时，重试只会拖长响应）。
/// </summary>
public static class Retry
{
    private const int DefaultMaxAttempts = 3;
    private static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromMilliseconds(200);

    public static Task ExecuteAsync(
        Func<CancellationToken, Task> action, ILogger logger, string operation,
        int maxAttempts = DefaultMaxAttempts, TimeSpan? baseDelay = null, CancellationToken ct = default)
        => ExecuteCoreAsync(async c => { await action(c); return true; }, logger, operation, maxAttempts, baseDelay, ct);

    public static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action, ILogger logger, string operation,
        int maxAttempts = DefaultMaxAttempts, TimeSpan? baseDelay = null, CancellationToken ct = default)
        => ExecuteCoreAsync(action, logger, operation, maxAttempts, baseDelay, ct);

    private static async Task<T> ExecuteCoreAsync<T>(
        Func<CancellationToken, Task<T>> action, ILogger logger, string operation,
        int maxAttempts, TimeSpan? baseDelay, CancellationToken ct)
    {
        var attempts = Math.Max(1, maxAttempts);
        var delay = baseDelay ?? DefaultBaseDelay;
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 主动取消：不重试
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt == attempts) break;

                var wait = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt - 1))
                           + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
                logger.LogWarning(ex, "{Operation} 第 {Attempt}/{Max} 次失败，{Delay}ms 后重试",
                    operation, attempt, attempts, (int)wait.TotalMilliseconds);
                await Task.Delay(wait, ct);
            }
        }

        throw last!;
    }
}
