using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Api.Reports;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.PublicReports;

/// <summary>
/// 免登录只读报告（/api/public/reports/{token}）：
/// 明确不做鉴权（分享链接即凭证），因此只暴露报告所需字段，且受「有效期 + 可吊销」双重约束。
/// </summary>
public static class PublicReportApiExtensions
{
    public static RouteGroupBuilder MapPublicReportApi(this RouteGroupBuilder group)
    {
        group.MapGet("/reports/{token}", async (
            string token,
            TestDbContext db,
            ReportAggregator aggregator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
                return Results.NotFound(new { message = "报告链接无效" });

            var share = await db.ReportShares.FirstOrDefaultAsync(s => s.Token == token, ct);
            if (share is null || share.Revoked)
                return Results.NotFound(new { message = "报告链接已失效" });
            if (share.ExpiresAt.HasValue && share.ExpiresAt.Value <= DateTime.UtcNow)
                return Results.Json(new { message = "报告链接已过期" }, statusCode: StatusCodes.Status410Gone);

            // 访问计数（尽力而为，失败不影响报告返回）
            share.ViewCount++;
            share.LastViewedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
            }

            var report = share.Kind switch
            {
                ReportShareKind.Execution => await aggregator.ForExecutionAsync(share.RefId, share.ExpiresAt, ct),
                ReportShareKind.SuiteRun => await aggregator.ForSuiteRunAsync(share.RefId, share.ExpiresAt, ct),
                ReportShareKind.Project => await aggregator.ForProjectAsync(
                    share.RefId, share.From, share.To, share.ExpiresAt, ct),
                ReportShareKind.TestPlan => await aggregator.ForTestPlanAsync(
                    share.RefId, share.ExpiresAt, ct),
                _ => null,
            };
            if (report is null) return Results.NotFound(new { message = "不支持的分享类型" });

            // 分享时自定义的标题覆盖聚合出来的默认标题
            return Results.Ok(string.IsNullOrWhiteSpace(share.Title)
                ? report
                : report with { Title = share.Title });
        });

        // 免登录下载 xlsx。
        // 与上面的 JSON 接口**共用同一套令牌校验**：既然链接本身就是凭证，
        // 「能在线上看」和「能下载」就不该是两种权限——校验逻辑抄第二遍才是真正的风险。
        group.MapGet("/reports/{token}/export", async (
            string token,
            HttpContext http,
            TestDbContext db,
            TestCaseReportService caseReports,
            TestPlanReportService planReports,
            CancellationToken ct) =>
        {
            var (share, error) = await ResolveShareAsync(token, db, ct);
            if (error is not null) return error;

            (byte[] Content, string FileName)? file = null;
            switch (share!.Kind)
            {
                case ReportShareKind.TestPlan:
                    // 计划验收报告（达标判定 / 轮次趋势 / 模块通过率 / 阻断用例）
                    file = await planReports.GenerateAsync(share.RefId, ct);
                    break;

                case ReportShareKind.Project:
                    file = await AsFile(() => caseReports.GenerateForProjectAsync(
                        share.RefId, share.From, share.To, null, ct));
                    break;

                case ReportShareKind.Execution:
                    file = await AsFile(() => caseReports.GenerateForExecutionAsync(share.RefId, ct));
                    break;

                case ReportShareKind.SuiteRun:
                {
                    // 套件运行没有独立的报告生成器：它是一批执行记录，
                    // 复用「批量导出」那条路径，聚合出来就是一套运行报告
                    var ids = await db.Executions.AsNoTracking()
                        .Where(e => e.SuiteRunId == share.RefId)
                        .OrderBy(e => e.CreatedAt)
                        .Select(e => e.Id)
                        .ToListAsync(ct);
                    if (ids.Count > 0)
                        file = await AsFile(() => caseReports.GenerateForExecutionsAsync(ids, ct));
                    break;
                }
            }

            if (file is null)
                return Results.NotFound(new { message = "该分享没有可下载的报告文件" });

            // 让企业代理/浏览器短时缓存挡一层重复生成（安全审查 S3）；
            // private：报告内容按令牌区分，绝不能进共享缓存
            http.Response.Headers.CacheControl = "private, max-age=60";
            return Results.File(file.Value.Content, XlsxContentType, file.Value.FileName);
        })
        // 独立限流（安全审查 S3）：生成整份 xlsx 是高成本操作，5 次/分钟/IP
        .RequireRateLimiting("public-export");

        return group;
    }

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// 令牌校验（JSON 与下载两个端点共用）。
    /// 失效语义与公开 JSON 接口保持一致：**吊销 → 404**（不泄露令牌曾经存在）、**过期 → 410**。
    /// </summary>
    private static async Task<(ReportShare? Share, IResult? Error)> ResolveShareAsync(
        string token, TestDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
            return (null, Results.NotFound(new { message = "报告链接无效" }));

        var share = await db.ReportShares.AsNoTracking().FirstOrDefaultAsync(s => s.Token == token, ct);
        if (share is null || share.Revoked)
            return (null, Results.NotFound(new { message = "报告链接已失效" }));
        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value <= DateTime.UtcNow)
            return (null, Results.Json(new { message = "报告链接已过期" },
                statusCode: StatusCodes.Status410Gone));

        return (share, null);
    }

    /// <summary>报告生成统一包一层：数据缺失/用例已被删干净时给 404，而不是 500</summary>
    private static async Task<(byte[] Content, string FileName)?> AsFile(Func<Task<ReportFile>> generate)
    {
        try
        {
            var file = await generate();
            return (file.Content, file.FileName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
