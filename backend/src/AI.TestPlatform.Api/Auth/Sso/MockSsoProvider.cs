namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// 开发/演示用 Mock Provider：跳过真实企业授权，code 直接是 mock:{用户名}。
/// 只应在开发环境通过配置开启（Sso:Providers:mock:Enabled=true），生产配置默认关闭。
/// 用它可以在没有企业微信/钉钉应用凭据时端到端联调整条 SSO 链路。
/// </summary>
public class MockSsoProvider : ISsoProvider
{
    private readonly SsoProviderConfig _config;

    public MockSsoProvider(SsoProviderConfig config) => _config = config;

    public string Id => "mock";
    public string DisplayName => _config.DisplayName ?? "演示登录";
    public bool IsEnabled => _config.Enabled;

    public Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken ct)
    {
        // Mock 没有「人工扫码」环节：直接以匿名身份发一个 code 回到前端回调页。
        var identity = string.IsNullOrWhiteSpace(_config.Secret) ? "demo" : _config.Secret!;
        var sep = redirectUri.Contains('?') ? '&' : '?';
        return Task.FromResult($"{redirectUri}{sep}code=mock:{Uri.EscapeDataString(identity)}&state={Uri.EscapeDataString(state)}");
    }

    public Task<SsoIdentity?> ExchangeAsync(SsoCallback callback, CancellationToken ct)
    {
        var code = callback.Code;
        if (!code.StartsWith("mock:", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<SsoIdentity?>(null);

        var subject = code[5..].Trim();
        if (subject.Length == 0 || subject.Length > 100)
            return Task.FromResult<SsoIdentity?>(null);

        return Task.FromResult<SsoIdentity?>(new SsoIdentity(subject, subject, null));
    }
}
