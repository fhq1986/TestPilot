namespace AI.TestPlatform.Domain.Entities;

public class ExecutionResult
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public Execution Execution { get; set; } = null!;
    public Guid? TestStepId { get; set; }
    public TestStep? TestStep { get; set; }
    public int StepOrder { get; set; }
    public ExecutionStatus Status { get; set; }
    public int? DurationMs { get; set; }
    public string? ScreenshotUrl { get; set; }
    public string? Log { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }

    // 步骤配置快照：用例被编辑后报告仍显示执行时的真实配置
    public StepConfig? StepSnapshot { get; set; }

    /// <summary>
    /// 步骤动作类型（ActionType 的 int）。
    /// 快照只含"配置"不含"动作"——动作是步骤的属性，结果行必须自己带，
    /// 否则详情页的步骤摘要没法以「动作 + 定位方式 + 配置摘要」展示。
    /// 可空：历史行没有该值（迁移前落库的），前端按缺失降级显示。
    /// </summary>
    public int? StepActionType { get; set; }

    // ------------------------------ 视觉回归
    public VisualStatus VisualStatus { get; set; } = VisualStatus.Skipped;
    /// <summary>与基线的像素差异比例（0-1）</summary>
    public double? VisualDiffRatio { get; set; }
    /// <summary>判定为变化时的阈值（留存当时口径，便于回溯）</summary>
    public double? VisualThreshold { get; set; }
    /// <summary>基线图 / 差异图 URL；本次实际截图见 ScreenshotUrl</summary>
    public string? BaselineImageUrl { get; set; }
    public string? DiffImageUrl { get; set; }
    /// <summary>AI 对差异的语义化说明（仅在判定为变化且开启 AI 说明时生成）</summary>
    public string? VisualNote { get; set; }

    /// <summary>
    /// M8：失败时采集的**页面可交互元素快照**（jsonb，`List&lt;InteractiveElement&gt;`）。
    /// 归因证据没有 DOM 时，LLM 只能给"指导"而给不出具体定位符；带上这份清单它才能产出可用的 `locator_value`。
    /// 仅失败步骤才有值，采集失败不影响执行主流程。
    /// </summary>
    public string? ElementSnapshot { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
