namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 需求（精简版）：测试覆盖的锚点。不做需求管理全流程（那不是测试平台的事），
/// 只回答两个问题：这个需求有没有用例在测？哪些需求还裸奔？
/// 外部需求系统（禅道/Jira）的正式对接预留 ExternalKey。
/// </summary>
public class Requirement
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>需求标题（如"用户登录-密码错误提示"）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>补充说明：验收要点、来源文档链接等</summary>
    public string? Description { get; set; }

    /// <summary>外部需求系统的标识（对接预留）</summary>
    public string? ExternalKey { get; set; }

    /// <summary>优先级：P0/P1/P2（自由文本，排序提示用）</summary>
    public string? Priority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>关联的用例（一个用例只挂一个需求；反向统计覆盖率）</summary>
    public List<TestCase> TestCases { get; set; } = new();
}
