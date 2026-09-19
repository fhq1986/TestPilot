namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 项目级用例扩展字段定义。
/// 不同团队要记的东西不一样（"关联需求单号""所属客户"），固定字段只能塞进描述——
/// 一张轻量定义表 + 用例上的 jsonb 值就够了，不必上规则引擎。
/// </summary>
public class CustomFieldDef
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>字段名（同项目内唯一，如「关联需求单号」）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>字段类型（决定编辑控件的形态与值的校验方式）</summary>
    public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;

    /// <summary>Select 类型的候选值（JSON 数组字符串，如 ["A","B"]）；其他类型为 null</summary>
    public string? Options { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum CustomFieldType { Text, Number, Date, Select }
