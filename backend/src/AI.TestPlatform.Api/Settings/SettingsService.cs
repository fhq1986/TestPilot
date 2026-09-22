using AI.TestPlatform.Api.Auth.Sso;
using AI.TestPlatform.Application.Settings;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AI.TestPlatform.Api.Settings;

// 系统配置服务：单行表（Id=1），无行时按默认值种子（WebhookToken 取 appsettings 现值）
public class SettingsService
{
    private readonly TestDbContext _db;
    private readonly IConfiguration _configuration;

    public SettingsService(TestDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<SystemConfig> GetAsync(CancellationToken ct)
    {
        var config = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Id == 1, ct);
        if (config is not null) return config;
        config = new SystemConfig
        {
            Id = 1,
            AiBaseUrl = "https://api.deepseek.com",
            AiModel = "deepseek-v4-flash",
            AiMaxTokens = 8192,
            WebhookToken = _configuration["Webhook:Token"] ?? "",
            // SSO 以 appsettings 现值为首次种子；此后数据库为权威，设置页保存即生效（无需重启）
            SsoAutoProvision = _configuration.GetValue<bool>("Sso:AutoProvision"),
            SsoFrontendBaseUrl = _configuration["Sso:FrontendBaseUrl"] ?? "",
            SsoWecomEnabled = _configuration.GetValue<bool>("Sso:Providers:wecom:Enabled"),
            SsoWecomCorpId = _configuration["Sso:Providers:wecom:CorpId"] ?? "",
            SsoWecomAgentId = _configuration["Sso:Providers:wecom:AgentId"] ?? "",
            SsoWecomSecret = _configuration["Sso:Providers:wecom:Secret"] ?? "",
            SsoDingtalkEnabled = _configuration.GetValue<bool>("Sso:Providers:dingtalk:Enabled"),
            SsoDingtalkClientId = _configuration["Sso:Providers:dingtalk:ClientId"] ?? "",
            SsoDingtalkClientSecret = _configuration["Sso:Providers:dingtalk:ClientSecret"] ?? "",
            // 标准 OIDC 种子
            SsoOidcEnabled = _configuration.GetValue<bool>("Sso:Providers:oidc:Enabled"),
            SsoOidcAuthority = _configuration["Sso:Providers:oidc:Authority"] ?? "",
            SsoOidcClientId = _configuration["Sso:Providers:oidc:ClientId"] ?? "",
            SsoOidcClientSecret = _configuration["Sso:Providers:oidc:ClientSecret"] ?? "",
            SsoOidcDisplayName = _configuration["Sso:Providers:oidc:DisplayName"] ?? "",
            SsoOidcScopes = _configuration["Sso:Providers:oidc:Scopes"] ?? "",
        };
        _db.SystemConfigs.Add(config);
        await _db.SaveChangesAsync(ct);
        return config;
    }

