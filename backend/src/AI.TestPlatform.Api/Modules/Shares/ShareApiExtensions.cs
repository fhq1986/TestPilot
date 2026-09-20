using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Reports;
using AI.TestPlatform.Application.Reports;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Shares;

// 报告分享令牌（/api/shares，需登录）：创建 / 列表 / 吊销
public static class ShareApiExtensions
{
    /// <summary>分享令牌默认有效期（天）</summary>
    private const int DefaultExpireDays = 7;
    private const int MaxExpireDays = 365;

    public static RouteGroupBuilder MapShareApi(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateShareRequest request,
            TestDbContext db,
            HttpContext http,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var validation = new CreateShareRequestValidator().Validate(request);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            // 校验被分享对象存在，并补齐项目 ID（后续聚合与权限提示都要用）
            var resolved = await ResolveAsync(db, request, ct);
            if (resolved.Error is not null)
                return Results.BadRequest(new { message = resolved.Error });

            var days = request.ExpiresInDays is null ? DefaultExpireDays
                : Math.Clamp(request.ExpiresInDays.Value, 1, MaxExpireDays);
            var share = new ReportShare
            {
                Token = GenerateToken(),
                Kind = request.Kind,
                RefId = request.RefId,
                ProjectId = resolved.ProjectId,
                Title = string.IsNullOrWhiteSpace(request.Title) ? resolved.Title : request.Title!.Trim(),
                From = request.From,
                To = request.To,
                // 0 表示不过期
                ExpiresAt = days <= 0 ? null : DateTime.UtcNow.AddDays(days),
                CreatedById = http.User.GetUserId(),
            };
            db.ReportShares.Add(share);
            await db.SaveChangesAsync(ct);

            // 链接基于请求来源推断的地址拼，而不是 AllowedOrigins：后者可能写 localhost，
            // 生成的分享链接会变成 localhost 而无法从外网访问
            var baseUrl = ReportShareLinkService.ClientFrontendBase(http.Request)
                ?? FrontendBase(configuration);
            return Results.Ok(ToDto(share, baseUrl));
        }).WithPermission(Permission.ViewReports).WithAudit("Create", "ReportShare");

        group.MapGet("/", async (
            TestDbContext db,
            IConfiguration configuration,
            CancellationToken ct,
            [FromQuery] ReportShareKind? kind = null,
            [FromQuery] Guid? refId = null) =>
        {
            var query = db.ReportShares.AsNoTracking();
            if (kind.HasValue) query = query.Where(s => s.Kind == kind.Value);
            if (refId.HasValue) query = query.Where(s => s.RefId == refId.Value);

            var rows = await query.OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(ct);
            var baseUrl = FrontendBase(configuration);
            return Results.Ok(rows.Select(s => ToDto(s, baseUrl)).ToList());
        }).WithPermission(Permission.ViewReports);

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var share = await db.ReportShares.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (share is null) return Results.NotFound();
            // 吊销而非删除：保留审计痕迹，报告链接立即失效
            share.Revoked = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ViewReports).WithAudit("Delete", "ReportShare");

        return group;
    }

    /// <summary>创建分享前确认对象存在，并推导标题与项目 ID</summary>
    private static async Task<(Guid? ProjectId, string Title, string? Error)> ResolveAsync(
        TestDbContext db, CreateShareRequest request, CancellationToken ct)
    {
        switch (request.Kind)
        {
            case ReportShareKind.Execution:
            {
                var execution = await db.Executions.AsNoTracking()
                    .Include(e => e.TestCase)
                    .FirstOrDefaultAsync(e => e.Id == request.RefId, ct);
                if (execution is null) return (null, string.Empty, "执行记录不存在");
                if (execution.Status is ExecutionStatus.Pending or ExecutionStatus.Running)
                    return (null, string.Empty, "执行尚未结束，暂不能生成分享报告");
                return (execution.TestCase?.ProjectId,
                    $"{execution.TestCase?.Name ?? "执行"} · 执行报告", null);
            }
            case ReportShareKind.SuiteRun:
            {
                var execution = await db.Executions.AsNoTracking()
                    .Where(e => e.SuiteRunId == request.RefId)
                    .Select(e => new { e.SuiteId, ProjectId = e.TestCase != null ? e.TestCase.ProjectId : (Guid?)null })
                    .FirstOrDefaultAsync(ct);
                if (execution is null) return (null, string.Empty, "套件运行不存在（没有关联的执行记录）");
                var suiteName = execution.SuiteId is null ? null : await db.TestSuites.AsNoTracking()
                    .Where(s => s.Id == execution.SuiteId.Value).Select(s => s.Name).FirstOrDefaultAsync(ct);
                return (execution.ProjectId, $"{suiteName ?? "套件"} · 运行报告", null);
            }
            case ReportShareKind.Project:
            {
                var project = await db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == request.RefId, ct);
                if (project is null) return (null, string.Empty, "项目不存在");
                return (project.Id, $"{project.Name} · 测试报告", null);
            }
            case ReportShareKind.TestPlan:
            {
                var plan = await db.TestPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == request.RefId, ct);
                if (plan is null) return (null, string.Empty, "测试计划不存在");
                var title = plan.ReleaseName is null
                    ? $"{plan.Name} · 验收报告"
                    : $"{plan.Name}（{plan.ReleaseName}）· 验收报告";
                return (plan.ProjectId, title, null);
            }
            default:
                return (null, string.Empty, "不支持的分享类型");
        }
    }

    // 令牌生成与前端地址拼接统一放在 ReportShareLinkService：
    // 发验收邮件的通知流程也要用同一套（否则两边会在令牌格式和链接形态上分叉）
    private static string GenerateToken() => ReportShareLinkService.NewToken();

    public static string FrontendBase(IConfiguration configuration) =>
        ReportShareLinkService.FrontendBase(configuration);

    public static ShareDto ToDto(ReportShare share, string frontendBase) => new(
        share.Id, share.Token, share.Kind, share.RefId, share.ProjectId,
        share.Title, share.ExpiresAt, share.Revoked, share.ViewCount, share.LastViewedAt,
        share.CreatedAt, $"{frontendBase}/share/{share.Token}");
}

public class CreateShareRequestValidator : AbstractValidator<CreateShareRequest>
{
    public CreateShareRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("分享类型不合法");
        RuleFor(x => x.RefId).NotEmpty().WithMessage("必须指定要分享的对象");
        RuleFor(x => x.Title).MaximumLength(300).WithMessage("标题不能超过300个字符");
        RuleFor(x => x.ExpiresInDays)
            .Must(days => days is null || days is >= 0 and <= 365)
            .WithMessage("有效期必须在 0（不过期）到 365 天之间");
    }
}
