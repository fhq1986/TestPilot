using FluentValidation;

namespace AI.TestPlatform.Application.Settings;

public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.AiBaseUrl)
            .Must(v => v is null || v.Length <= 500)
            .WithMessage("AI 接口地址不能超过500个字符")
            .Must(v => v is null || string.IsNullOrEmpty(v) ||
                (Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")))
            .WithMessage("AI 接口地址必须是 http 或 https 的合法 URL");
        RuleFor(x => x.AiModel)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("AI 模型名称不能超过200个字符");
        RuleFor(x => x.AiMaxTokens)
            .Must(v => v is null || v is >= 256 and <= 32768)
            .WithMessage("最大 Token 数必须在256到32768之间");
        RuleFor(x => x.WebhookToken)
            .Must(v => v is null || v.Length <= 500)
            .WithMessage("Webhook Token 不能超过500个字符");

        // ------------------------------ 通知配置
        RuleFor(x => x.NotifyWecomWebhook).Must(BeValidWebhook)
            .WithMessage("企业微信 Webhook 必须是 http/https 的合法 URL，且不超过500个字符");
        RuleFor(x => x.NotifyDingtalkWebhook).Must(BeValidWebhook)
            .WithMessage("钉钉 Webhook 必须是 http/https 的合法 URL，且不超过500个字符");
        RuleFor(x => x.NotifyFeishuWebhook).Must(BeValidWebhook)
            .WithMessage("飞书 Webhook 必须是 http/https 的合法 URL，且不超过500个字符");

        RuleFor(x => x.SmtpHost)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("SMTP 服务器不能超过200个字符");
        RuleFor(x => x.SmtpPort)
            .Must(v => v is null || v is >= 1 and <= 65535)
            .WithMessage("SMTP 端口必须在1到65535之间");
        RuleFor(x => x.SmtpUser)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("SMTP 用户名不能超过200个字符");
        RuleFor(x => x.SmtpPassword)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("SMTP 密码/授权码不能超过200个字符");
        RuleFor(x => x.MailTo)
            .Must(v => v is null || v.Length <= 1000)
            .WithMessage("收件人不能超过1000个字符")
            .Must(BeValidMailList)
            .WithMessage("收件人格式不正确，多个地址请用英文逗号分隔");
    }

    /// <summary>空值表示不修改；非空必须是 http/https 绝对地址</summary>
    private static bool BeValidWebhook(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.Length <= 500 &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool BeValidMailList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var items = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) return true;
        return items.All(i => i.Length <= 200 && i.Contains('@') && !i.Contains(' '));
    }
}
