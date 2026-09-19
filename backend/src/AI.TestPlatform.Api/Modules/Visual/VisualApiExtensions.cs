using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Visual;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Visual;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Visual;

// 视觉基线端点（/api/visual）：列表 / 接受变化（更新基线）/ 删除基线 / 用例视觉配置
public static class VisualApiExtensions
{
    public static RouteGroupBuilder MapVisualApi(this RouteGroupBuilder group)
    {
        // 基线列表：按项目或用例筛选，供「视觉基线」管理页与用例详情页使用
        group.MapGet("/baselines", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] Guid? testCaseId = null,
            [FromQuery] string? keyword = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            // IgnoreQueryFilters：投影里引用了 TestCase（Name/ProjectId），EF 会对 join 的
            // TestCases 应用软删过滤器——已删用例的基线被悄悄剔除，而上面 CountAsync 不 join
            // 就不受影响，结果是 total=18 但列表只剩 12 条：第 2 页永远空白。
            // 基线是独立资产，已删用例的基线也要显示（删除基线走它自己的端点）。
            var query = db.VisualBaselines.AsNoTracking().IgnoreQueryFilters();
            if (testCaseId.HasValue)
                query = query.Where(b => b.TestCaseId == testCaseId.Value);
            if (projectId.HasValue)
                query = query.Where(b => b.TestCase.ProjectId == projectId.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(b => EF.Functions.ILike(b.TestCase.Name, $"%{k}%"));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(b => b.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new VisualBaselineDto(
                    b.Id, b.TestCaseId, b.TestCase.Name, b.TestCase.Module, b.StepOrder,
                    b.ImagePath, b.Width, b.Height, b.CompareCount,
                    b.LastComparedAt, b.SourceExecutionId, b.CreatedAt, b.UpdatedAt))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<VisualBaselineDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewTestCases);

        // 接受变化：把某次执行的步骤截图设为新基线（确认差异无误后的一键操作）
        group.MapPost("/baselines/accept", async (
            AcceptBaselineRequest request,
            VisualRegressionService visual,
            CancellationToken ct) =>
        {
            var (baseline, error) = await visual.AcceptAsync(request.ExecutionResultId, ct);
            // 失败原因如实回给界面：原来统一回"结果不存在或没有截图"，
            // 而"步骤没通过所以不接受"是完全不同的一件事，混在一起用户没法判断该怎么办
            return baseline is null
                ? Results.BadRequest(new { message = error ?? "该步骤结果不存在或没有可用截图" })
                : Results.Ok(baseline);
        }).WithPermission(Permission.ManageBaselines).WithAudit("AcceptBaseline", "VisualBaseline");

        group.MapDelete("/baselines/{id:guid}", async (
            Guid id,
            TestDbContext db,
            ScreenshotStorage screenshots,
            CancellationToken ct) =>
        {
            var baseline = await db.VisualBaselines.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (baseline is null) return Results.NotFound();

            db.VisualBaselines.Remove(baseline);
            await db.SaveChangesAsync(ct);
            // 基线图片一并删除，下次执行会重新建立（避免残留孤儿对象）
            // ImagePath 是 /screenshots/ 前缀 URL，走对象 key 删除；历史本地路径也能兜底
            screenshots.DeleteByUrl(baseline.ImagePath);
            return Results.NoContent();
        }).WithPermission(Permission.ManageBaselines).WithAudit("Delete", "VisualBaseline");

        // 某用例的视觉开关、阈值与基线数量（用例编辑页调用）
        group.MapGet("/cases/{testCaseId:guid}", async (
            Guid testCaseId, TestDbContext db, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.AsNoTracking()
                .Where(t => t.Id == testCaseId)
                .Select(t => new { t.Id, t.Name, t.VisualEnabled, t.VisualThreshold })
                .FirstOrDefaultAsync(ct);
            if (testCase is null) return Results.NotFound();

            var baselineCount = await db.VisualBaselines.AsNoTracking()
                .CountAsync(b => b.TestCaseId == testCaseId, ct);

            return Results.Ok(new VisualCaseSettingDto(
                testCase.Id, testCase.Name, testCase.VisualEnabled, testCase.VisualThreshold, baselineCount));
        }).WithPermission(Permission.ViewTestCases);

        return group;
    }
}
