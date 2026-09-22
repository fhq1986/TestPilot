namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>企业身份源授权后解析出的平台无关身份。Subject 是企业在该应用内的稳定唯一标识。</summary>
public sealed record SsoIdentity(string Subject, string? DisplayName, string? Email);

/// <summary>
/// 授权回调上下文（企业把用户送回平台时携带的一次性凭据）。
/// <para><see cref="State"/> 由后端生成、一次性、服务端校验，用于防 CSRF；标准 OIDC 同时把它当作 <c>nonce</c>。</para>
/// <para><see cref="RedirectUri"/> 是发起授权时用的回调地址——OIDC 换取令牌**必须回传同值**（Azure AD 等要求严格匹配），
/// 所以这里显式带上，而不是让 Provider 自己猜。</para>
/// </summary>
public sealed record SsoCallback(string Code, string State, string RedirectUri);

/// <summary>
/// 单个 SSO 身份源的抽象：如何构造「授权页」地址、如何用授权码换身份。
/// 实现必须无状态（access_token 等凭据缓存放在实现内部时需线程安全）。
/// </summary>
public interface ISsoProvider
{
    /// <summary>URL 段里的 Provider 标识（wecom / dingtalk / oidc / mock）</summary>
    string Id { get; }

    /// <summary>登录按钮显示名</summary>
    string DisplayName { get; }

    /// <summary>是否启用（未启用的 Provider 不出现在登录页，相关端点直接 404）</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 构造企业授权页地址。<paramref name="redirectUri"/> 是企业授权完成后的回调
    /// （指向前端 /login/sso 回调页），<paramref name="state"/> 由后端生成用于防 CSRF。
    /// <para>异步：标准 OIDC 需要先拉取发现文档（<c>.well-known/openid-configuration</c>）才知道 authorization_endpoint。</para>
    /// </summary>
    Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken ct);

    /// <summary>授权码换企业身份。code 无效 / 过期 / 调用失败返回 null（由调用方统一转成 401 语义）。</summary>
    Task<SsoIdentity?> ExchangeAsync(SsoCallback callback, CancellationToken ct);
}
