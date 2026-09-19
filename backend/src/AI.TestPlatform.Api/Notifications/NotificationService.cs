using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Reports;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Notifications;

/// <summary>
/// 执行结果通知：企业微信 / 钉钉 / 飞书 机器人 Webhook + SMTP 邮件。
///
/// 配置来自系统设置（SystemConfig 的通知字段），全部为空即视为关闭。
/// 所有渠道发送失败都只记日志，不影响执行主流程。
/// </summary>
public class NotificationService
{
    /// <summary>验收报告附件的 MIME 类型</summary>
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly TestDbContext _db;
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReportShareLinkService _shareLinks;
    private readonly TestPlanReportService _planReports;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(TestDbContext db, SettingsService settings,
        IHttpClientFactory httpClientFactory,
        ReportShareLinkService shareLinks, TestPlanReportService planReports,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _shareLinks = shareLinks;
        _planReports = planReports;
        _logger = logger;
    }

    /// <summary>执行结束后推送通知（失败只记日志）</summary>
    public async Task NotifyExecutionFinishedAsync(Guid executionId, CancellationToken ct)
    {
        var config = await _settings.GetAsync(ct);
        if (!config.NotifyEnabled) return;

        var execution = await _db.Executions.AsNoTracking()
            .Include(e => e.TestCase)
            .Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution is null) return;
        if (execution.Status is ExecutionStatus.Pending or ExecutionStatus.Running) return;

        // 属于测试计划轮次的执行**不逐条推**：一轮可能有几百条，逐条推会刷屏，
        // 由轮次结束时的一条汇总消息统一交代（见 NotifyPlanRoundFinishedAsync）。
        if (execution.PlanRoundId is not null) return;

        var isProblem = execution.Status is ExecutionStatus.Failed or ExecutionStatus.Error;
        if (config.NotifyOnFailureOnly && !isProblem) return;

