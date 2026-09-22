namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 压测场景 ↔ 接口用例的关联（迭代 F·P2-9）。
///
/// 为什么是独立表而不是 jsonb 数组：这里需要 **FK 级联**（用例被删时自动解绑，
/// 而不是在脚本生成时才炸）和**反向引用查询**（"这个用例被哪些压测场景引用"）。
/// 对齐 TestSuiteCase 的既有做法。
/// </summary>
public class LoadTestScenarioCase
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public LoadTestScenario? Scenario { get; set; }

    public Guid TestCaseId { get; set; }
    public TestCase? TestCase { get; set; }

    /// <summary>在脚本里的出现顺序（决定 group 的先后，影响有变量传递的链路）</summary>
    public int Order { get; set; }
}
