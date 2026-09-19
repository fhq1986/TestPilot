using System.Security.Cryptography;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Reports;

/// <summary>
/// 报告分享令牌与链接的生成。
///
/// 存在的理由：令牌生成、前端地址拼接、"取或建"这套逻辑原先散在
/// <c>ShareApiExtensions</c> 里，而发验收邮件的通知流程**也需要同一套东西**。
/// 与其让通知流程去复制一份，不如收成一个服务，两边共用——
/// 否则「页面点分享」和「邮件里的链接」早晚会在有效期与令牌格式上分叉。
/// </summary>
public class ReportShareLinkService
{
    /// <summary>
    /// 邮件里给出的链接有效期（天）。
    /// 比页面手点分享的默认 7 天更长：邮件常常在企业邮箱里躺上几周才被点开，
    /// 「点开发现过期」比「根本没给链接」更让人恼火。
    /// </summary>
    public const int EmailedLinkExpireDays = 30;

    private readonly TestDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportShareLinkService> _logger;

    public ReportShareLinkService(TestDbContext db, IConfiguration configuration,
        ILogger<ReportShareLinkService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>URL 安全随机令牌（32 字节 → 43 字符 base64url）</summary>
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>前端地址：分享链接指向前端 SPA 的 /share/{token} 路由</summary>
    public static string FrontendBase(IConfiguration configuration)
    {
        var origin = (configuration["AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(origin) ? "http://localhost:3000" : origin.TrimEnd('/');
    }

    /// <summary>拼出某个令牌的对外可访问链接（前端 SPA 的 /share 页面）</summary>
    public static string BuildLink(IConfiguration configuration, string token) =>
        $"{FrontendBase(configuration)}/share/{token}";

    /// <summary>
    /// 对外可访问的 API 地址。默认「前端地址 + /api」——开发环境的 Vite 代理与生产 nginx
    /// 都是这个约定；若实际部署把 API 放在别的域名或路径下，用 <c>PublicApiBaseUrl</c> 覆盖。
    /// </summary>
    private string ApiBase
    {
        get
        {
            var configured = _configuration["PublicApiBaseUrl"];
            return string.IsNullOrWhiteSpace(configured)
                ? $"{FrontendBase(_configuration)}/api"
                : configured.TrimEnd('/');
        }
    }

    /// <summary>把令牌拼成「在线查看 / 直接下载」一对链接，供邮件正文使用</summary>
    public ReportShareLink LinksOf(string token) => new(
        token,
        BuildLink(_configuration, token),
        $"{ApiBase}/public/reports/{token}/export");

    /// <summary>
    /// 取该计划「当前可用」的验收报告分享链接；没有就建一个。
    ///
    /// 刻意**复用**而不是每轮新建：一个计划每晚跑一轮，几周下来分享列表会被同一个计划的
    /// 几十个令牌刷满；而且同一份验收报告的链接本就该稳定——它会在邮件里、群里被反复引用。
    /// 过期后下一轮自然会建新的，等于顺带完成了续期。
    /// </summary>
    public async Task<ReportShareLink?> EnsurePlanReportLinkAsync(TestPlan plan, CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            var token = await _db.ReportShares.AsNoTracking()
                .Where(s => s.Kind == ReportShareKind.TestPlan
                            && s.RefId == plan.Id
                            && !s.Revoked
                            && (s.ExpiresAt == null || s.ExpiresAt > now))
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.Token)
                .FirstOrDefaultAsync(ct);

            if (token is null)
            {
                var share = new ReportShare
                {
                    Token = NewToken(),
                    Kind = ReportShareKind.TestPlan,
                    RefId = plan.Id,
                    ProjectId = plan.ProjectId,
                    Title = plan.ReleaseName is null
                        ? $"{plan.Name} · 验收报告"
                        : $"{plan.Name}（{plan.ReleaseName}）· 验收报告",
                    ExpiresAt = now.AddDays(EmailedLinkExpireDays),
                };
                _db.ReportShares.Add(share);
                await _db.SaveChangesAsync(ct);
                token = share.Token;
            }

            return LinksOf(token);
        }
        catch (Exception ex)
        {
            // 链接建不出来不该阻断发信：正文里少一个链接，总比整封报告都发不出去好
            _logger.LogWarning(ex, "生成计划 {PlanId} 的分享链接失败", plan.Id);
            return null;
        }
    }
}

/// <summary>一份分享令牌对应的两个入口：在线看 / 直接下</summary>
public record ReportShareLink(string Token, string OnlineUrl, string DownloadUrl);
