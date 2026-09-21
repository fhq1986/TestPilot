namespace AI.TestPlatform.Domain.Entities;

public class TestCase
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public TestType Type { get; set; }
    public string? Description { get; set; }

    // 外部导入来源信息（如 Excel 测试用例模板）：用例编号与所属模块
    public string? CaseCode { get; set; }
    public string? Module { get; set; }
    // 导入时 Excel 中的「前置条件/操作步骤」与「预期结果」原文，便于报告回显与重新解析
    public string? SourceSteps { get; set; }
    public string? ExpectedResult { get; set; }

    /// <summary>
    /// 用例级网络规则（jsonb，JSON 数组）。执行时在页面打开后、任何请求发出前注册到
    /// Playwright 的 route 上。
    ///
    /// 为什么需要它：前端自动化的头号不稳定源是"第三方依赖"——支付、短信、地图、埋点接口
    /// 抖一下，用例就红一次，而它跟被测系统的质量毫无关系。有了规则就能按用例把这些依赖桩掉。
    ///
    /// 放在**用例上**而不是做成一个步骤：route 必须在请求发出**之前**注册，
    /// 做成步骤就会依赖"作者记得把它排在 Navigate 前面"，一旦顺序错了就是静默不生效。
    /// </summary>
    public string? NetworkRules { get; set; }

    // AI 生成标记
    public bool AIGenerated { get; set; }
    public string? AIPrompt { get; set; }

    // 执行配置
    public string? Browser { get; set; }
    public int Timeout { get; set; } = 30000;
    public int RetryCount { get; set; }

    // 失败即中止：任一步骤失败后跳过剩余步骤（剩余步骤标记 Skipped）
    public bool FailFast { get; set; }

    // 不稳定（flaky）：最近若干次执行结果既通过又失败，回归可信度低
    public bool IsFlaky { get; set; }
    /// <summary>最近 10 次执行的不稳定度（0-1，值越高越不稳定）</summary>
    public double FlakeRate { get; set; }
    public DateTime? FlakeCheckedAt { get; set; }

    // 参数化：绑定数据集后，用例步骤中的 {{列名}} 会按数据行逐行替换
    public Guid? DataSetId { get; set; }
    public DataSet? DataSet { get; set; }

    // 视觉回归：开启后对步骤截图与基线比对
    public bool VisualEnabled { get; set; }
    /// <summary>视觉差异容忍阈值（0-1，默认 0.01 = 1% 像素差异以内视为通过）</summary>
    public double VisualThreshold { get; set; } = 0.01;
    /// <summary>
    /// 视觉忽略区域（jsonb 数组：[{"x":5,"y":8,"w":50,"h":45}]，百分比 0~100 相对基线图尺寸）。
    /// 时间戳、广告位、头像这类每次渲染必然不同的区域，比对时直接屏蔽——
    /// 否则跨浏览器/跨平台的字体渲染差异会持续制造误报，团队开始"习惯性点接受"，门禁就废了。
    /// 百分比而不是像素：视口尺寸变化时按比例缩放。
    /// </summary>
    public string? VisualIgnoreRegions { get; set; }

    /// <summary>
    /// 扩展字段值（jsonb 对象：键 = CustomFieldDef.Id，值为字符串统一存储）。
    /// 定义删除后值残留无害——读取端按现存定义渲染，无定义的键直接忽略。
    /// </summary>
    public string? CustomFields { get; set; }

    // ------------------------------ 评审（方案 A：标记层，不影响执行与自动流转）
    /// <summary>评审状态。内容变更时 Approved 自动回 Pending（结论绑定内容版本）</summary>
    public CaseReviewStatus ReviewStatus { get; set; } = CaseReviewStatus.None;
    /// <summary>最近一次提交评审的人（通知「驳回/批准」的收件人）</summary>
    public Guid? ReviewSubmittedById { get; set; }
    public DateTime? ReviewSubmittedAt { get; set; }
    /// <summary>批准/驳回的评审人</summary>
    public Guid? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    /// <summary>评审意见（驳回时必填，批准可选）</summary>
    public string? ReviewNote { get; set; }

    /// <summary>关联需求（需求覆盖率统计的依据；一个用例最多挂一个需求，精简口径）</summary>
    public Guid? RequirementId { get; set; }
    public Requirement? Requirement { get; set; }

    // 版本控制
    public int Version { get; set; } = 1;
    public Guid? ParentId { get; set; }
    public TestCase? Parent { get; set; }
    /// <summary>
    /// 生命周期状态。**不要**拿它表示"已删除"——软删除见 <see cref="DeletedAt"/>。
    /// 语义：Draft = 还没验证过能跑通；Active = 至少成功执行通过一次（由执行收尾自动提升）。
    /// </summary>
    public TestCaseStatus Status { get; set; } = TestCaseStatus.Draft;
    public string? Priority { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 软删除时间，非空即已删除（全局查询过滤器按它隐藏）。
    ///
    /// 从 `Status = Deprecated` 拆出来，原因见 <see cref="TestCaseStatus"/>：
    /// 删除是删除、状态是状态，混用会让"状态列永远显示不出已删除"，
    /// 还会诱使别处拿状态当哨兵值用。
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public List<TestStep> Steps { get; set; } = new();
    public List<Execution> Executions { get; set; } = new();

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>创建人</summary>
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>最后修改人</summary>
    public Guid? UpdatedById { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
