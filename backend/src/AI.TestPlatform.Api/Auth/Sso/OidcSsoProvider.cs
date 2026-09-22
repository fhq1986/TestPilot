using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// 标准 OIDC 授权码流登录（迭代 E·②）。适配 Azure AD / Okta / Keycloak / Auth0 / Google 等
/// 一切符合 OpenID Connect Discovery 的身份源——端点全部从发现文档读取，不写死。
///
/// 流程：authorize_endpoint(code) → token_endpoint(code 换 id_token) → 校验 ID Token 签名/iss/aud/exp/nonce → 取 sub。
/// 安全要点：
/// - ID Token 用 IdP 的 JWKS 验签（<see cref="OidcDiscoveryCache"/> 拉取，含密钥轮换）；
/// - 校验 <c>iss</c>（防换发）、<c>aud</c>=ClientId（防拿别家应用的令牌）、<c>exp</c>（防过期）、
///   <c>nonce</c>=state（防重放/注入，见 <see cref="SsoCallback.State"/>）；
/// - 这是**机密客户端**（带 ClientSecret），故用 client_secret 换令牌，不需要 PKCE。
/// </summary>
public class OidcSsoProvider : ISsoProvider
{
    private readonly SsoProviderConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OidcDiscoveryCache _discovery;
    private readonly ILogger<OidcSsoProvider> _logger;

    public OidcSsoProvider(
        SsoProviderConfig config,
        IHttpClientFactory httpFactory,
        OidcDiscoveryCache discovery,
        ILogger<OidcSsoProvider> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _discovery = discovery;
        _logger = logger;
    }

    public string Id => "oidc";

    public string DisplayName => _config.DisplayName ?? "单点登录";

    public bool IsEnabled => _config.Enabled
        && !string.IsNullOrWhiteSpace(_config.Authority)
        && !string.IsNullOrWhiteSpace(_config.ClientId);

    /// <summary>请求的 scope：缺省 <c>openid profile email</c>（openid 是 OIDC 必需项）。</summary>
    private string Scopes => string.IsNullOrWhiteSpace(_config.Scopes)
        ? "openid profile email"
        : _config.Scopes!.Trim();

    public async Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken ct)
    {
        var meta = await _discovery.GetAsync(_config.Authority!, ct);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _config.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["state"] = state,
            // state 复用为 nonce：两者同为"一次性随机值"，ExchangeAsync 时校验 ID Token 的 nonce 与之相等
            ["nonce"] = state,
        };
        return meta.AuthorizationEndpoint + "?" + BuildQuery(query);
    }

    public async Task<SsoIdentity?> ExchangeAsync(SsoCallback callback, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callback.State))
            return null; // OIDC 必须有 state（兼作 nonce），否则无法校验

        var meta = await _discovery.GetAsync(_config.Authority!, ct);
        var http = _httpFactory.CreateClient("sso");

        // ---- 授权码换令牌 ----
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = callback.Code,
            ["redirect_uri"] = callback.RedirectUri,
            ["client_id"] = _config.ClientId!,
        };
        if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            form["client_secret"] = _config.ClientSecret!;

        using var res = await http.PostAsync(meta.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("OIDC 令牌交换失败：{Status} {Reason}", (int)res.StatusCode, res.ReasonPhrase);
            return null;
        }

        var token = await res.Content.ReadFromJsonAsync<OidcTokenResponse>(ct);
        if (token is null || string.IsNullOrWhiteSpace(token.IdToken))
        {
            _logger.LogWarning("OIDC 令牌响应缺少 id_token");
            return null;
        }

        return ValidateIdToken(token.IdToken, meta, callback.State);
    }

    /// <summary>校验 ID Token 并抽出平台身份。任一步不通过返回 null（调用方转 401）。</summary>
    private SsoIdentity? ValidateIdToken(string idToken, OpenIdConnectConfiguration meta, string expectedNonce)
    {
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = meta.Issuer,
            ValidateAudience = true,
            ValidAudience = _config.ClientId,
            ValidateLifetime = true,
            // 允许一点点时钟偏差（IdP 与平台时钟未必严格同步）
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = meta.SigningKeys,
        };

        try
        {
            // ValidateToken 负责验签 + iss/aud/exp 校验；身份字段一律从**原始 payload** 取，
            // 因为 JwtSecurityTokenHandler 默认会把 name/sub 映射成 WS-Federation 长 URI，
            // 用 principal.FindFirst("sub") 会取不到（踩过一次）。
            handler.ValidateToken(idToken, parameters, out var validated);
            if (validated is not JwtSecurityToken jwt)
            {
                _logger.LogWarning("OIDC 令牌类型异常，无法解析 payload");
                return null;
            }

            // nonce 校验：必须等于本次授权发出的 state（防 ID Token 重放/注入）
            if (!jwt.Payload.TryGetValue("nonce", out var nonceObj)
                || !string.Equals(nonceObj?.ToString(), expectedNonce, StringComparison.Ordinal))
            {
                _logger.LogWarning("OIDC nonce 校验失败（可能为重放攻击）");
                return null;
            }

            string? Claim(string type) =>
                jwt.Payload.TryGetValue(type, out var v) ? v?.ToString() : null;

            var subject = Claim("sub");
            if (string.IsNullOrWhiteSpace(subject))
            {
                _logger.LogWarning("OIDC ID Token 缺少 sub 声明");
                return null;
            }

            var displayName = FirstNonEmpty(
                Claim("name"),
                Claim("preferred_username"),
                Claim("email"),
                subject);
            var email = Claim("email");

            return new SsoIdentity(subject, displayName, email);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "OIDC ID Token 校验失败");
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string BuildQuery(IReadOnlyDictionary<string, string?> values) =>
        string.Join('&', values
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

    private sealed record OidcTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
