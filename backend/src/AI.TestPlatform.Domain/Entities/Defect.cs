namespace AI.TestPlatform.Domain.Entities;

/// <summary>缺陷严重度：建议性 &lt; 一般 &lt; 严重 &lt; 致命（数值即升序）</summary>
public enum DefectSeverity
{
    /// <summary>建议性：体验优化、文案调整类</summary>
    Suggestion = 0,
    /// <summary>一般：功能缺陷但有替代路径，不阻塞主流程</summary>
    Normal = 1,
    /// <summary>严重：主流程受阻，但有临时绕行方案</summary>
    Major = 2,
    /// <summary>致命：系统崩溃 / 数据丢失 / 主功能完全不可用</summary>
    Critical = 3,
}

/// <summary>
/// 缺陷生命周期。闭环 = Verified / Closed；
/// 「未闭环」= New / Assigned / Fixed（Fixed 还没验证过，仍欠着验证的债）。
/// </summary>
public enum DefectStatus
{
    /// <summary>新建（发现后尚未指派）</summary>
    New = 0,
    /// <summary>已指派修复负责人</summary>
    Assigned = 1,
    /// <summary>开发已修复，等待回归验证</summary>
    Fixed = 2,
    /// <summary>回归验证通过（闭环）</summary>
    Verified = 3,
    /// <summary>已关闭（验收/版本收尾）</summary>
    Closed = 4,
    /// <summary>驳回（非缺陷 / 无法复现 / 重复提交），带原因</summary>
    Rejected = 5,
    /// <summary>挂起（本期不修），带原因</summary>
    Deferred = 6,
}

/// <summary>
/// 缺陷：测试活动的最终产出物。与执行的关联分两层——
/// <see cref="FoundInExecutionId"/> 是「首次发现」的快照（复盘入口），
/// <see cref="DefectOccurrence"/> 是「每次复现/认领」的流水（回归统计与去重）。
/// </summary>
public class Defect
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    /// <summary>描述与证据：一键转缺陷时会预填步骤错误、AI 诊断等</summary>
    public string? Description { get; set; }

    public DefectSeverity Severity { get; set; }
    public DefectStatus Status { get; set; } = DefectStatus.New;

    /// <summary>修复负责人（可空 = 尚未指派）</summary>
    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }

    /// <summary>提交人（可空：用户被删后缺陷仍在）</summary>
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    /// <summary>首次发现的执行（执行记录被删后置空，缺陷保留）</summary>
    public Guid? FoundInExecutionId { get; set; }
    public Execution? FoundInExecution { get; set; }
    /// <summary>首次发现的步骤号快照（步骤结果可能随执行被删，存值不存外键）</summary>
    public int? FoundInStepOrder { get; set; }
    /// <summary>首次发现时所属用例</summary>
    public Guid? FoundInTestCaseId { get; set; }
    public TestCase? FoundInTestCase { get; set; }

    /// <summary>外部缺陷系统链接（预留，P3 对接时启用）</summary>
    public string? ExternalRef { get; set; }

    /// <summary>状态流转备注：修复说明 / 驳回原因 / 挂起原因共用</summary>
    public string? ResolutionNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>开发标记修复的时间（修复时长 = FixedAt - CreatedAt）</summary>
    public DateTime? FixedAt { get; set; }
    /// <summary>回归验证通过的时间（验证时长 = VerifiedAt - FixedAt）</summary>
    public DateTime? VerifiedAt { get; set; }
    /// <summary>验证人（提交人本人或测试负责人，见端点权限注释）</summary>
    public Guid? VerifiedById { get; set; }
    public User? VerifiedBy { get; set; }
}

/// <summary>缺陷 ↔ 用例 多对多：一个缺陷可被多个用例暴露，一个用例也可能暴露多个缺陷。</summary>
public class DefectCase
{
    public Guid DefectId { get; set; }
    public Defect Defect { get; set; } = null!;

    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;
}

/// <summary>
/// 缺陷复现/认领流水：把某次执行的某个失败步骤「认领」到已有缺陷上（去重），
/// 或回归时同一缺陷再次暴露。执行记录被删后 ExecutionId 置空但流水保留（OccurrenceAt 仍有统计价值）。
/// </summary>
public class DefectOccurrence
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }
    public Defect Defect { get; set; } = null!;

    public Guid? ExecutionId { get; set; }
    public Execution? Execution { get; set; }

    public int StepOrder { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
