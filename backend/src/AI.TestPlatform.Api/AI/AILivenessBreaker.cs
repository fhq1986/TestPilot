using AI.TestPlatform.Api.Execution;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// AI 可用性熔断器（进程内单例，不落盘——可再生的临时状态）。
///
/// 当 AIWorker 连续不可达（连接失败 / 5xx / 超时）到阈值后打开熔断，让所有 AI 路径
/// （M3 生成、M4 定位自愈、M6 诊断、M8 Agent）**直接短路为 no-op**，不再发起任何 LLM 请求——
/// 避免"LLM 挂了还把每条执行都拖到超时"。
///
/// 状态机：Closed（健康）→ Open（熔断）→ HalfOpen（冷放行**一次**探针）→ 成功回 Closed / 失败**立即**回 Open。
/// ⚠ 半开态必须只放行一个探针，且探针失败要**立即重新开路**（不能等再攒够阈值，
/// 否则一次失败的探针会把熔断"漏开"，退化成全部放行）。
/// </summary>
public class AILivenessBreaker
{
    private readonly object _lock = new();
    private readonly int _threshold;
    private readonly TimeSpan _cooldown;

    private int _consecutiveFailures;
    private DateTime? _openedAt;              // 非 null = Open
    private bool _halfOpenProbeInFlight;      // 半开探针在途

    public AILivenessBreaker(IOptions<AgentLoopOptions> options)
    {
        var o = options.Value;
        _threshold = Math.Max(1, o.ConsecutiveLlmFailuresThreshold);
        // 下限取 1ms（非法负值兜底）；生产默认 60s，由配置决定
        _cooldown = TimeSpan.FromMilliseconds(Math.Max(1, o.LlmCircuitCooldownMs));
    }

    /// <summary>当前是否处于熔断（Open/HalfOpen 均视为"不可发起新调用"）</summary>
    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _openedAt is not null || _halfOpenProbeInFlight;
            }
        }
    }

    /// <summary>状态码：0=Closed（健康）1=HalfOpen（半开探针中）2=Open（熔断）。供 /metrics 展示。</summary>
    public int StateCode
    {
        get
        {
            lock (_lock)
            {
                if (_halfOpenProbeInFlight) return 1;
                return _openedAt is null ? 0 : 2;
            }
        }
    }

    /// <summary>是否允许发起一次 LLM 调用。返回 true 时若处于半开，则占用唯一探针名额。</summary>
    public bool AllowCall()
    {
        lock (_lock)
        {
            if (_halfOpenProbeInFlight)
                return false;                     // 已有探针在途，其余一律拒绝
            if (_openedAt is null)
                return true;                      // Closed
            if (DateTime.UtcNow - _openedAt.Value >= _cooldown)
            {
                _halfOpenProbeInFlight = true;    // 半开：只放行这一个探针
                return true;
            }
            return false;                         // Open，短路
        }
    }

    public void OnSuccess()
    {
        lock (_lock)
        {
            _openedAt = null;
            _halfOpenProbeInFlight = false;
            _consecutiveFailures = 0;
        }
    }

    public void OnFailure()
    {
        lock (_lock)
        {
            if (_halfOpenProbeInFlight)
            {
                // 探针失败：立即重新开路（不等阈值）
                _halfOpenProbeInFlight = false;
                _openedAt = DateTime.UtcNow;
                _consecutiveFailures = _threshold;
                return;
            }
            if (++_consecutiveFailures >= _threshold)
                _openedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 统一包装：熔断中直接返回 fallback；调用成功/失败据此驱动状态机。
    /// 只吞掉 <see cref="AIWorkerException"/>（AI 不可用）——其余异常照常上抛，不掩盖真实缺陷。
    /// </summary>
    public async Task<T?> SafeCallAsync<T>(Func<Task<T>> call, T? fallback)
    {
        if (!AllowCall())
            return fallback;
        try
        {
            var result = await call();
            OnSuccess();
            return result;
        }
        catch (AIWorkerException)
        {
            OnFailure();
            return fallback;
        }
    }
}
