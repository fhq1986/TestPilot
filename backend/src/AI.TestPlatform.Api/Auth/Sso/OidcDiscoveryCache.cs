using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>
/// OIDC 发现文档缓存（迭代 E·②）。
///
/// 标准 OIDC 的端点（authorization / token / jwks / issuer）都从
/// <c>{Authority}/.well-known/openid-configuration</c> 动态发现，不能写死。
/// <see cref="ConfigurationManager{T}"/> 自带缓存与到期自动刷新（含 jwks 轮换），
/// 这里只按 Authority 去重，避免每次请求都新建一个管理器。
///
/// 注册为**单例**：缓存需要跨请求存活才有意义。
/// </summary>
public sealed class OidcDiscoveryCache
{
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IHttpClientFactory _httpFactory;

    public OidcDiscoveryCache(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    /// <summary>拉取（或命中缓存）某个 Authority 的 OIDC 元数据。</summary>
    public Task<OpenIdConnectConfiguration> GetAsync(string authority, CancellationToken ct)
    {
        var key = authority.TrimEnd('/');
        var manager = _managers.GetOrAdd(key, Build);
        return manager.GetConfigurationAsync(ct);
    }

    private ConfigurationManager<OpenIdConnectConfiguration> Build(string authority)
    {
        var metadataAddress = $"{authority}/.well-known/openid-configuration";
        // 复用 "sso" 命名 HttpClient（超时/代理策略与企微/钉钉一致）。
        // RequireHttps 跟随 Authority 协议：生产必须 https，本地 Keycloak/自建 IdP 允许 http 便于联调。
        var retriever = new HttpDocumentRetriever(_httpFactory.CreateClient("sso"))
        {
            RequireHttps = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
        };
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress, new OpenIdConnectConfigurationRetriever(), retriever);
    }
}
