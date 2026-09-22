using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.Settings;
using AI.TestPlatform.Domain.Entities;
using FluentValidation;

namespace AI.TestPlatform.Api.Modules.Settings;

// 系统配置端点（/api/settings）：GET/PUT 单行配置（脱敏），POST /ai/test 测试 AI 连接
public static class SettingsApiExtensions
{
    public static RouteGroupBuilder MapSettingsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            SettingsService settings,
            CancellationToken ct) =>
        {
            var config = await settings.GetAsync(ct);
            return Results.Ok(settings.ToView(config));
        }).WithPermission(Permission.ManageSettings).Produces<SettingsView>();

        group.MapPut("/", async (
            UpdateSettingsRequest request,
            IValidator<UpdateSettingsRequest> validator,
            SettingsService settings,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var view = await settings.UpdateAsync(request, ct);
            return Results.Ok(view);
        }).WithPermission(Permission.ManageSettings).WithAudit("Update", "Settings");

        // AI 提供商预设（Base URL + 常用模型），供前端联动选择
        group.MapGet("/ai-providers", () => Results.Ok(AIProviderCatalog.Providers))
            .WithPermission(Permission.ManageSettings).Produces<IReadOnlyList<AIProviderPreset>>();

        group.MapPost("/ai/test", async (
            AIClient aiClient,
            CancellationToken ct) =>
        {
            var result = await aiClient.PingAsync(ct);
            return result.Ok ? Results.Ok(result) : Results.Json(result, statusCode: 502);
        }).WithPermission(Permission.ManageSettings).WithAudit("TestAI", "Settings");

        // 通知渠道连通性测试：向已配置渠道发一条测试消息
        group.MapPost("/notify/test", async (
            NotifyTestRequest? request,
            NotificationService notifier,
            CancellationToken ct) =>
        {
            var report = await notifier.SendTestAsync(request?.Channel, ct);
            if (!report.AnyConfigured)
                return Results.BadRequest(new NotifyTestResult(false, Array.Empty<NotifyTestChannelResult>(),
                    "尚未配置任何通知渠道，请先填写机器人 Webhook 或 SMTP 服务器"));

            var channels = report.Channels
                .Select(c => new NotifyTestChannelResult(c.Channel, c.Ok, c.Error))
                .ToList();
            var failures = channels.Where(c => !c.Ok).ToList();
            var message = failures.Count == 0
                ? $"已向 {channels.Count} 个渠道发送测试消息"
                : $"成功 {channels.Count - failures.Count} 个，失败 {failures.Count} 个："
                  + string.Join("；", failures.Select(f => $"{f.Channel}（{f.Error}）"));

            var result = new NotifyTestResult(failures.Count == 0, channels, message);
            return failures.Count == 0 ? Results.Ok(result) : Results.Json(result, statusCode: 502);
        }).WithPermission(Permission.ManageSettings).WithAudit("TestNotify", "Settings");

        // 发送测试邮件：与上面的「通道连通性测试」不同，这里发的是**真实形态**的验收邮件
        // （HTML 正文 + 在线查看/下载链接 + xlsx 附件），用来确认收件人实际收到长什么样。
        group.MapPost("/mail/test", async (
            TestMailRequest? request,
            NotificationService notifier,
            CancellationToken ct) =>
        {
            var result = await notifier.SendPlanResultTestMailAsync(request?.To, request?.PlanId, ct);
            // 发送失败是"渠道/配置"问题而非请求非法，用 502 与上面的渠道测试保持一致
            return result.Ok ? Results.Ok(result) : Results.Json(result, statusCode: 502);
        }).WithPermission(Permission.ManageSettings).WithAudit("TestMail", "Settings");

        return group;
    }
}
