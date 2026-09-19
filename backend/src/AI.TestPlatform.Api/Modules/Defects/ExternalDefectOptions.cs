namespace AI.TestPlatform.Api.Modules.Defects;

/// <summary>
/// 外部缺陷系统对接配置。两个 Provider 均默认关闭（平台「配置开关」惯例）：
/// 不配置时推送入口不出现、端点直接拒绝，行为与没有对接功能时完全一致。
/// </summary>
public class ExternalDefectOptions
{
    public JiraOptions Jira { get; set; } = new();
    public ZentaoOptions Zentao { get; set; } = new();
}

public class JiraOptions
{
    public bool Enabled { get; set; }

    /// <summary>Jira 站点根地址（如 https://jira.example.com）</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Jira Cloud 用邮箱；Server/DC 版可留空</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>API Token（Cloud）或密码（Server/DC）</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>缺陷落到的项目 Key（如 QA）</summary>
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>问题类型名（默认 Bug，不同实例可能有自定义名）</summary>
    public string IssueType { get; set; } = "Bug";
}

public class ZentaoOptions
{
    public bool Enabled { get; set; }

    /// <summary>禅道站点根地址（需开启 API v1：后台-集成-API）</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>禅道 API Token（后台生成，或 /api.php/v1/tokens 换取）</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>缺陷所属产品 ID</summary>
    public int ProductId { get; set; }
}
