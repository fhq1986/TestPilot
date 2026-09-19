namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 测试用例的历史版本快照。
///
/// **为什么是快照表，而不是"每次编辑新建一行 TestCase、用 ParentId 串成链"**：
/// 后者会让 `TestCases` 表里同一个逻辑用例出现多行，而全项目有 **71 处**在查 `TestCases`
/// （列表、看板计数、报告、计划范围、套件、执行……）。那些查询一旦开始把历史版本也当成独立用例，
/// 就会出现"用例数悄悄变大、通过率分母不对"这类**没人会立刻发现**的问题，漏掉任何一处都算bug。
/// 快照模型把历史放进独立表，主表仍然"一个用例一行"，既有查询一行都不用改。
///
/// （代价：执行记录通过 `TestCaseId` 只能指到"用例"，指不到"当时是第几版"。
///   真需要的话，给 `Execution` 加一个运行时的版本号字段即可，那是独立的一小步；
///   而 `TestCase.ParentId` 在这个模型下**用不上**，保留字段但保持为空。）
/// </summary>
public class TestCaseVersion
{
    public Guid Id { get; set; }

    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;

    /// <summary>版本号。语义：**第 N 版的内容**（改到第 3 版时，表里会有 v1、v2 两条快照）</summary>
    public int Version { get; set; }

    /// <summary>内容快照（JSON，结构见 TestCaseSnapshot）</summary>
    public string Snapshot { get; set; } = string.Empty;

    /// <summary>人话变更摘要（如"步骤 5 → 7 步；超时 30000 → 60000"），列表里直接展示</summary>
    public string? ChangeSummary { get; set; }

    /// <summary>
    /// 该版本的步骤数（冗余字段）。
    /// 列表要显示步数，若为此把每行快照 JSON 都反序列化一遍不值当——
    /// 与 `DataSet.RowCount` 是同一个理由（jsonb 里没法直接 Count）。
    /// </summary>
    public int StepCount { get; set; }

    /// <summary>是谁改的（改之前那一刻的当前用户）</summary>
    public Guid? OperatorId { get; set; }
    public string? OperatorName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
