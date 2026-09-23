namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// M8 Agent：一次"失败 → 归因 → 修复 → 重跑"尝试的完整轨迹，挂在 <see cref="Execution"/> 下（一对多）。
///
/// 设计要点（见 docs/m8-agent-design.md §4.3 / §5）：
/// - 修复**只作用于执行期的 steps 副本**，不修改真实 <see cref="TestStep"/>；
///   本实体里的 <see cref="AppliedActions"/> 就是那份 diff 的留痕。
/// - "是否已采纳到用例"由 <see cref="Persisted"/> 表达（Phase 2 的「采纳为用例新版本」才会置 true）。
/// - <see cref="Result"/> 是**尝试级**结论；整轮 Loop 的**执行级**结论落在
///   <see cref="Execution.AgentFinalVerdict"/>。
/// </summary>
public class AgentAttempt
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public Execution? Execution { get; set; }

    /// <summary>第几次尝试（1-based）</summary>
    public int AttemptNumber { get; set; }

    /// <summary>针对哪个失败步骤（StepOrder）</summary>
    public int TargetStepOrder { get; set; }

    /// <summary>归因输入证据（jsonb）：List&lt;FailedStepEvidence&gt;</summary>
    public string? FailureEvidence { get; set; }

    /// <summary>LLM 原始返回，调试用；应用层截断到 8000 字符（省略号计入上限，防 Postgres 22001）</summary>
    public string? DiagnosisRaw { get; set; }

    public FixCategory FixCategory { get; set; }
    public float Confidence { get; set; }

    /// <summary>人类可读的修复描述</summary>
    public string? FixSummary { get; set; }

    /// <summary>实际应用的修复动作（jsonb）：List&lt;FixActionDto&gt;</summary>
    public string? AppliedActions { get; set; }
    public bool AppliedSuccessfully { get; set; }

    /// <summary>
    /// LLM **提议**的修复动作（jsonb）：List&lt;FixActionDto&gt;。
    ///
    /// 与 <see cref="AppliedActions"/> 的区别是语义而非粗细：那是"已应用的 diff"，
    /// 这是"待采纳的提议"。需人工审批的尝试在自愈循环里就 break 了（尚未应用任何动作），
    /// 所以 <see cref="AppliedActions"/> 对它**必然为空**——「采纳」只能依据本字段落库。
    /// 不复用 <see cref="DiagnosisRaw"/> 是因为那是调试用字段、被截断到 8000 字符，
    /// 超限后 JSON 直接不合法，会让采纳静默变成 no-op。
    /// </summary>
    public string? ProposedFixes { get; set; }

    /// <summary>该修复是否已被"采纳"落库到用例（Phase 2，默认 false）</summary>
    public bool Persisted { get; set; }

    public AgentAttemptResult Result { get; set; }

    /// <summary>修复后仍失败时的错误信息</summary>
    public string? FailureAfterFix { get; set; }

    public bool NeedsApproval { get; set; }

    /// <summary>人工审批结论：null=未审，true=通过，false=拒绝</summary>
    public bool? Approved { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // LLM 成本追踪
    public int LlmInputTokens { get; set; }
    public int LlmOutputTokens { get; set; }
    public string? LlmModel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
