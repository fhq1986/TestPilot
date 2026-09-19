namespace AI.TestPlatform.Domain.Entities;

public class TestStep
{
    public Guid Id { get; set; }
    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;
    public int StepOrder { get; set; }
    public ActionType ActionType { get; set; }

    // 通用配置（jsonb）
    public StepConfig Config { get; set; } = new();

    // AI 相关
    public string? AIInstruction { get; set; }
    public string? AIElementDescription { get; set; }

    // 迭代 C：共享步骤引用。非空时本步骤是个「占位」，运行时由 SharedStepExpander
    // 展开成组内的真实步骤；此时 Config / ActionType 不参与执行。
    public Guid? SharedGroupId { get; set; }
    public SharedStepGroup? SharedGroup { get; set; }

    /// <summary>引用共享步骤组时对本组变量的覆盖（优先于组内默认值）</summary>
    public List<SharedVariableEntry>? SharedVariables { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
