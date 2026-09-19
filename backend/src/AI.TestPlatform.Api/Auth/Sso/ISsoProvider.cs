namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>企业身份源扫码后解析出的平台无关身份。Subject 是企业在该应用内的稳定唯一标识。</summary>
public sealed record SsoIdentity(string Subject, string? DisplayName, string? Email);

/// <summary>
/// 单个 SSO 身份源的抽象：如何构造「扫码授权页」地址、如何用授权码换身份。
/// 实现必须无状态（access_token 等凭据缓存放在实现内部时需线程安全）。
/// </summary>
public interface ISsoProvider
{
    /// <summary>URL 段里的 Provider 标识（wecom / dingtalk / mock）</summary>
    string Id { get; }

    /// <summary>登录按钮显示名</summary>
    string DisplayName { get; }

    /// <summary>是否启用（未启用的 Provider 不出现在登录页，相关端点直接 404）</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 构造企业授权页地址。<paramref name="redirectUri"/> 是企业授权完成后的回调
    /// （指向前端 /login/sso 回调页），<paramref name="state"/> 由后端生成用于防 CSRF。
    /// </summary>
    string BuildAuthorizeUrl(string redirectUri, string state);

    /// <summary>授权码换企业身份。code 无效 / 过期 / 调用失败返回 null（由调用方统一转成 401 语义）。</summary>
    Task<SsoIdentity?> ExchangeAsync(string code, CancellationToken ct);
}
