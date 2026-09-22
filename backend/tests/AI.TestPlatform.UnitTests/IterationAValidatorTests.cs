using AI.TestPlatform.Application.CI;
using AI.TestPlatform.Application.Schedules;
using AI.TestPlatform.Application.Settings;

namespace AI.TestPlatform.UnitTests;

public class ScheduleValidatorTests
{
    private readonly CreateScheduleRequestValidator _create = new();
    private readonly UpdateScheduleRequestValidator _update = new();
    private readonly CronPreviewRequestValidator _preview = new();

    private static CreateScheduleRequest Create(
        string cron = "0 2 * * *", string name = "夜间回归", Guid? projectId = null,
        string? module = null, string? priority = null, List<Guid>? ids = null)
        => new(projectId ?? Guid.NewGuid(), name, cron, true, module, priority, ids, null);

    [Fact]
    public void 合法请求通过()
        => Assert.True(_create.Validate(Create()).IsValid);

    [Fact]
    public void 非法Cron被拒绝()
    {
        var result = _create.Validate(Create(cron: "0 2 * *"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cron"));
    }

    [Fact]
    public void 空名称被拒绝()
        => Assert.False(_create.Validate(Create(name: "  ")).IsValid);

    [Fact]
    public void 未指定项目被拒绝()
        => Assert.False(_create.Validate(new CreateScheduleRequest(
            Guid.Empty, "任务", "0 2 * * *", true, null, null, null, null)).IsValid);

    [Fact]
    public void 用例数量超上限被拒绝()
    {
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();
        var result = _create.Validate(Create(ids: ids));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("500"));
    }

    [Fact]
    public void 更新请求同样校验Cron()
        => Assert.False(_update.Validate(new UpdateScheduleRequest(
            "任务", "bad cron here", true, null, null, null, null)).IsValid);

    [Fact]
    public void Cron预览限制次数范围()
    {
        Assert.True(_preview.Validate(new CronPreviewRequest("0 2 * * *", 5)).IsValid);
        Assert.False(_preview.Validate(new CronPreviewRequest("0 2 * * *", 0)).IsValid);
        Assert.False(_preview.Validate(new CronPreviewRequest("0 2 * * *", 50)).IsValid);
    }
}

public class CiWebhookValidatorTests
{
    private readonly WebhookTriggerRequestValidator _validator = new();

    private static WebhookTriggerRequest Request(
        List<Guid>? ids = null, Guid? projectId = null, string? module = null, string? priority = null)
        => new(ids, projectId, module, priority, null, null, null, null, null);

    [Fact]
    public void 给定用例列表通过()
        => Assert.True(_validator.Validate(Request(ids: new List<Guid> { Guid.NewGuid() })).IsValid);

    [Fact]
    public void 仅给定项目也通过_按条件筛选用例()
        => Assert.True(_validator.Validate(Request(projectId: Guid.NewGuid(), module: "登录", priority: "P0")).IsValid);

    [Fact]
    public void 未指定用例与项目被拒绝()
    {
        var result = _validator.Validate(Request());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("用例列表"));
    }

    [Fact]
    public void 空用例列表且无项目被拒绝()
        => Assert.False(_validator.Validate(Request(ids: new List<Guid>())).IsValid);

    [Fact]
    public void 用例数超上限被拒绝()
    {
        var ids = Enumerable.Range(0, WebhookTriggerRequestValidator.MaxCases + 1)
            .Select(_ => Guid.NewGuid()).ToList();
        Assert.False(_validator.Validate(Request(ids: ids)).IsValid);
    }

    [Fact]
    public void 构建上下文超长被拒绝()
    {
        var request = new WebhookTriggerRequest(
            new List<Guid> { Guid.NewGuid() }, null, null, null, null,
            new string('a', 65), null, null, null);
        Assert.False(_validator.Validate(request).IsValid);
    }
}

public class NotificationSettingsValidatorTests
{
    private readonly UpdateSettingsRequestValidator _validator = new();

    // 用具名参数构造：这个 record 有近 20 个字段，按位置传的话
    // 每次在中间加一个配置项都会把后面全部顶偏（已经踩过一次，报的还是莫名其妙的 ClearWebhook 缺参）
    private static UpdateSettingsRequest Request(
        string? wecom = null, string? dingtalk = null, string? feishu = null, string? mailTo = null)
        => new(
            AiBaseUrl: null, AiApiKey: null, AiModel: null,
            AiMaxTokens: null, WebhookToken: null, AllowPrivateNetworkImport: null,
            NotifyEnabled: true, NotifyOnFailureOnly: true, NotifyPlanResultEmail: null,
            NotifyWecomWebhook: wecom, NotifyDingtalkWebhook: dingtalk, NotifyFeishuWebhook: feishu,
            SmtpHost: null, SmtpPort: null, SmtpUseSsl: null,
            SmtpUser: null, SmtpPassword: null, MailTo: mailTo,
            SsoAutoProvision: null, SsoFrontendBaseUrl: null,
            SsoWecomEnabled: null, SsoWecomCorpId: null, SsoWecomAgentId: null, SsoWecomSecret: null,
            SsoDingtalkEnabled: null, SsoDingtalkClientId: null, SsoDingtalkClientSecret: null,
            SsoOidcEnabled: null, SsoOidcAuthority: null, SsoOidcClientId: null, SsoOidcClientSecret: null,
            SsoOidcDisplayName: null, SsoOidcScopes: null,
            ClearWebhook: null, AgentLoopEnabled: null);

    [Fact]
    public void 合法Webhook通过()
        => Assert.True(_validator.Validate(Request(wecom: "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc")).IsValid);

    [Fact]
    public void 非http协议的Webhook被拒绝()
        => Assert.False(_validator.Validate(Request(feishu: "ftp://example.com/hook")).IsValid);

    [Fact]
    public void 留空表示不修改_校验通过()
        => Assert.True(_validator.Validate(Request()).IsValid);

    [Fact]
    public void 非法收件人被拒绝()
        => Assert.False(_validator.Validate(Request(mailTo: "not-an-email")).IsValid);

    [Fact]
    public void 多收件人逗号分隔通过()
        => Assert.True(_validator.Validate(Request(mailTo: "a@b.com, c@d.com")).IsValid);
}
