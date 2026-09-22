using System.Net.Http.Json;

namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// 企业微信（WeCom）扫码登录实现。走自建应用 OAuth2：
/// 授权页 login.work.weixin.qq.com → code → corptoken → auth/getuserinfo 拿 userid。
/// </summary>
public class WecomSsoProvider : ISsoProvider
{
    private readonly SsoProviderConfig _config;
    private readonly IHttpClientFactory _httpFactory;

    // corp access_token 缓存（7200s 有效期，提前 5 分钟刷新）
    private readonly object _tokenLock = new();
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt;

    public WecomSsoProvider(SsoProviderConfig config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public string Id => "wecom";
    public string DisplayName => _config.DisplayName ?? "企业微信";
    public bool IsEnabled => _config.Enabled
        && !string.IsNullOrWhiteSpace(_config.CorpId)
        && !string.IsNullOrWhiteSpace(_config.Secret);

    public Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken ct)
    {
        // 企微新版扫码登录页（wwlogin）。login_type=CorpApp 表示自建应用网页授权。
        var url = $"https://login.work.weixin.qq.com/wwlogin/sso/login" +
                  $"?login_type=CorpApp&appid={Uri.EscapeDataString(_config.CorpId ?? string.Empty)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
        if (int.TryParse(_config.AgentId, out var agentId))
            url += $"&agentid={agentId}";
        return Task.FromResult(url);
    }

    public async Task<SsoIdentity?> ExchangeAsync(SsoCallback callback, CancellationToken ct)
    {
        var code = callback.Code;
        var token = await GetCorpAccessTokenAsync(ct);
        if (token is null)
            return null;

        var http = _httpFactory.CreateClient("sso");

        // code 换 userid（一次性，5 分钟有效）
        var userinfo = await http.GetFromJsonAsync<WeComUserInfoResponse>(
            $"https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token={token}&code={Uri.EscapeDataString(code)}",
            ct);
        if (userinfo is null || userinfo.ErrCode != 0 || string.IsNullOrEmpty(userinfo.UserId))
            return null;

        // 拉取名与邮箱（可选：部分企业未开通讯录邮箱权限，取不到就留空）
        string? displayName = null, email = null;
        var detail = await http.GetFromJsonAsync<WeComUserDetailResponse>(
            $"https://qyapi.weixin.qq.com/cgi-bin/user/get?access_token={token}&userid={Uri.EscapeDataString(userinfo.UserId)}",
            ct);
        if (detail is not null && detail.ErrCode == 0)
        {
            displayName = string.IsNullOrWhiteSpace(detail.Name) ? null : detail.Name;
            email = string.IsNullOrWhiteSpace(detail.BizMail) ? null : detail.BizMail;
        }

        return new SsoIdentity(userinfo.UserId, displayName, email);
    }

    private async Task<string?> GetCorpAccessTokenAsync(CancellationToken ct)
    {
        lock (_tokenLock)
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _cachedToken;
        }

        var http = _httpFactory.CreateClient("sso");
        var res = await http.GetFromJsonAsync<WeComTokenResponse>(
            $"https://qyapi.weixin.qq.com/cgi-bin/gettoken" +
            $"?corpid={Uri.EscapeDataString(_config.CorpId ?? string.Empty)}" +
            $"&corpsecret={Uri.EscapeDataString(_config.Secret ?? string.Empty)}",
            ct);
        if (res is null || res.ErrCode != 0 || string.IsNullOrEmpty(res.AccessToken))
            return null;

        lock (_tokenLock)
        {
            _cachedToken = res.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, res.ExpiresIn - 300));
        }
        return res.AccessToken;
    }

    private sealed record WeComTokenResponse(int ErrCode, string? AccessToken, int ExpiresIn);
    private sealed record WeComUserInfoResponse(int ErrCode, string? UserId);
    private sealed record WeComUserDetailResponse(int ErrCode, string? Name, string? BizMail);
}