    public async Task<SettingsView> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct)
    {
        var config = await GetAsync(ct);
        if (!string.IsNullOrWhiteSpace(request.AiBaseUrl)) config.AiBaseUrl = request.AiBaseUrl;
        if (!string.IsNullOrWhiteSpace(request.AiApiKey)) config.AiApiKey = request.AiApiKey;
        if (!string.IsNullOrWhiteSpace(request.AiModel)) config.AiModel = request.AiModel;
        if (request.AiMaxTokens.HasValue) config.AiMaxTokens = request.AiMaxTokens.Value;
        if (!string.IsNullOrWhiteSpace(request.WebhookToken)) config.WebhookToken = request.WebhookToken;
        if (request.AllowPrivateNetworkImport.HasValue)
            config.AllowPrivateNetworkImport = request.AllowPrivateNetworkImport.Value;

        // ------------------------------ 通知配置
        if (request.NotifyEnabled.HasValue) config.NotifyEnabled = request.NotifyEnabled.Value;
        if (request.NotifyOnFailureOnly.HasValue) config.NotifyOnFailureOnly = request.NotifyOnFailureOnly.Value;
        if (request.NotifyPlanResultEmail.HasValue) config.NotifyPlanResultEmail = request.NotifyPlanResultEmail.Value;
        if (!string.IsNullOrWhiteSpace(request.NotifyWecomWebhook)) config.NotifyWecomWebhook = request.NotifyWecomWebhook.Trim();
        if (!string.IsNullOrWhiteSpace(request.NotifyDingtalkWebhook)) config.NotifyDingtalkWebhook = request.NotifyDingtalkWebhook.Trim();
        if (!string.IsNullOrWhiteSpace(request.NotifyFeishuWebhook)) config.NotifyFeishuWebhook = request.NotifyFeishuWebhook.Trim();
        if (!string.IsNullOrWhiteSpace(request.SmtpHost)) config.SmtpHost = request.SmtpHost.Trim();
        if (request.SmtpPort.HasValue) config.SmtpPort = request.SmtpPort.Value;
        if (request.SmtpUseSsl.HasValue) config.SmtpUseSsl = request.SmtpUseSsl.Value;
        if (!string.IsNullOrWhiteSpace(request.SmtpUser)) config.SmtpUser = request.SmtpUser.Trim();
        // 邮件授权码/密码：留空视为不修改（回显始终脱敏，避免误清空）
        if (!string.IsNullOrWhiteSpace(request.SmtpPassword)) config.SmtpPassword = request.SmtpPassword;
        if (!string.IsNullOrWhiteSpace(request.MailTo)) config.MailTo = request.MailTo.Trim();

        // ------------------------------ SSO 扫码登录
        if (request.SsoAutoProvision.HasValue) config.SsoAutoProvision = request.SsoAutoProvision.Value;
        // 非密钥字段直接回显编辑，传 null 不修改、传空串即清空
        if (request.SsoFrontendBaseUrl != null) config.SsoFrontendBaseUrl = request.SsoFrontendBaseUrl.Trim();
        if (request.SsoWecomEnabled.HasValue) config.SsoWecomEnabled = request.SsoWecomEnabled.Value;
        if (request.SsoWecomCorpId != null) config.SsoWecomCorpId = request.SsoWecomCorpId.Trim();
        if (request.SsoWecomAgentId != null) config.SsoWecomAgentId = request.SsoWecomAgentId.Trim();
        // Secret：留空视为不修改（回显始终脱敏）
        if (!string.IsNullOrWhiteSpace(request.SsoWecomSecret)) config.SsoWecomSecret = request.SsoWecomSecret.Trim();
        if (request.SsoDingtalkEnabled.HasValue) config.SsoDingtalkEnabled = request.SsoDingtalkEnabled.Value;
        if (request.SsoDingtalkClientId != null) config.SsoDingtalkClientId = request.SsoDingtalkClientId.Trim();
        if (!string.IsNullOrWhiteSpace(request.SsoDingtalkClientSecret))
            config.SsoDingtalkClientSecret = request.SsoDingtalkClientSecret.Trim();
        // 标准 OIDC
        if (request.SsoOidcEnabled.HasValue) config.SsoOidcEnabled = request.SsoOidcEnabled.Value;
        if (request.SsoOidcAuthority != null) config.SsoOidcAuthority = request.SsoOidcAuthority.Trim();
        if (request.SsoOidcClientId != null) config.SsoOidcClientId = request.SsoOidcClientId.Trim();
        if (!string.IsNullOrWhiteSpace(request.SsoOidcClientSecret))
            config.SsoOidcClientSecret = request.SsoOidcClientSecret.Trim();
        if (request.SsoOidcDisplayName != null) config.SsoOidcDisplayName = request.SsoOidcDisplayName.Trim();
        if (request.SsoOidcScopes != null) config.SsoOidcScopes = request.SsoOidcScopes.Trim();

        // 显式清空（前端「清除」按钮）：wecom / dingtalk / feishu / mailto / smtppassword
        foreach (var key in (request.ClearWebhook ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(k => k.ToLowerInvariant()))
        {
            switch (key)
            {
                case "wecom": config.NotifyWecomWebhook = string.Empty; break;
                case "dingtalk": config.NotifyDingtalkWebhook = string.Empty; break;
                case "feishu": config.NotifyFeishuWebhook = string.Empty; break;
                case "mailto": config.MailTo = string.Empty; break;
                case "smtppassword": config.SmtpPassword = string.Empty; break;
            }
        }

        // ------------------------------ M8 Agent 自愈闭环（系统级总开关）
        if (request.AgentLoopEnabled.HasValue) config.AgentLoopEnabled = request.AgentLoopEnabled.Value;

        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToView(config);
    }

    public static string Mask(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= 8 ? "***"
        : $"{value[..3]}***{value[^4..]}";

    /// <summary>Webhook 地址脱敏：只保留协议与主机，路径与密钥全部隐去</summary>
    public static string MaskUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "***";
        return $"{uri.Scheme}://{uri.Host}/***";
    }

    private static NotifyChannelView Channel(string? url) =>
        new(MaskUrl(url), !string.IsNullOrWhiteSpace(url));

    public SettingsView ToView(SystemConfig c) => new(
        c.AiBaseUrl, Mask(c.AiApiKey), !string.IsNullOrEmpty(c.AiApiKey),
        c.AiModel, c.AiMaxTokens,
        Mask(c.WebhookToken), !string.IsNullOrEmpty(c.WebhookToken),
        c.UpdatedAt, c.AllowPrivateNetworkImport,
        c.NotifyEnabled, c.NotifyOnFailureOnly, c.NotifyPlanResultEmail,
        Channel(c.NotifyWecomWebhook), Channel(c.NotifyDingtalkWebhook), Channel(c.NotifyFeishuWebhook),
        c.SmtpHost, c.SmtpPort, c.SmtpUseSsl,
        c.SmtpUser, Mask(c.SmtpPassword), !string.IsNullOrEmpty(c.SmtpPassword), c.MailTo,
        c.SsoAutoProvision, c.SsoFrontendBaseUrl,
        c.SsoWecomEnabled, c.SsoWecomCorpId, c.SsoWecomAgentId,
        Mask(c.SsoWecomSecret), !string.IsNullOrEmpty(c.SsoWecomSecret),
        c.SsoDingtalkEnabled, c.SsoDingtalkClientId,
        Mask(c.SsoDingtalkClientSecret), !string.IsNullOrEmpty(c.SsoDingtalkClientSecret),
        c.SsoOidcEnabled, c.SsoOidcAuthority, c.SsoOidcClientId,
        Mask(c.SsoOidcClientSecret), !string.IsNullOrEmpty(c.SsoOidcClientSecret),
        c.SsoOidcDisplayName, c.SsoOidcScopes,
        c.AgentLoopEnabled);

    /// <summary>
    /// 构建当前生效的 SSO 运行时配置（数据库为权威，设置页保存即生效，无需重启）。
    /// mock Provider 不进设置页（仅开发环境可用），仍从 appsettings 读开关。
    /// </summary>
    public async Task<SsoOptions> GetSsoOptionsAsync(CancellationToken ct)
    {
        var c = await GetAsync(ct);
        var options = new SsoOptions
        {
            AutoProvision = c.SsoAutoProvision,
            // 旧部署的 SystemConfig 行没有 SSO 列值（空串）：回落 appsettings 种子，避免生成相对路径的回调地址
            FrontendBaseUrl = string.IsNullOrWhiteSpace(c.SsoFrontendBaseUrl)
                ? _configuration["Sso:FrontendBaseUrl"]
                : c.SsoFrontendBaseUrl,
            Providers =
            {
                ["wecom"] = new SsoProviderConfig
                {
                    Enabled = c.SsoWecomEnabled,
                    CorpId = c.SsoWecomCorpId,
                    AgentId = c.SsoWecomAgentId,
                    Secret = c.SsoWecomSecret,
                },
                ["dingtalk"] = new SsoProviderConfig
                {
                    Enabled = c.SsoDingtalkEnabled,
                    ClientId = c.SsoDingtalkClientId,
                    ClientSecret = c.SsoDingtalkClientSecret,
                },
                ["oidc"] = new SsoProviderConfig
                {
                    Enabled = c.SsoOidcEnabled,
                    ClientId = c.SsoOidcClientId,
                    ClientSecret = c.SsoOidcClientSecret,
                    Authority = c.SsoOidcAuthority,
                    DisplayName = string.IsNullOrWhiteSpace(c.SsoOidcDisplayName) ? null : c.SsoOidcDisplayName,
                    Scopes = string.IsNullOrWhiteSpace(c.SsoOidcScopes) ? null : c.SsoOidcScopes,
                },
                ["mock"] = new SsoProviderConfig
                {
                    Enabled = _configuration.GetValue<bool>("Sso:Providers:mock:Enabled"),
                },
            },
        };
        return options;
    }
}
