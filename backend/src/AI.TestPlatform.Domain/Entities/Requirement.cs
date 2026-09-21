namespace AI.TestPlatform.Domain.Entities;

/// <summary>需求状态：未开始 → 进行中 → 已完成</summary>
public enum RequirementStatus { NotStarted = 0, InProgress = 1, Completed = 2 }

/// <summary>
/// 需求（精简版）：测试覆盖的锚点。不做需求管理全流程（那不是测试平台的事），
/// 只回答两个问题：这个需求有没有用例在测？哪些需求还裸奔？
/// 外部需求系统（禅道/Jira）的正式对接预留 ExternalKey。
/// </summary>
public class Requirement
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>需求标题（如"用户登录-密码错误提示"）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>补充说明：验收要点、来源文档链接等</summary>
    public string? Description { get; set; }

    /// <summary>外部需求系统的标识（对接预留）</summary>
    public string? ExternalKey { get; set; }

    /// <summary>优先级：P0/P1/P2（自由文本，排序提示用）</summary>
    public string? Priority { get; set; }

    // ------------------------------ 进度字段
    /// <summary>计划开始时间</summary>
    public DateTime? PlanStartDate { get; set; }
    /// <summary>计划完成时间</summary>
    public DateTime? PlanEndDate { get; set; }
    /// <summary>实际开始时间</summary>
    public DateTime? ActualStartDate { get; set; }
    /// <summary>实际完成时间</summary>
    public DateTime? ActualEndDate { get; set; }
    /// <summary>状态：未开始 / 进行中 / 已完成</summary>
    public RequirementStatus Status { get; set; } = RequirementStatus.NotStarted;

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>创建人</summary>
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>最后修改人</summary>
    public Guid? UpdatedById { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>关联的用例（一个用例只挂一个需求；反向统计覆盖率）</summary>
    public List<TestCase> TestCases { get; set; } = new();

    /// <summary>关联的测试计划（反向：一个需求可以有多个计划在测）</summary>
    public List<TestPlan> TestPlans { get; set; } = new();
}
