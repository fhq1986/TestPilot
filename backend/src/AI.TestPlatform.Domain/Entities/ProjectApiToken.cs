namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 项目级 API Token：供 CI / 外部系统以项目身份触发执行。
///
/// 只存 SHA-256 哈希，不存明文——数据库泄露也拿不到可用凭证；
/// 明文仅在创建响应里出现一次。Prefix 存前 12 个字符（含 atp_ 前缀），
/// 用于列表展示时辨认"是哪把钥匙"，不构成泄露。
/// </summary>
public class ProjectApiToken
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>用途说明（如「Jenkins 流水线」「GitHub Actions」）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256(token) 的小写十六进制。查询用索引，长度固定 64。</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>明文前 12 字符（atp_ + 8 位随机），仅用于展示辨认</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>可空 = 不过期</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>吊销时间。吊销是软删除：保留记录供审计，不再可用</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>最近一次通过该令牌鉴权的时间（触发成功才算）</summary>
    public DateTime? LastUsedAt { get; set; }

    public Project? Project { get; set; }
}
