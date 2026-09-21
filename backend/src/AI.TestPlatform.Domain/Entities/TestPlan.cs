namespace AI.TestPlatform.Domain.Entities;

/// <summary>计划状态。Draft → Active → Completed → Archived，单向流转</summary>
public enum TestPlanStatus { Draft, Active, Completed, Archived }

/// <summary>达标口径：最后一轮达标即可，还是任意一轮达标即算（适用于持续回归）</summary>
public enum PlanGateMode { LastRound, AnyRound }

/// <summary>
/// 轮次执行状态。
///
/// 刻意**不含** 通过/失败 —— 那种状态必须由「本轮所有执行结果」聚合得出，
/// 落成冗余字段就得在每条执行完成时回写，既写放大，异常中断时还会留下不一致的值。
/// 轮次是否达标一律实时算（见 PlanRunner 的聚合查询）。
/// </summary>
public enum PlanRoundStatus { Running, Completed, Aborted }

/// <summary>
/// 测试计划（验收过程）。
///
/// 与 <see cref="TestSuite"/> 的边界：
/// - 套件回答「跑哪一批用例」——长期存在、内容随用例演进、没有质量目标；
/// - 计划回答「这个版本在什么时间窗内、要达到什么目标、跑几轮、达没达标」——
///   有起止时间与版本标识、有目标通过率与门禁、有负责人，执行单位是**多轮轮次**。
///
/// 一句话：**套件管「跑什么」，计划管「验收」。**
/// </summary>
public class TestPlan
{
    /// <summary>显式生成主键（与 RecorderSession 同理：不依赖 EF 的值生成时机）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 版本 / 里程碑标识（如 "v2.3.0"、"2026-09 迭代"）。
    /// 刻意用字符串而不是独立实体：平台不该去管团队的版本管理，
    /// 只要能挂在报告与门禁结果上供人识别即可。
    /// </summary>
    public string? ReleaseName { get; set; }

    public TestPlanStatus Status { get; set; } = TestPlanStatus.Draft;

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>负责人</summary>
    public Guid? OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>关联的需求（一个计划可对应一个需求，多计划对应同一需求由 TestPlans nav prop 反向承载）</summary>
    public Guid? RequirementId { get; set; }
    public Requirement? Requirement { get; set; }

    // ------------------------------ 质量目标
    /// <summary>目标通过率（0-1）。达标判定：实际通过率 >= 该值</summary>
    public double TargetPassRate { get; set; } = 0.95;

    /// <summary>是否允许存在 Error 状态的执行（默认不允许——环境坏了不该算「通过」）</summary>
    public bool AllowErrors { get; set; }

    /// <summary>是否把不稳定用例（IsFlaky）的失败排除在失败数之外，避免抖动卡住验收</summary>
    public bool ExcludeFlakyFromFailure { get; set; } = true;

    public PlanGateMode GateMode { get; set; } = PlanGateMode.LastRound;

    /// <summary>
    /// 缺陷验收门槛：开启后，所属项目存在未闭环的致命/严重缺陷时达标判定直接不通过
    /// （未闭环 = New/Assigned/Fixed，与缺陷模块统计同一口径）。默认关闭，不改变既有计划行为。
    /// </summary>
    public bool DefectGateEnabled { get; set; }

    // ------------------------------ 执行配置（作为轮次默认值，单次触发可覆盖）
    public Guid? EnvironmentId { get; set; }
    public Environment? Environment { get; set; }

    /// <summary>浏览器矩阵；为空表示按「环境 → 用例 → chromium」逐条解析</summary>
    public List<string>? Browsers { get; set; }
    public bool ExpandDataSets { get; set; } = true;

    // ------------------------------ 汇总字段（列表页展示用，避免逐计划聚合）
    public DateTime? LastRoundAt { get; set; }
    public int LastCreatedCount { get; set; }
    public string? LastError { get; set; }

    public List<TestPlanItem> Items { get; set; } = new();
    public List<TestPlanRound> Rounds { get; set; } = new();

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>创建人</summary>
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>最后修改人</summary>
    public Guid? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 计划范围内的用例（扁平，含执行顺序）。
///
/// 为什么不做成「计划 → 套件 → 用例」的两层引用：
/// 计划的本质是「这一轮要验什么」，范围必须能一处看全、逐个微调；
/// 两层间接引用会让报告里说不清「这一轮到底跑了哪些」，
/// 而且套件一改计划跟着变——验收时最怕这种隐式联动。
/// 需要同步时用「从套件导入」，是显式动作。
/// </summary>
public class TestPlanItem
{
    /// <summary>
    /// 刻意**不**预设主键（与 TestPlan 不同）。
    ///
    /// 理由：这个实体是通过 <c>plan.Items.Add(...)</c> 挂到**已存在的**计划上的。
    /// 对一个已跟踪的父实体，EF 会逐个判断子实体的状态——主键非空就假定「这行已经存在」，
    /// 于是发出 UPDATE 而不是 INSERT，影响 0 行后抛 DbUpdateConcurrencyException。
    /// 交给 EF 生成主键，它才会正确判定为 Added。
    /// （对比：TestPlan 是直接 db.TestPlans.Add() 的，整张图都被强制标为 Added，所以预设主键没问题。）
    /// </summary>
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }
    public TestPlan Plan { get; set; } = null!;

    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;

    /// <summary>计划内执行顺序（从 0 开始）</summary>
    public int Order { get; set; }
}

/// <summary>计划的一轮执行</summary>
public class TestPlanRound
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public TestPlan Plan { get; set; } = null!;

    /// <summary>第几轮（计划内从 1 递增）</summary>
    public int RoundNo { get; set; }

    public PlanRoundStatus Status { get; set; } = PlanRoundStatus.Running;

    public TriggerType TriggerType { get; set; }
    public string? TriggerSource { get; set; }
    public Guid? TriggeredById { get; set; }

    // 固化本轮怎么跑的，便于事后回溯（计划配置改了也不影响历史轮次的解释）
    public Guid? EnvironmentId { get; set; }
    public List<string>? Browsers { get; set; }
    public bool ExpandDataSets { get; set; }

    /// <summary>创建的执行数 = Σ(用例 × 浏览器 × 数据行)</summary>
    public int CreatedCount { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 轮次的范围快照。
///
/// 存在的理由只有一个：**历史轮次报告不能被后续编辑污染**。
/// 用例改名、被删、换模块之后，第 3 轮的报告仍要能显示当时的事实，
/// 否则验收材料在事后就不可信了。
/// </summary>
public class PlanRoundCase
{
    /// <summary>同上：可能被挂到已跟踪的轮次上，交给 EF 生成主键</summary>
    public Guid Id { get; set; }
    public Guid RoundId { get; set; }
    public TestPlanRound Round { get; set; } = null!;

    public Guid TestCaseId { get; set; }
    /// <summary>当时的用例名</summary>
    public string TestCaseName { get; set; } = string.Empty;
    /// <summary>当时的模块</summary>
    public string? Module { get; set; }
    public int Order { get; set; }
}
