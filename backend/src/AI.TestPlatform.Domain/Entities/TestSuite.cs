namespace AI.TestPlatform.Domain.Entities;

/// <summary>套件类型：决定界面归类与是否用于门禁</summary>
public enum SuiteKind { Smoke, Regression, Release, Custom }

/// <summary>
/// 测试套件 / 测试计划：把用例按业务场景组织成「冒烟集 / 回归集 / 发版必跑集」。
/// 执行套件时按顺序批量创建执行，并用 SuiteRunId 把一次套件运行的所有执行聚合起来。
/// </summary>
public class TestSuite
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SuiteKind Kind { get; set; } = SuiteKind.Custom;

    /// <summary>执行时使用的环境（为空则用用例自身地址）</summary>
    public Guid? EnvironmentId { get; set; }
    public Environment? Environment { get; set; }

    /// <summary>
    /// 失败策略（执行编排）：默认 Continue，保持「一条失败不影响同批其它用例」的既有行为。
    /// 冒烟集/发版必跑集通常设成 StopOnFailure——首条失败往往说明环境或版本有问题，
    /// 继续把几百条用例跑完只是浪费机器和时间。
    /// </summary>
    public SuiteFailurePolicy FailurePolicy { get; set; } = SuiteFailurePolicy.Continue;

    /// <summary>套件内用例（含顺序）</summary>
    public List<TestSuiteCase> Cases { get; set; } = new();

    // ------------------------------ 运行状态
    public DateTime? LastRunAt { get; set; }
    /// <summary>最近一次套件运行 ID（可用它查询本次运行的全部执行）</summary>
    public Guid? LastSuiteRunId { get; set; }
    public int LastCreatedCount { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>套件与用例的关联（多对多 + 顺序）</summary>
public class TestSuiteCase
{
    public Guid Id { get; set; }
    public Guid SuiteId { get; set; }
    public TestSuite Suite { get; set; } = null!;
    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;
    /// <summary>套件内执行顺序（从 0 开始）</summary>
    public int Order { get; set; }

    /// <summary>
    /// 前置用例（执行编排）：本用例要等「同一次套件运行内」的这一条用例通过之后才开跑；
    /// 前置未通过（失败/错误/被跳过）时本用例直接跳过，不做无效执行。
    ///
    /// 只允许指向**同一个套件内**的用例（保存时校验，且不允许自依赖与环形依赖——
    /// 环会让两边都永远等不到对方，最后整个批次卡在待执行）。
    /// 刻意不带导航属性：TestSuiteCase 已经有一个 TestCase 导航，再加一条指向 TestCases
    /// 的关系会让 EF 推断出两条同目标外键，这里在 DbContext 里显式配置成无导航的 SetNull。
    /// </summary>
    public Guid? DependsOnTestCaseId { get; set; }
}
