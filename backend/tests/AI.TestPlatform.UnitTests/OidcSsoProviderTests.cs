using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.TestPlatform.Api.Auth.Sso;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 标准 OIDC Provider（迭代 E·②）的单元测试。
///
/// 用**桩 HttpClient**（不走网络）模拟一个最小 IdP：发现文档 + JWKS + 令牌端点，
/// 覆盖三条安全红线——ID Token 验签、<c>nonce</c> 校验、<c>aud</c> 校验——以及授权 URL 的构造。
/// </summary>
public class OidcSsoProviderTests
{
    private const string Authority = "http://idp.test";
    private const string ClientId = "test-client";
    private const string Kid = "test-key";

    // ------------------------------------------------------------------ 授权 URL

    [Fact]
    public async Task BuildAuthorizeUrl_包含发现端点与nonce等于state()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();
        const string state = "state-abc";

        var url = await provider.BuildAuthorizeUrlAsync("http://app.test/login/sso?provider=oidc", state, default);

        Assert.StartsWith($"{Authority}/authorize?", url);
        Assert.Contains("client_id=test-client", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=openid%20profile%20email", url);
        Assert.Contains($"state={state}", url);
        // state 复用为 nonce —— 这是防重放的关键，必须出现在授权 URL 里
        Assert.Contains($"nonce={state}", url);
    }

    // ------------------------------------------------------------------ 正常换身份

    [Fact]
    public async Task Exchange_合法IDToken_返回身份()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();
        var idToken = idp.MintIdToken(nonce: "n1", sub: "user-123", name: "张三", email: "z@example.com");
        idp.TokenResponse = TokenJson(idToken);

        var identity = await provider.ExchangeAsync(
            new SsoCallback("code-1", "n1", "http://app.test/login/sso?provider=oidc"), default);

