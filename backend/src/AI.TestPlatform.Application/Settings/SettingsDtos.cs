namespace AI.TestPlatform.Application.Settings;

public record SettingsView(
    string AiBaseUrl, string AiApiKeyMasked, bool HasAiApiKey,
    string AiModel, int AiMaxTokens,
    string WebhookTokenMasked, bool HasWebhookToken,
    DateTime UpdatedAt, bool AllowPrivateNetworkImport,
    // ------------------------------ 通知配置
    bool NotifyEnabled, bool NotifyOnFailureOnly, bool NotifyPlanResultEmail,
    NotifyChannelView Wecom, NotifyChannelView Dingtalk, NotifyChannelView Feishu,
    string SmtpHost, int SmtpPort, bool SmtpUseSsl,
    string SmtpUser, string SmtpPasswordMasked, bool HasSmtpPassword, string MailTo,
    // ------------------------------ SSO 扫码登录
    bool SsoAutoProvision, string SsoFrontendBaseUrl,
    bool SsoWecomEnabled, string SsoWecomCorpId, string SsoWecomAgentId,
    string SsoWecomSecretMasked, bool HasSsoWecomSecret,
    bool SsoDingtalkEnabled, string SsoDingtalkClientId,
    string SsoDingtalkClientSecretMasked, bool HasSsoDingtalkClientSecret,
    // ------------------------------ 标准 OIDC
    bool SsoOidcEnabled, string SsoOidcAuthority, string SsoOidcClientId,
    string SsoOidcClientSecretMasked, bool HasSsoOidcClientSecret,
    string SsoOidcDisplayName, string SsoOidcScopes,
    // ------------------------------ M8 Agent 自愈闭环（系统级总开关）
    bool AgentLoopEnabled);

/// <summary>Webhook 渠道视图：地址脱敏 + 是否已配置</summary>
public record NotifyChannelView(string WebhookMasked, bool Configured);

public record UpdateSettingsRequest(
    string? AiBaseUrl, string? AiApiKey, string? AiModel,
    int? AiMaxTokens, string? WebhookToken, bool? AllowPrivateNetworkImport,
    // ------------------------------ 通知配置
    bool? NotifyEnabled, bool? NotifyOnFailureOnly, bool? NotifyPlanResultEmail,
    string? NotifyWecomWebhook, string? NotifyDingtalkWebhook, string? NotifyFeishuWebhook,
    string? SmtpHost, int? SmtpPort, bool? SmtpUseSsl,
    string SmtpUser, string? SmtpPassword, string? MailTo,
    // ------------------------------ SSO 扫码登录
    bool? SsoAutoProvision, string? SsoFrontendBaseUrl,
    bool? SsoWecomEnabled, string? SsoWecomCorpId, string? SsoWecomAgentId, string? SsoWecomSecret,
    bool? SsoDingtalkEnabled, string? SsoDingtalkClientId, string? SsoDingtalkClientSecret,
    // ------------------------------ 标准 OIDC
    bool? SsoOidcEnabled, string? SsoOidcAuthority, string? SsoOidcClientId, string? SsoOidcClientSecret,
    string? SsoOidcDisplayName, string? SsoOidcScopes,
    /// <summary>置空 Webhook 地址（前端「清除」按钮用），如 "wecom" / "dingtalk" / "feishu"</summary>
    string? ClearWebhook,
    /// <summary>M8 Agent 自愈闭环系统级总开关（系统配置页）</summary>
    bool? AgentLoopEnabled);

public record TestConnectionResult(bool Ok, string Model, int? LatencyMs, string? Message);

/// <summary>通知渠道测试结果（Message 供前端直接展示，失败原因在 Channels 里逐条给出）</summary>
public record NotifyTestResult(bool Ok, IReadOnlyList<NotifyTestChannelResult> Channels, string? Message = null);

public record NotifyTestChannelResult(string Channel, bool Ok, string? Error);

/// <summary>渠道测试请求：Channel 为空表示向所有已配置渠道发送</summary>
public record NotifyTestRequest(string? Channel);

/// <summary>
/// 发送测试邮件请求。
/// To 留空则用设置里的固定收件人；PlanId 留空则自动取最近跑过轮次的计划。
/// </summary>
public record TestMailRequest(string? To, Guid? PlanId);
