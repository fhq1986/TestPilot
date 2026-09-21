namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 测试数据集（参数化 / 数据驱动）：一张「列 + 行」的表，供用例用 {{列名}} 取值。
/// 例：登录用例绑定「账号矩阵」数据集，5 行数据 → 一次执行展开成 5 条数据行结果。
/// </summary>
public class DataSet
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>列名（有序），与 Rows 中每行的 key 对应</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>数据行：每行是「列名 → 值」的映射</summary>
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    /// <summary>
    /// 数据行数（冗余字段）：Rows 以 jsonb 存储、无法在 SQL 里 Count，
    /// 列表/套件等场景需要「有多少行」时读这一列即可，避免把整份 jsonb 拉进内存。
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>是否把首行当作示例/表头说明（仅影响界面提示，不参与执行）</summary>
    public bool FirstRowIsSample { get; set; }

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>创建人</summary>
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>最后修改人</summary>
    public Guid? UpdatedById { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
