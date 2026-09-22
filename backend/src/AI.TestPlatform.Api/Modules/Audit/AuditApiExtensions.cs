using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Audit;

// 审计日志查询端点（/api/audit，仅管理员）
public static class AuditApiExtensions
{
    public static RouteGroupBuilder MapAuditApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            string? action,
            string? resourceType,
            string? username,
            bool? succeeded,
            DateTime? from,
            DateTime? to,
            int? page,
            int? pageSize,
            TestDbContext db,
            CancellationToken ct) =>
        {
            // 列表筛选口径与导出共用 AuditQuery + ApplyFilter，
            // 避免「页面看到的」和「导出的」不一致
            var filter = new AuditQuery(action, resourceType, username, succeeded, from, to);
            var query = AuditExportService.ApplyFilter(db.AuditLogs.AsNoTracking(), filter);

            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var current = Math.Max(page ?? 1, 1);
            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((current - 1) * size).Take(size)
                .Select(l => new AuditLogViewDto(
                    l.Id, l.Username, l.UserRole, l.Action, l.ResourceType,
                    l.ResourceId, l.ResourceName, l.Method, l.Path,
                    l.StatusCode, l.Succeeded, l.Detail, l.IpAddress, l.DurationMs, l.CreatedAt,
                    l.ResponseBody))
                .ToListAsync(ct);

            return Results.Ok(new AuditLogPageDto(total, current, size, items));
        }).WithPermission(Permission.ViewAuditLog).Produces<AuditLogPageDto>();

        // 导出为 xlsx：遵循当前筛选条件（合规评审需要把记录带走，翻页看是不够的）
        group.MapGet("/export", async (
            string? action,
            string? resourceType,
            string? username,
            bool? succeeded,
            DateTime? from,
            DateTime? to,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var bytes = await AuditExportService.BuildAsync(db,
                new AuditQuery(action, resourceType, username, succeeded, from, to), ct);
            var fileName = $"audit-log-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }).WithPermission(Permission.ViewAuditLog).WithAudit("Export", "AuditLog", captureBody: false);

        // 供前端渲染筛选下拉：哪些动作 / 资源类型在日志里真实出现过
        group.MapGet("/facets", async (TestDbContext db, CancellationToken ct) =>
        {
            var actions = await db.AuditLogs.AsNoTracking()
                .Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
            var resourceTypes = await db.AuditLogs.AsNoTracking()
                .Select(l => l.ResourceType).Distinct().OrderBy(r => r).ToListAsync(ct);
            var usernames = await db.AuditLogs.AsNoTracking()
                .Where(l => l.Username != null)
                .Select(l => l.Username!).Distinct().OrderBy(u => u).ToListAsync(ct);
            return Results.Ok(new { actions, resourceTypes, usernames });
        }).WithPermission(Permission.ViewAuditLog);

        return group;
    }

    /// <param name="ResponseBody">
    /// 响应结果摘要（脱敏后）。**列表里不展示**（体积大、列表只需要扫一眼谁做了什么），
    /// 但必须随记录一起返回，详情抽屉才能直接看到，不必再发一次请求。
    /// </param>
    public record AuditLogViewDto(
        Guid Id, string? Username, string? UserRole, string Action, string ResourceType,
        string? ResourceId, string? ResourceName, string Method, string Path,
        int StatusCode, bool Succeeded, string? Detail, string? IpAddress, int DurationMs, DateTime CreatedAt,
        string? ResponseBody = null);

    public record AuditLogPageDto(int Total, int Page, int PageSize, IReadOnlyList<AuditLogViewDto> Items);
}
