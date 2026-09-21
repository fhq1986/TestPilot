namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// M8 Agent 失败自愈闭环的可调参数（配置节：appsettings.json 的 AgentLoop）。
///
/// ⚠ **总开关不在这里**：系统级总开关是 <c>SystemConfig.AgentLoopEnabled</c>（系统配置页手动开关），
/// 项目级开关是 <c>Project.AgentLoopEnabled</c>；本类只放预算 / 置信度门 / 阈值 / 跳过规则等常量型参数。
/// </summary>
public class AgentLoopOptions
{
    /// <summary>单次执行的 LLM 调用上限</summary>
    public int MaxLlmCallsPerExecution { get; set; } = 5;

    /// <summary>Plan 轮次场景下更紧的 LLM 调用上限（批量执行要整体通过率，不宜被单条拖住）</summary>
    public int MaxLlmCallsPerPlanRound { get; set; } = 3;

    /// <summary>单个失败点的最大修复尝试次数</summary>
    public int MaxFixAttemptsPerFailure { get; set; } = 3;

    /// <summary>自动应用的默认最低置信度（LocatorUpdate 等走各自门类阈值）</summary>
    public float AutoApplyMinConfidence { get; set; } = 0.5f;

    /// <summary>需审批类修复"可自动批准"的置信度门（>= 该值才可能免审批）</summary>
    public float ReviewAutoApproveConfidence { get; set; } = 0.9f;

    /// <summary>AgentHealCircuitBreaker：近 24h 连续多少次"自愈仍失败"后暂停该项目</summary>
    public int ConsecutiveFailuresBeforeCircuitBreak { get; set; } = 3;

    /// <summary>归因结果缓存的 TTL（分钟）</summary>
    public int AttributionCacheTtlMinutes { get; set; } = 1440;

    /// <summary>单次执行的 Agent Loop 总耗时上限（毫秒）——须显著小于 Execution:StaleRunningMinutes</summary>
    public int MaxAgentLoopDurationMs { get; set; } = 120_000;

    /// <summary>是否允许破坏性修复（删除 / 重排）。默认不允许</summary>
    public bool AllowDestructiveFixes { get; set; }

    /// <summary>套件成员执行时跳过 Agent Loop（避免阻塞依赖链）</summary>
    public bool SkipAgentLoopInSuite { get; set; } = true;

    /// <summary>CI 触发执行时跳过 Agent Loop（CI 要快速反馈）</summary>
    public bool SkipAgentLoopInCi { get; set; } = true;

    // ------------------------------ AILivenessBreaker（AI 可用性熔断）
    /// <summary>连续多少次 LLM 调用失败后打开熔断</summary>
    public int ConsecutiveLlmFailuresThreshold { get; set; } = 5;

    /// <summary>熔断打开后的冷却时长（毫秒）；到期后进入半开放行一次探针</summary>
    public int LlmCircuitCooldownMs { get; set; } = 60_000;
}
