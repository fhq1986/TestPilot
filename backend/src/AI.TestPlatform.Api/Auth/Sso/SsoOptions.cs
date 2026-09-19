namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// SSO 扫码登录配置。整体遵循平台的「配置开关」惯例：不配置或 Enabled=false 时完全不走该 Provider，
/// 行为与没有 SSO 功能时一致（用户名密码登录不受任何影响）。
/// </summary>
public class SsoOptions
{
    /// <summary>
    /// 是否允许「扫码登录的新用户」自动开通平台账号（角色 Viewer）。
    /// 默认 false：未绑定的新用户扫码时会提示联系管理员，避免外部人员扫码即获得平台访问权。
    /// </summary>
    public bool AutoProvision { get; set; }

    /// <summary>
    /// 前端站点根地址（如 http://localhost:5173）。企业授权回调会跳到
    /// {FrontendBaseUrl}/login/sso，前端回调页再把 code+state 提交给后端换平台 JWT。
    /// </summary>
    public string? FrontendBaseUrl { get; set; }

    /// <summary>按 Provider Id 索引的配置（wecom / dingtalk / mock）。键不区分大小写。</summary>
    public Dictionary<string, SsoProviderConfig> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public class SsoProviderConfig
{
    /// <summary>是否启用该 Provider（总开关）</summary>
    public bool Enabled { get; set; }

    /// <summary>登录按钮上显示的名字（缺省用 Provider 内置名）</summary>
    public string? DisplayName { get; set; }

    // ---------- 企业微信 ----------
    /// <summary>企业 CorpId</summary>
    public string? CorpId { get; set; }

    /// <summary>自建应用 AgentId</summary>
    public string? AgentId { get; set; }

    /// <summary>自建应用 Secret</summary>
    public string? Secret { get; set; }

    // ---------- 钉钉 ----------
    /// <summary>应用 ClientId（AppKey）</summary>
    public string? ClientId { get; set; }

    /// <summary>应用 ClientSecret（AppSecret）</summary>
    public string? ClientSecret { get; set; }
}
