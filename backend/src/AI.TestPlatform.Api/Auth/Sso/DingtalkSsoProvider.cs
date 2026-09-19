using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// 钉钉扫码登录实现。走新版 v1.0 接口：
/// login.dingtalk.com/oauth2/auth → code → oauth2/userAccessToken → contact/users/me 拿 unionId。
/// </summary>
public class DingtalkSsoProvider : ISsoProvider
{
    private readonly SsoProviderConfig _config;
    private readonly IHttpClientFactory _httpFactory;

    public DingtalkSsoProvider(SsoProviderConfig config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public string Id => "dingtalk";
    public string DisplayName => _config.DisplayName ?? "钉钉";
    public bool IsEnabled => _config.Enabled
        && !string.IsNullOrWhiteSpace(_config.ClientId)
        && !string.IsNullOrWhiteSpace(_config.ClientSecret);

    public string BuildAuthorizeUrl(string redirectUri, string state)
    {
        return "https://login.dingtalk.com/oauth2/auth" +
               $"?client_id={Uri.EscapeDataString(_config.ClientId ?? string.Empty)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               "&response_type=code&scope=openid&prompt=consent" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<SsoIdentity?> ExchangeAsync(string code, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("sso");

        // code 换用户级 access_token（POST JSON，key 为 camelCase 的 grantType）
        var tokenRes = await http.PostAsync(
            "https://api.dingtalk.com/v1.0/oauth2/userAccessToken",
            new StringContent(
                JsonSerializer.Serialize(new DingTalkTokenRequest(
                    _config.ClientId ?? string.Empty,
                    _config.ClientSecret ?? string.Empty,
                    code,
                    "authorization_code")),
                Encoding.UTF8,
                "application/json"),
            ct);
        if (!tokenRes.IsSuccessStatusCode)
            return null;
        var token = await tokenRes.Content.ReadFromJsonAsync<DingTalkTokenResponse>(ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            return null;

        // unionId 是同一企业开放平台账号体系下的稳定唯一标识。
        // access_token 走请求头 x-acs-dingtalk-access-token（钉钉 v1.0 约定），不能用 Authorization。
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.dingtalk.com/v1.0/contact/users/me");
        req.Headers.TryAddWithoutValidation("x-acs-dingtalk-access-token", token.AccessToken);
        var meRes = await http.SendAsync(req, ct);
        if (!meRes.IsSuccessStatusCode)
            return null;
        var me = await meRes.Content.ReadFromJsonAsync<DingTalkMeResponse>(ct);
        if (me is null || string.IsNullOrEmpty(me.UnionId))
            return null;

        return new SsoIdentity(me.UnionId, me.Nick, me.Email);
    }

    private sealed record DingTalkTokenRequest(
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("clientSecret")] string ClientSecret,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("grantType")] string GrantType);

    private sealed record DingTalkTokenResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("expireIn")] long ExpireIn);

    private sealed record DingTalkMeResponse(
        [property: JsonPropertyName("unionId")] string? UnionId,
        [property: JsonPropertyName("nick")] string? Nick,
        [property: JsonPropertyName("email")] string? Email);
}
