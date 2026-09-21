namespace AI.TestPlatform.Domain.Entities;

// 系统配置（单行表，Id 固定为 1）
public class SystemConfig
{
    public int Id { get; set; }
    public string AiBaseUrl { get; set; } = string.Empty;
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = string.Empty;
    public int AiMaxTokens { get; set; } = 8192;
    public string WebhookToken { get; set; } = string.Empty;
    public bool AllowPrivateNetworkImport { get; set; } = true;

    // ------------------------------ 通知配置
    public bool NotifyEnabled { get; set; }
    /// <summary>仅失败时通知（关闭则每次执行结束都通知）</summary>
    public bool NotifyOnFailureOnly { get; set; } = true;
    public string NotifyWecomWebhook { get; set; } = string.Empty;
    public string NotifyDingtalkWebhook { get; set; } = string.Empty;
    public string NotifyFeishuWebhook { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 465;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    /// <summary>收件人，逗号分隔。作为「找不到具体负责人」时的兜底收件人</summary>
    public string MailTo { get; set; } = string.Empty;

    /// <summary>
    /// 测试计划轮次完成后，把验收结果发给项目的测试负责人与计划的负责人。
    /// 与 <see cref="NotifyOnFailureOnly"/> 刻意解耦：那是一道"别报警刷屏"的闸门，
    /// 而这封邮件是**定向的验收凭证**（还带 xlsx 附件），达标结果同样需要留存，
    /// 所以达标不发会直接毁掉它的用途。
    /// </summary>
    public bool NotifyPlanResultEmail { get; set; } = true;

    // ------------------------------ SSO 扫码登录
    // 设置页可改、保存即生效（appsettings 的 Sso 节仅在首次种子时读入，此后数据库为权威）。
    // 密钥类字段（企业微信 Secret / 钉钉 ClientSecret）与 AI Key 同规：回显只给掩码，留空保存表示保留原值。
    /// <summary>扫码登录的新用户自动开通平台账号（角色 Viewer）</summary>
    public bool SsoAutoProvision { get; set; }
    /// <summary>前端站点根地址，企业授权回调跳到 {SsoFrontendBaseUrl}/login/sso</summary>
    public string SsoFrontendBaseUrl { get; set; } = string.Empty;
    public bool SsoWecomEnabled { get; set; }
    public string SsoWecomCorpId { get; set; } = string.Empty;
    public string SsoWecomAgentId { get; set; } = string.Empty;
    public string SsoWecomSecret { get; set; } = string.Empty;
    public bool SsoDingtalkEnabled { get; set; }
    public string SsoDingtalkClientId { get; set; } = string.Empty;
    public string SsoDingtalkClientSecret { get; set; } = string.Empty;

    // ------------------------------ M8 Agent 自愈闭环（系统级总开关）
    // 与 SSO 同规：系统配置页可改、保存即生效、数据库为权威。默认关。
    // 这是总闸——关闭后所有项目都不跑 Agent Loop（项目级 AgentLoopEnabled 再叠加一层与门）。
    /// <summary>是否启用 Agent 失败自愈闭环（系统级总开关）</summary>
    public bool AgentLoopEnabled { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