        Assert.NotNull(identity);
        Assert.Equal("user-123", identity!.Subject);
        Assert.Equal("张三", identity.DisplayName);
        Assert.Equal("z@example.com", identity.Email);
    }

    // ------------------------------------------------------------------ nonce 不匹配 → 拒绝

    [Fact]
    public async Task Exchange_nonce不匹配_拒绝()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();
        // ID Token 里的 nonce 是 n1，但回调带来的 state 是 n2 → 疑似重放，必须拒绝
        idp.TokenResponse = TokenJson(idp.MintIdToken(nonce: "n1"));

        var identity = await provider.ExchangeAsync(
            new SsoCallback("code-1", "n2", "http://app.test/login/sso?provider=oidc"), default);

        Assert.Null(identity);
    }

    // ------------------------------------------------------------------ aud 不匹配 → 拒绝

    [Fact]
    public async Task Exchange_audience不匹配_拒绝()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();
        // 令牌是发给别家应用的（aud 不同）→ 不能拿来登录本平台
        idp.TokenResponse = TokenJson(idp.MintIdToken(nonce: "n1", audience: "another-client"));

        var identity = await provider.ExchangeAsync(
            new SsoCallback("code-1", "n1", "http://app.test/login/sso?provider=oidc"), default);

        Assert.Null(identity);
    }

    // ------------------------------------------------------------------ 签名不匹配 → 拒绝

    [Fact]
    public async Task Exchange_签名不匹配_拒绝()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();
        // 用另一把私钥签名（JWKS 里没有对应公钥）→ 验签失败
        using var rogue = RSA.Create(2048);
        idp.TokenResponse = TokenJson(idp.MintIdToken(nonce: "n1", signingKey: rogue));

        var identity = await provider.ExchangeAsync(
            new SsoCallback("code-1", "n1", "http://app.test/login/sso?provider=oidc"), default);

        Assert.Null(identity);
    }

    // ------------------------------------------------------------------ state 缺失 → 拒绝

    [Fact]
    public async Task Exchange_缺少state_拒绝()
    {
        using var idp = new FakeIdp();
        var provider = idp.CreateProvider();

        var identity = await provider.ExchangeAsync(
            new SsoCallback("code-1", string.Empty, "http://app.test/login/sso?provider=oidc"), default);

        Assert.Null(identity);
    }

    // ------------------------------------------------------------------ 未配置 → 不启用

    [Fact]
    public void IsEnabled_缺Authority或ClientId_为false()
    {
        var factory = new StubHttpClientFactory(new RoutingHandler(_ => Json("{}")));

        Assert.False(new OidcSsoProvider(new SsoProviderConfig { Enabled = true, ClientId = "x" },
            factory, new OidcDiscoveryCache(factory), NullLogger<OidcSsoProvider>.Instance).IsEnabled);
        Assert.False(new OidcSsoProvider(new SsoProviderConfig { Enabled = true, Authority = Authority },
            factory, new OidcDiscoveryCache(factory), NullLogger<OidcSsoProvider>.Instance).IsEnabled);
        Assert.True(new OidcSsoProvider(
            new SsoProviderConfig { Enabled = true, Authority = Authority, ClientId = "x" },
            factory, new OidcDiscoveryCache(factory), NullLogger<OidcSsoProvider>.Instance).IsEnabled);
    }

    // ------------------------------------------------------------------ 回调地址一致性

    [Fact]
    public void CallbackRedirectUrl_登录与绑定_路径与provider一致()
    {
        var options = new SsoOptions { FrontendBaseUrl = "http://app.test/" };

        Assert.Equal("http://app.test/login/sso?provider=oidc",
            SsoLoginService.CallbackRedirectUrl(options, "oidc", "login"));
        Assert.Equal("http://app.test/login/sso/bind?provider=oidc",
            SsoLoginService.CallbackRedirectUrl(options, "oidc", "bind"));
    }

    // ================================================================== 测试替身

    private static string TokenJson(string idToken) =>
        JsonSerializer.Serialize(new { access_token = "at", id_token = idToken, token_type = "Bearer", expires_in = 3600 });

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>模拟最小 IdP：一张 RSA 密钥 + 发现文档 / JWKS / 令牌端点。</summary>
    private sealed class FakeIdp : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly StubHttpClientFactory _factory;

        public string TokenResponse { get; set; } = TokenJson(string.Empty);

        public FakeIdp()
        {
            var handler = new RoutingHandler(req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                return path switch
                {
                    "/.well-known/openid-configuration" => Json(DiscoveryJson()),
                    "/jwks" => Json(JwksJson()),
                    "/token" => Json(TokenResponse),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                };
            });
            _factory = new StubHttpClientFactory(handler);
        }

        public OidcSsoProvider CreateProvider() => new(
            new SsoProviderConfig
            {
                Enabled = true,
                Authority = Authority,
                ClientId = ClientId,
                ClientSecret = "secret",
            },
            _factory,
            new OidcDiscoveryCache(_factory),
            NullLogger<OidcSsoProvider>.Instance);

        /// <summary>签一枚 ID Token。<paramref name="signingKey"/> 缺省用本 IdP 的密钥（JWKS 里能验签）。</summary>
        public string MintIdToken(string nonce, string sub = "u1", string? name = null,
            string? email = null, string audience = ClientId, RSA? signingKey = null)
        {
            var rsa = signingKey ?? _rsa;
            var key = new RsaSecurityKey(rsa) { KeyId = signingKey is null ? Kid : "rogue" };
            var claims = new List<Claim> { new("sub", sub), new("nonce", nonce) };
            if (name is not null) claims.Add(new Claim("name", name));
            if (email is not null) claims.Add(new Claim("email", email));

            var token = new JwtSecurityToken(
                issuer: Authority,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string DiscoveryJson() => JsonSerializer.Serialize(new
        {
            issuer = Authority,
            authorization_endpoint = $"{Authority}/authorize",
            token_endpoint = $"{Authority}/token",
            jwks_uri = $"{Authority}/jwks",
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
        });

        private string JwksJson()
        {
            var p = _rsa.ExportParameters(false);
            return JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA", use = "sig", kid = Kid, alg = "RS256",
                        n = Base64UrlEncoder.Encode(p.Modulus!),
                        e = Base64UrlEncoder.Encode(p.Exponent!),
                    },
                },
            });
        }

        public void Dispose() => _rsa.Dispose();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;
        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) => _route = route;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_route(request));
    }
}
