using System.Threading.Channels;

namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 压测的进程内唤醒信号（迭代 F·P2-9）。
///
/// 与 <see cref="Execution.ExecutionQueue"/> 同构但**刻意不复用同一个类型**：
/// 两者的读者是各自独立的 worker，共用一个 Channel 会让「唤醒谁」变得含糊
/// （压测入队却把执行器唤醒，白白空转一次）。
/// 真正的任务存储仍是数据库的 Pending 记录，多实例共享也不会重复执行。
/// </summary>
public class LoadTestQueue
{
    private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    /// <summary>通知 worker「有新的压测任务了」</summary>
    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct)
        => _channel.Writer.WriteAsync(0, ct);

    /// <summary>等待唤醒信号（用于 worker 循环）</summary>
    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    /// <summary>丢弃累积的唤醒信号，避免空转</summary>
    public void Drain()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }
}
