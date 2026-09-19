using Pgvector;

namespace AI.TestPlatform.Domain.Entities;

// AI 元素识别缓存（自愈核心）。
// 2026-09-17 起带向量列：ElementEmbedder 的 256 维哈希嵌入落 pgvector，
// 检索用数据库近邻查询，替换掉「全表拉回内存两两比对」的字符模糊匹配；
// 字符匹配保留作保底回退（向量写入失败/历史行未回填时兜底）。
public class AIElementCache
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string PageUrl { get; set; } = string.Empty;
    public string ElementDescription { get; set; } = string.Empty;

    /// <summary>
    /// 描述的 256 维嵌入（ElementEmbedder）。可空：历史行靠命中时惰性回填，
    /// 新增行总是写入。检索时 IS NOT NULL 的行才参与近邻排序。
    /// </summary>
    public Vector? Embedding { get; set; }

    // 历史选择器（jsonb，最新在末尾）
    public List<SelectorHistoryEntry> SelectorHistory { get; set; } = new();

    public DateTime? LastMatchedAt { get; set; }
    public int MatchCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SelectorHistoryEntry
{
    public string Type { get; set; } = "css";
    public string Value { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
