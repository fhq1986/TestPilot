namespace AI.TestPlatform.Domain.Entities;

// 测试环境（被测系统地址 + 登录信息）
public class Environment
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? LoginUrl { get; set; }
    public string? LoginUsername { get; set; }
    public string? LoginPassword { get; set; }
    public string? LoginSuccessIndicator { get; set; }
    public bool AutoLogin { get; set; } = true;
    /// <summary>该环境的默认浏览器（chromium / firefox / webkit）；为空表示跟随用例配置</summary>
    public string? Browser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// 执行环境快照（脱敏，jsonb）
public class EnvironmentSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