        var message = BuildMessage(execution);
        await DispatchAsync(config, message, ct);
    }

    /// <summary>向所有已启用渠道发送一条自定义消息（用于设置页「发送测试消息」）</summary>
    public async Task<ChannelReport> SendTestAsync(string? channel, CancellationToken ct)
    {
        var config = await _settings.GetAsync(ct);
        var message = new NotificationMessage(
            "AI 自动化测试平台 · 通道测试",
            $"""
            **AI 自动化测试平台 通道测试**

            > 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}
            > 说明：收到本条消息说明该通知渠道配置正确。
            """,
            $"【AI 自动化测试平台】通道测试 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，收到本条消息说明该通知渠道配置正确。");

        return await DispatchAsync(config, message, ct, channel);
    }

    // ---------------------------------------------------------------- 消息组装

    /// <summary>
    /// 测试计划轮次完成通知。
    ///
    /// 与逐执行通知的区别：一次轮次可能包含几百条执行，逐条推会刷屏。
    /// 这里只在轮次结束时推**一条汇总**，带上通过率与达标结论——
    /// 这才是「一轮跑完，大家看一条消息就知道结果」的形态。
    /// </summary>
    public async Task NotifyPlanRoundFinishedAsync(Guid planId, Guid roundId, CancellationToken ct)
    {
        var config = await _settings.GetAsync(ct);
        if (!config.NotifyEnabled) return;

        var plan = await _db.TestPlans.AsNoTracking()
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return;

        var outcome = await ComputeOutcomeAsync(plan, roundId, ct);
        if (outcome is null) return;

        // ① 定向验收邮件。
        // 刻意放在「仅失败时通知」判断**之前**：那道闸门是为了"别让报警刷屏"，
        // 而这封邮件是带附件的验收凭证，达标结果恰恰最需要留档发给项目经理与客户。
        // 它的开关是独立的（NotifyPlanResultEmail）+ 是否配了 SMTP / 收件人。
        // 开关与前置条件都放在调用侧：SendPlanResultMailAsync 本身只管"把邮件发出去"，
        // 这样「发送测试邮件」才能在忽略业务开关的前提下复用同一个发送实现
        if (config.NotifyPlanResultEmail && !string.IsNullOrWhiteSpace(config.SmtpHost))
        {
            var recipients = await ResolvePlanRecipientsAsync(config, plan, ct);
            if (recipients.Count == 0)
                _logger.LogInformation(
                    "计划 {PlanId} 没有可用收件人（项目未设测试负责人、计划无负责人，且未配置兜底收件人），跳过验收邮件",
                    plan.Id);
            else
                await SendPlanResultMailAsync(config, plan, outcome, recipients, isTest: false, ct);
        }

        var title = outcome.Met ? "测试计划轮次完成 · 达标" : "测试计划轮次完成 · 未达标";

        // ② 机器人渠道汇总。达标 + 仅失败时通知 → 不打扰。
        if (config.NotifyOnFailureOnly && outcome.Met) return;

        var lines = $"""
            **{title}**
            > 计划：{plan.Name}{(plan.ReleaseName is null ? string.Empty : $"（{plan.ReleaseName}）")}
            > 轮次：第 {outcome.RoundNo} 轮
            > 结果：通过 {outcome.Passed} / 失败 {outcome.Failed} / 错误 {outcome.Error} / 跳过 {outcome.Skipped}
            > 通过率：{outcome.PassRate:P1}（目标 {plan.TargetPassRate:P1}）
            > 结论：{(outcome.Met ? "达标 ✅" : "未达标 ❌")}
            """;
        if (outcome.Excluded > 0)
            lines += $"\n> 说明：已排除 {outcome.Excluded} 条不稳定用例（flaky）的失败样本";

        // 邮件渠道由上面的定向验收邮件独占，这里跳过 mail——
        // 否则同一轮结果会同时从「定向邮件」和「MailTo 广播邮件」出去，收件人收到两封一样的
        await DispatchAsync(config, new NotificationMessage(title, lines, lines), ct, skipMail: true);
    }

    // ---------------------------------------------------------------- 计划验收邮件

    /// <summary>一轮的统计口径（与企微/钉钉汇总、邮件正文共用，避免两个地方各算一遍）</summary>
    private sealed record PlanRoundOutcome(
        int RoundNo, int Passed, int Failed, int Error, int Skipped, int Excluded,
        double PassRate, bool Met);

    /// <summary>验收邮件的发送结果。带上 HasAttachment，是为了让「发送测试邮件」能如实告知
    /// 「报告附件有没有生成出来」——附件缺失时用户会以为功能坏了，其实只是没数据。</summary>
    private sealed record PlanMailResult(bool Ok, string? Error, bool HasAttachment);

    /// <summary>
    /// 按轮次执行记录算出统计口径。企微/钉钉汇总与验收邮件共用这一处，
    /// 免得同一次通知里两处各算一遍、算法还慢慢分叉。
    /// 轮次不存在时返回 null。
    /// </summary>
    private async Task<PlanRoundOutcome?> ComputeOutcomeAsync(
        TestPlan plan, Guid roundId, CancellationToken ct)
    {
        var roundNo = await _db.TestPlanRounds.AsNoTracking()
            .Where(r => r.Id == roundId).Select(r => (int?)r.RoundNo).FirstOrDefaultAsync(ct);
        if (roundNo is null) return null;

        var rows = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId == roundId)
            .Select(e => new
            {
                e.Status,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
            })
            .ToListAsync(ct);

        var passed = rows.Count(r => r.Status == ExecutionStatus.Passed);
        var skipped = rows.Count(r => r.Status == ExecutionStatus.Skipped);
        var flakyFailed = rows.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Failed);
        var flakyError = rows.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Error);

        var excluded = plan.ExcludeFlakyFromFailure ? flakyFailed + flakyError : 0;
        var failed = rows.Count(r => r.Status == ExecutionStatus.Failed)
                     - (plan.ExcludeFlakyFromFailure ? flakyFailed : 0);
        var error = rows.Count(r => r.Status == ExecutionStatus.Error)
                    - (plan.ExcludeFlakyFromFailure ? flakyError : 0);
        var denominator = rows.Count - skipped - excluded;
        var passRate = denominator > 0 ? (double)passed / denominator : 0;
        var met = passRate >= plan.TargetPassRate && (plan.AllowErrors || error == 0);

        return new PlanRoundOutcome(
            roundNo.Value, passed, failed, error, skipped, excluded, passRate, met);
    }

    /// <summary>
    /// 验收结果邮件：HTML 正文 + 在线查看/下载链接 + xlsx 附件。
    /// 收件人由调用方给（真实通知走"负责人"，测试邮件走用户填的地址）。
    /// 内部失败只记日志并返回失败结果，不往外抛（与其它通知渠道一致）。
    /// </summary>
    private async Task<PlanMailResult> SendPlanResultMailAsync(
        SystemConfig config, TestPlan plan, PlanRoundOutcome outcome,
        IReadOnlyList<string> recipients, bool isTest, CancellationToken ct)
    {
        try
        {
            var link = await _shareLinks.EnsurePlanReportLinkAsync(plan, ct);

            byte[]? attachment = null;
            string? attachmentName = null;
            var report = await _planReports.GenerateAsync(plan.Id, ct);
            if (report is not null)
            {
                attachment = report.Value.Content;
                attachmentName = report.Value.FileName;
            }

            var title = BuildPlanMailSubject(plan, outcome, isTest);

            var message = new NotificationMessage(
                title,
                BuildPlanResultText(plan, outcome, link, isTest),
                BuildPlanResultText(plan, outcome, link, isTest),
                Html: BuildPlanResultHtml(plan, outcome, link, isTest),
                Attachment: attachment is null || attachmentName is null
                    ? null
                    : new NotificationAttachment(attachmentName, attachment, XlsxContentType));

            var result = await SendMailAsync(config, recipients, message, ct);
            var hasAttachment = message.Attachment is not null;
            if (result.Ok)
                _logger.LogInformation("计划 {PlanId} 的验收邮件已发送至 {Count} 个收件人（测试={IsTest}，附件={HasAttachment}）",
                    plan.Id, recipients.Count, isTest, hasAttachment);
            else
                _logger.LogWarning("计划 {PlanId} 的验收邮件发送失败：{Error}", plan.Id, result.Error);
            return new PlanMailResult(result.Ok, result.Error, hasAttachment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "计划 {PlanId} 的验收邮件异常", plan.Id);
            return new PlanMailResult(false, ex.Message, false);
        }
    }

    /// <summary>
    /// 发送一封**样例验收邮件**（设置页「发送测试邮件」）。
    ///
    /// 与 <see cref="SendTestAsync"/> 的区别：那个是"通道通不通"的探针——
    /// 一句固定文本、发给 MailTo、纯文本无附件，看不出收件人实际会收到什么；
    /// 这个是**真实形态**：HTML 正文 + 在线查看/下载链接 + xlsx 附件，
    /// 用最近跑过轮次的那个计划的真实数字，因此能在配置阶段就把整条链路看全。
    /// </summary>
    public async Task<TestMailResult> SendPlanResultTestMailAsync(
        string? to, Guid? planId, CancellationToken ct)
    {
        var config = await _settings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(config.SmtpHost))
            return new TestMailResult(false, "尚未配置 SMTP 服务器，请先填写并保存 SMTP 设置",
                Array.Empty<string>(), null, false);

        // 填了就必须是个合法地址：这里**不能**像发信时那样"丢掉非法项再回落"——
        // 测邮件的意义就是确认"这个地址能不能收到"，悄悄发到别人那儿等于把测试做废了
        IReadOnlyList<string> recipients;
        if (!string.IsNullOrWhiteSpace(to))
        {
            recipients = EmailAddress.ParseList(to);
            if (recipients.Count == 0)
                return new TestMailResult(false, $"收件邮箱格式不正确：{to.Trim()}",
                    Array.Empty<string>(), null, false);
        }
        else
        {
            // 没填就退回系统设置里的固定收件人，省得每次都手打
            recipients = EmailAddress.ParseList(config.MailTo);
            if (recipients.Count == 0)
                return new TestMailResult(false, "请填写收件邮箱（系统设置里的收件人为空）",
                    Array.Empty<string>(), null, false);
        }

        TestPlan? plan;
        if (planId is not null)
        {
            plan = await _db.TestPlans.AsNoTracking().Include(p => p.Owner)
                .FirstOrDefaultAsync(p => p.Id == planId.Value, ct);
            if (plan is null)
                return new TestMailResult(false, "指定的测试计划不存在", recipients, null, false);
        }
        else
        {
            // 挑一个最近有轮次的计划：没有轮次就没有真实数字，也生成不出像样的报告附件
            plan = await _db.TestPlans.AsNoTracking().Include(p => p.Owner)
                .Where(p => p.Rounds.Any())
                .OrderByDescending(p => p.LastRoundAt ?? p.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (plan is null)
                return new TestMailResult(false,
                    "还没有执行过任何测试计划，无法生成样例邮件；请先在某个计划上跑一轮",
                    recipients, null, false);
        }

        var latestRoundId = await _db.TestPlanRounds.AsNoTracking()
            .Where(r => r.PlanId == plan.Id)
            .OrderByDescending(r => r.RoundNo)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (latestRoundId is null)
            return new TestMailResult(false, $"计划「{plan.Name}」还没有轮次记录，无法生成样例邮件",
                recipients, null, false);

        var outcome = await ComputeOutcomeAsync(plan, latestRoundId.Value, ct);
        if (outcome is null)
            return new TestMailResult(false, "轮次数据读取失败", recipients, null, false);

        var result = await SendPlanResultMailAsync(config, plan, outcome, recipients, isTest: true, ct);
        var subject = BuildPlanMailSubject(plan, outcome, isTest: true);

        return result.Ok
            ? new TestMailResult(true,
                $"测试邮件已发送至 {string.Join("、", recipients)}"
                + $"（使用计划「{plan.Name}」第 {outcome.RoundNo} 轮的真实数据"
                + (result.HasAttachment ? "，含报告附件）" : "，⚠ 未生成报告附件）"),
                recipients, subject, result.HasAttachment)
            : new TestMailResult(false, $"发送失败：{result.Error}", recipients, subject, false);
    }

    /// <summary>
    /// 收件人 = 项目的测试负责人 + 计划的负责人（去重，非法邮箱直接丢弃）。
    ///
    /// **已停用的用户不投递**：停用意味着这个账号已经退出流程（多是离职或转岗），
    /// 继续往他的邮箱发验收报告既没意义也容易出事。
    ///
    /// 两个人都可能没填邮箱（或都已停用），因此都取不到时**回落到系统设置里的固定收件人**——
    /// 否则一次人事变动就会让验收邮件彻底静默，而"静默失效"是最难被发现的故障。
    /// 判定用的是与用户表单同一个 <see cref="EmailAddress.IsValid"/>，
    /// 避免出现「表单说合法、发信被丢掉」这种查起来最费劲的不一致。
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolvePlanRecipientsAsync(
        SystemConfig config, TestPlan plan, CancellationToken ct)
    {
        var emails = new List<string>();

        var projectTestOwnerEmail = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == plan.ProjectId)
            .Select(p => p.TestOwner != null && p.TestOwner.IsActive ? p.TestOwner.Email : null)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(projectTestOwnerEmail)) emails.Add(projectTestOwnerEmail);

        if (plan.OwnerId is not null)
        {
            var planOwnerEmail = await _db.Users.AsNoTracking()
                .Where(u => u.Id == plan.OwnerId.Value && u.IsActive)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(planOwnerEmail)) emails.Add(planOwnerEmail);
        }

        var resolved = emails
            .Where(EmailAddress.IsValid)
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return resolved.Count > 0 ? resolved : EmailAddress.ParseList(config.MailTo);
    }

    /// <summary>
    /// 邮件主题。抽出来是为了让「发送测试邮件」的响应能回带**与实际发出的完全一致**的主题——
    /// 两边各拼一次，迟早会出现"接口说 A、收件箱里是 B"这种没法解释的差异。
    /// 测试邮件在主题里就标明，避免收件人以为真有一次验收跑完了。
    /// </summary>
    private static string BuildPlanMailSubject(TestPlan plan, PlanRoundOutcome o, bool isTest) =>
        $"{(isTest ? "【测试邮件】" : string.Empty)}测试计划轮次完成 · {(o.Met ? "达标" : "未达标")}"
        + $"｜{plan.Name}{(plan.ReleaseName is null ? string.Empty : $"（{plan.ReleaseName}）")}";

    /// <summary>纯文本版（邮件客户端降级显示 / 飞书文本渠道复用）</summary>
    private static string BuildPlanResultText(TestPlan plan, PlanRoundOutcome o, ReportShareLink? link,
        bool isTest)
    {
        var sb = new StringBuilder();
        if (isTest)
        {
            sb.AppendLine("【这是一封测试邮件】设置页点击「发送测试邮件」发出，");
            sb.AppendLine("用来说明真实验收邮件的形态；下面的数字取自该计划的最近一轮，不是新跑的结果。");
            sb.AppendLine();
        }
        sb.AppendLine($"测试计划轮次完成 · {(o.Met ? "达标" : "未达标")}");
        sb.AppendLine($"计划：{plan.Name}{(plan.ReleaseName is null ? string.Empty : $"（{plan.ReleaseName}）")}");
        sb.AppendLine($"轮次：第 {o.RoundNo} 轮");
        sb.AppendLine($"结果：通过 {o.Passed} / 失败 {o.Failed} / 错误 {o.Error} / 跳过 {o.Skipped}");
        sb.AppendLine($"通过率：{o.PassRate:P1}（目标 {plan.TargetPassRate:P1}）");
        sb.AppendLine($"结论：{(o.Met ? "达标" : "未达标")}");
        if (o.Excluded > 0)
            sb.AppendLine($"说明：已排除 {o.Excluded} 条不稳定用例（flaky）的失败样本");
        if (link is not null)
        {
            sb.AppendLine($"在线查看：{link.OnlineUrl}");
            sb.AppendLine($"下载报告：{link.DownloadUrl}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// HTML 版正文。样式全部内联——多数邮件客户端会剥掉 &lt;style&gt; 与 class，
    /// 用外链样式表的邮件在 Outlook / 企业微信邮箱里会直接变成一坨纯文本。
    /// </summary>
    private static string BuildPlanResultHtml(TestPlan plan, PlanRoundOutcome o, ReportShareLink? link,
        bool isTest)
    {
        var accent = o.Met ? "#16a34a" : "#dc2626";
        var verdict = o.Met ? "达标" : "未达标";
        var planName = WebUtility.HtmlEncode(plan.Name);
        var release = plan.ReleaseName is null ? null : WebUtility.HtmlEncode(plan.ReleaseName);

        string Row(string label, string value) =>
            $"""
             <tr>
               <td style="padding:6px 0;color:#6b7280;font-size:13px;width:96px;vertical-align:top">{label}</td>
               <td style="padding:6px 0;color:#111827;font-size:13px">{value}</td>
             </tr>
             """;

        var sb = new StringBuilder();
        sb.Append("""
            <div style="margin:0;padding:24px 12px;background:#f3f4f6">
              <div style="max-width:640px;margin:0 auto;background:#ffffff;border-radius:8px;overflow:hidden;
                          font-family:-apple-system,BlinkMacSystemFont,'Segoe UI','Microsoft YaHei',sans-serif">
            """);
        sb.Append($"""
              <div style="background:{accent};padding:18px 24px;color:#ffffff">
                <div style="font-size:12px;opacity:.85">AI 自动化测试平台 · 测试计划验收</div>
                <div style="font-size:20px;font-weight:600;margin-top:4px">{verdict}</div>
              </div>
            """);
        // 测试邮件的醒目标识：正文里的数字虽然是真的（取自最近一轮），
        // 但收件人很容易误以为"刚刚又跑了一轮"，必须一开始就说清楚
        if (isTest)
            sb.Append("""
              <div style="padding:10px 24px;background:#fef3c7;color:#92400e;font-size:12px;line-height:1.6">
                <b>这是一封测试邮件</b>——由系统设置页的「发送测试邮件」发出，用于确认邮件配置与报告形态。
                下面的数据取自该测试计划的<b>最近一轮</b>执行，并非刚刚新跑的结果。
              </div>
            """);
        sb.Append($"""
              <div style="padding:20px 24px">
                <table style="width:100%;border-collapse:collapse">
            """);
        sb.Append(Row("计划", planName + (release is null ? string.Empty : $"（{release}）")));
        sb.Append(Row("轮次", $"第 {o.RoundNo} 轮"));
        sb.Append(Row("结果",
            $"通过 <b style=\"color:#16a34a\">{o.Passed}</b> · "
            + $"失败 <b style=\"color:#dc2626\">{o.Failed}</b> · "
            + $"错误 <b style=\"color:#dc2626\">{o.Error}</b> · 跳过 {o.Skipped}"));
        sb.Append(Row("通过率",
            $"<b style=\"color:{accent}\">{o.PassRate:P1}</b> "
            + $"<span style=\"color:#6b7280\">（目标 {plan.TargetPassRate:P1}）</span>"));
        sb.Append(Row("结论", $"<b style=\"color:{accent}\">{verdict}</b>"));
        if (o.Excluded > 0)
            sb.Append(Row("说明", $"已排除 {o.Excluded} 条不稳定用例（flaky）的失败样本"));
        if (plan.EndsAt is not null)
            sb.Append(Row("计划截止", plan.EndsAt.Value.ToString("yyyy-MM-dd")));
        sb.Append("</table>");

        if (link is not null)
        {
            var online = WebUtility.HtmlEncode(link.OnlineUrl);
            var download = WebUtility.HtmlEncode(link.DownloadUrl);
            sb.Append($"""
                    <div style="margin-top:22px">
                      <a href="{online}" style="display:inline-block;padding:10px 20px;background:#2563eb;color:#ffffff;
                         text-decoration:none;border-radius:6px;font-size:14px;margin-right:10px">在线查看报告</a>
                      <a href="{download}" style="display:inline-block;padding:10px 20px;background:#ffffff;color:#2563eb;
                         text-decoration:none;border-radius:6px;font-size:14px;border:1px solid #2563eb">下载报告 (xlsx)</a>
                    </div>
                    <div style="margin-top:12px;color:#9ca3af;font-size:12px;line-height:1.6">
                      报告文件的 Excel 版本已作为附件随本邮件发送；<br />
                      若链接打不开，请复制到浏览器访问：{online}
                    </div>
                """);
        }
        else
        {
            sb.Append("""
                    <div style="margin-top:18px;padding:12px;background:#fef3c7;border-radius:6px;
                                color:#92400e;font-size:12px">
                      本次未能生成在线报告链接，完整报告见附件。
                    </div>
                """);
        }

        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        sb.Append($"""
                <div style="margin-top:20px;padding-top:14px;border-top:1px solid #e5e7eb;color:#9ca3af;font-size:12px">
                  本邮件由系统在测试计划轮次结束时自动发送 ｜ {generatedAt}
                </div>
              </div>
            </div></div>
            """);
        return sb.ToString();
    }

    private static NotificationMessage BuildMessage(global::AI.TestPlatform.Domain.Entities.Execution execution)
    {
        var name = execution.TestCase?.Name ?? "(用例已删除)";
        var status = execution.Status switch
        {
            ExecutionStatus.Passed => "通过",
            ExecutionStatus.Failed => "失败",
            ExecutionStatus.Error => "错误",
            ExecutionStatus.Skipped => "跳过",
            _ => execution.Status.ToString(),
        };
        var badge = execution.Status switch
        {
            ExecutionStatus.Passed => "✅",
            ExecutionStatus.Failed => "❌",
            ExecutionStatus.Error => "⚠️",
            _ => "ℹ️",
        };

        var failed = execution.Results
            .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .OrderBy(r => r.StepOrder)
            .ToList();
        var passed = execution.Results.Count(r => r.Status == ExecutionStatus.Passed);

        var env = execution.EnvironmentSnapshot?.Name ?? "未指定环境";
        var duration = execution.DurationMs is null ? "-" : $"{execution.DurationMs / 1000.0:F1}s";
        var source = string.IsNullOrWhiteSpace(execution.TriggerSource) ? "手动" : execution.TriggerSource;
        var ciExtra = string.IsNullOrWhiteSpace(execution.CommitSha)
            ? string.Empty
            : $"\n> 提交：`{Short(execution.CommitSha)}`{(string.IsNullOrWhiteSpace(execution.Branch) ? "" : $"（{execution.Branch}）")}";

        var title = $"{badge} 测试执行{status}：{name}";

        var lines = new List<string>
        {
            $"**{title}**",
            string.Empty,
            $"> 用例：{name}",
            $"> 状态：**{status}**",
            $"> 环境：{env}",
            $"> 来源：{source}{ciExtra}",
            $"> 结果：通过 {passed} / 失败 {failed.Count}｜耗时 {duration}",
        };

        if (failed.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("**失败步骤**");
            foreach (var r in failed.Take(5))
            {
                var reason = r.ErrorMessage is null
                    ? "无错误信息"
                    : r.ErrorMessage.Length > 120 ? r.ErrorMessage[..120] + "…" : r.ErrorMessage;
                lines.Add($"- 步骤 {r.StepOrder + 1}：{reason}");
            }
            if (failed.Count > 5) lines.Add($"- …… 另有 {failed.Count - 5} 个失败步骤");
        }

        if (!string.IsNullOrWhiteSpace(execution.AIDiagnosis))
        {
            lines.Add(string.Empty);
            lines.Add($"**AI 诊断**：{execution.AIDiagnosis}");
        }

        var markdown = string.Join('\n', lines);
        // 飞书纯文本机器人不接受 Markdown，压成单段纯文本
        var plain = new StringBuilder()
            .AppendLine($"{badge} 测试执行{status}：{name}")
            .AppendLine($"状态：{status}｜环境：{env}｜来源：{source}")
            .AppendLine($"结果：通过 {passed} / 失败 {failed.Count}｜耗时 {duration}")
            .ToString()
            .TrimEnd();

        return new NotificationMessage(title, markdown, plain);
    }

    private static string Short(string sha) => sha.Length <= 8 ? sha : sha[..8];

    // ---------------------------------------------------------------- 渠道分发

    private async Task<ChannelReport> DispatchAsync(SystemConfig config,
        NotificationMessage message, CancellationToken ct,
        string? only = null, bool skipMail = false)
    {
        var results = new List<ChannelResult>();

        bool ShouldRun(string key) => only is null || string.Equals(only, key, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.NotifyWecomWebhook) && ShouldRun("wecom"))
            results.Add(await SendWecomAsync(config.NotifyWecomWebhook, message, ct));

        if (!string.IsNullOrWhiteSpace(config.NotifyDingtalkWebhook) && ShouldRun("dingtalk"))
            results.Add(await SendDingtalkAsync(config.NotifyDingtalkWebhook, message, ct));

        if (!string.IsNullOrWhiteSpace(config.NotifyFeishuWebhook) && ShouldRun("feishu"))
            results.Add(await SendFeishuAsync(config.NotifyFeishuWebhook, message, ct));

        // SMTP 服务器为空视为未启用邮件渠道（否则会报出没有意义的「未配置 SMTP」失败项）
        if (!skipMail && !string.IsNullOrWhiteSpace(config.SmtpHost) && ShouldRun("mail"))
            results.Add(await SendMailAsync(config, EmailAddress.ParseList(config.MailTo), message, ct));

        foreach (var r in results.Where(r => !r.Ok))
            _logger.LogWarning("通知渠道 {Channel} 发送失败：{Error}", r.Channel, r.Error);

        return new ChannelReport(results);
    }

    private async Task<ChannelResult> PostJsonAsync(string channel, string url, object payload, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new ChannelResult(channel, false, "Webhook 地址不是合法的 http/https URL");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(uri, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new ChannelResult(channel, false, $"HTTP {(int)response.StatusCode}：{Trim(body)}");
            // 三个平台的机器人都是「HTTP 200 + 业务错误码」的约定，需要再看一层
            var businessError = ExtractBusinessError(body);
            return businessError is null
                ? new ChannelResult(channel, true, null)
                : new ChannelResult(channel, false, businessError);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ChannelResult(channel, false, ex.Message);
        }
    }

    private static string? ExtractBusinessError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var key in new[] { "errcode", "code", "StatusCode", "status_code" })
            {
                if (root.TryGetProperty(key, out var node) &&
                    node.ValueKind == JsonValueKind.Number && node.GetInt32() != 0)
                    return $"{key}={node.GetInt32()}：{Trim(body)}";
            }
            return null;
        }
        catch (JsonException)
        {
            // 非 JSON 响应只能按 HTTP 状态判断
            return null;
        }
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private Task<ChannelResult> SendWecomAsync(string url, NotificationMessage message, CancellationToken ct)
        => PostJsonAsync("企业微信", url, new
        {
            msgtype = "markdown",
            markdown = new { content = message.Markdown },
        }, ct);

    private Task<ChannelResult> SendDingtalkAsync(string url, NotificationMessage message, CancellationToken ct)
        => PostJsonAsync("钉钉", url, new
        {
            msgtype = "markdown",
            markdown = new { title = message.Title, text = message.Markdown },
        }, ct);

    private Task<ChannelResult> SendFeishuAsync(string url, NotificationMessage message, CancellationToken ct)
        => PostJsonAsync("飞书", url, new
        {
            msg_type = "text",
            content = new { text = message.Plain },
        }, ct);

    /// <summary>
    /// 发信。收件人由调用方给定而不是固定读 <c>MailTo</c>：
    /// 计划验收邮件要发给具体的负责人，而执行通知是广播给固定的组邮箱，两者收件人来源不同。
    /// </summary>
    /// <summary>
    /// 评审事件通知（提交评审 / 批准 / 驳回）。收件人由调用方决定；失败只记日志。
    /// </summary>
    public async Task NotifyReviewEventAsync(IReadOnlyList<string> recipients,
        string title, string body)
    {
        try
        {
            var config = await _settings.GetAsync(ct: default);
            if (string.IsNullOrWhiteSpace(config.SmtpHost)) return;
            var message = new NotificationMessage(Title: title, Markdown: body, Plain: body);
            var result = await SendMailAsync(config, recipients, message, CancellationToken.None);
            _logger.LogInformation("评审事件通知：{Title}，收件人 {Recipients}，结果 {Ok} {Error}",
                title, recipients.Count, result.Ok, result.Error ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "评审事件通知发送失败：{Title}", title);
        }
    }

    /// <summary>
    /// 评论 @提及提醒：给被提及（Username 或 DisplayName 匹配 @名字）、且配置了邮箱的用户发邮件。
    /// 作者本人被 @ 不发（自己写的自己收到是噪声）。无 SMTP 配置 / 无匹配收件人时静默返回。
    /// 由评论创建端点 fire-and-forget 调用：邮件失败只记日志，绝不影响评论本身。
    /// </summary>
    public async Task NotifyCommentMentionsAsync(Guid commentId, CancellationToken ct)
    {
        try
        {
            var comment = await _db.Comments.AsNoTracking()
                .Include(c => c.Author)
                .FirstOrDefaultAsync(c => c.Id == commentId, ct);
            if (comment is null) return;

            var mentioned = System.Text.RegularExpressions.Regex
                .Matches(comment.Body, @"@([\w\u4e00-\u9fa5.-]+)")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            if (mentioned.Count == 0) return;

            var config = await _settings.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(config.SmtpHost)) return;

            var users = await _db.Users.AsNoTracking()
                .Where(u => mentioned.Contains(u.Username) || mentioned.Contains(u.DisplayName))
                .Select(u => new { u.Id, u.Username, u.DisplayName, u.Email })
                .ToListAsync(ct);
            var recipients = users
                .Where(u => u.Id != comment.AuthorId && !string.IsNullOrWhiteSpace(u.Email))
                .Select(u => u.Email!).Distinct().ToList();
            if (recipients.Count == 0) return;

            var (targetLabel, targetTitle) = await ResolveCommentTargetAsync(comment.Target, comment.TargetId, ct);
            if (targetLabel is null) return;

            var title = $"【AI 测试平台】{comment.Author.DisplayName} 在{targetLabel}「{targetTitle}」的评论中提到了你";
            var message = new NotificationMessage(
                Title: title,
                Markdown: $"{comment.Author.DisplayName} 在{targetLabel}「**{targetTitle}**」的评论中提到了你：\n\n> {comment.Body}\n\n请登录 AI 测试平台查看完整讨论。",
                Plain: $"{comment.Author.DisplayName} 在{targetLabel}「{targetTitle}」的评论中提到了你：{comment.Body}（请登录平台查看）");

            var result = await SendMailAsync(config, recipients, message, ct);
            _logger.LogInformation("评论 @提及提醒：{Mentioned} 人，发送给 {Recipients}，结果 {Ok} {Error}",
                mentioned.Count, recipients.Count, result.Ok, result.Error ?? "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "评论 @提及提醒发送失败 评论 {CommentId}", commentId);
        }
    }

    /// <summary>解析评论挂载对象的展示名（用例名/缺陷标题/计划名）。对象不存在返回 (null, null)。</summary>
    private async Task<(string? Label, string? Title)> ResolveCommentTargetAsync(
        CommentTarget target, Guid targetId, CancellationToken ct)
    {
        string? label, title;
        switch (target)
        {
            case CommentTarget.TestCase:
                label = "用例";
                title = await _db.TestCases.AsNoTracking()
                    .Where(t => t.Id == targetId).Select(t => t.Name).FirstOrDefaultAsync(ct);
                break;
            case CommentTarget.Defect:
                label = "缺陷";
                title = await _db.Defects.AsNoTracking()
                    .Where(d => d.Id == targetId).Select(d => d.Title).FirstOrDefaultAsync(ct);
                break;
            case CommentTarget.TestPlan:
                label = "测试计划";
                title = await _db.TestPlans.AsNoTracking()
                    .Where(p => p.Id == targetId).Select(p => p.Name).FirstOrDefaultAsync(ct);
                break;
            default:
                return (null, null);
        }
        return title is null ? (null, null) : (label, title);
    }

    private async Task<ChannelResult> SendMailAsync(SystemConfig config,
        IReadOnlyList<string> recipients, NotificationMessage message, CancellationToken ct)
    {
        const string channel = "邮件";
        if (string.IsNullOrWhiteSpace(config.SmtpHost))
            return new ChannelResult(channel, false, "未配置 SMTP 服务器");
        if (recipients.Count == 0)
            return new ChannelResult(channel, false, "未配置收件人");

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(string.IsNullOrWhiteSpace(config.SmtpUser)
                    ? "ai-test-platform@localhost" : config.SmtpUser),
                Subject = message.Title,
                // 有 HTML 就用 HTML（验收邮件是图文带按钮的），否则退回纯文本
                Body = message.Html ?? message.Markdown.Replace("**", string.Empty).Replace("> ", string.Empty),
                IsBodyHtml = message.Html is not null,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
            };
            foreach (var to in recipients) mail.To.Add(to);

            if (message.Attachment is { } attachment)
            {
                // MailMessage 释放时会连带释放附件的流，这里不必单独持有
                mail.Attachments.Add(new Attachment(
                    new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType));
            }

            using var smtp = new SmtpClient(config.SmtpHost, config.SmtpPort)
            {
                EnableSsl = config.SmtpUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000,
            };
            if (!string.IsNullOrWhiteSpace(config.SmtpUser))
                smtp.Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPassword);

            await smtp.SendMailAsync(mail, ct);
            return new ChannelResult(channel, true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ChannelResult(channel, false, ex.Message);
        }
    }
}

/// <summary>邮件附件。内容直接放在内存里：验收报告是几十 KB 级的 xlsx，不值得落临时文件</summary>
public record NotificationAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>「发送测试邮件」的结果。把收件人与是否带附件一并回传，让设置页能如实展示发生了什么</summary>
public record TestMailResult(
    bool Ok, string Message,
    IReadOnlyList<string> Recipients, string? Subject, bool HasAttachment);

/// <summary>
/// 一条待发送的消息。
/// <paramref name="Html"/> 为空时邮件走纯文本（机器人渠道一直用 Markdown / Plain），
/// 这样加 HTML 支持不会影响既有的执行通知。
/// </summary>
public record NotificationMessage(
    string Title, string Markdown, string Plain,
    string? Html = null, NotificationAttachment? Attachment = null);

public record ChannelResult(string Channel, bool Ok, string? Error);

public record ChannelReport(IReadOnlyList<ChannelResult> Channels)
{
    public bool AnyConfigured => Channels.Count > 0;
    public int Succeeded => Channels.Count(c => c.Ok);
}
