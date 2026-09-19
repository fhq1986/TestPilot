namespace AI.TestPlatform.Domain.Entities;

/// <summary>分享对象类型</summary>
public enum ReportShareKind
{
    /// <summary>单次执行</summary>
    Execution = 0,
    /// <summary>一次套件运行（一批执行）</summary>
    SuiteRun = 1,
    /// <summary>项目汇总（可选时间区间）</summary>
    Project = 2,
    /// <summary>测试计划（含达标判定与轮次趋势）</summary>
    TestPlan = 3,
}

/// <summary>
/// 报告分享令牌：生成一条免登录可访问的在线报告链接。
/// 报告内容由 Kind + RefId 决定，只读；可设置过期时间并可随时吊销。
/// </summary>
public class ReportShare
{
    public Guid Id { get; set; }

    /// <summary>URL 安全随机令牌（唯一）</summary>
    public string Token { get; set; } = string.Empty;

    public ReportShareKind Kind { get; set; }
    public Guid RefId { get; set; }

    /// <summary>项目（套件运行/项目汇总时必须，用于聚合）</summary>
    public Guid? ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>项目汇总报告的时间区间（仅 Kind=Project 时使用）</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>过期时间；为空表示长期有效</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>是否已吊销（吊销后立即失效）</summary>
    public bool Revoked { get; set; }

    public int ViewCount { get; set; }
    public DateTime? LastViewedAt { get; set; }

    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
