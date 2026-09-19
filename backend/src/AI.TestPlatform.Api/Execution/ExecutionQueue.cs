using System.Threading.Channels;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 进程内唤醒信号：入队后立刻唤醒执行器去数据库抢占任务（低延迟）。
/// 真正的任务存储是数据库的 Pending 记录，因此多实例共享同一队列也不会重复执行。
/// </summary>
public class ExecutionQueue
{
    private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    /// <summary>通知执行器「有新任务了」</summary>
    public ValueTask EnqueueAsync(Guid executionId, CancellationToken ct)
        => _channel.Writer.WriteAsync(0, ct);

    /// <summary>等待唤醒信号（用于执行器循环）</summary>
    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    /// <summary>丢弃累积的唤醒信号，避免空转</summary>
    public void Drain()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }
}
