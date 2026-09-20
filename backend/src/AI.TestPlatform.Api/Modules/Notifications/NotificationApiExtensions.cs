using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Notifications;

/// <summary>
/// 站内消息端点（/api/notifications）。
///
/// **权限：只按 UserId 过滤，不挂具体权限位**。消息是「我自己的收件箱」，
/// 任何登录用户都有权看自己的消息；能看到哪些内容由写入侧决定（见 InAppNotificationService），
/// 而不是由这里的权限门槛决定。所以这里的每个查询都**必须**带 UserId 条件，
/// 一旦漏了就是全站消息泄露——这是本文件唯一的高危点。
///
/// 刻意写成 <c>WithPermission(Permission.None)</c> 而不是省略：这是「本端点不要求任何权限位」
/// 的显式声明（PermissionCatalog.Has 对 None 恒为真），既让权限自检测试有据可依，
/// 也避免后来者误以为这里忘了挂权限。登录态仍由 RequireAuthorization 保证。
/// </summary>
public static class NotificationApiExtensions
{
    public static RouteGroupBuilder MapNotificationApi(this RouteGroupBuilder group)
    {
        // 我的消息列表（按时间倒序，可按分类与未读筛选）
        group.MapGet("/", async (
            TestDbContext db,
            HttpContext http,
            CancellationToken ct,
            [FromQuery] bool? unreadOnly = null,
            [FromQuery] NotificationCategory? category = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var userId = http.User.GetUserId();
            var query = db.InAppNotifications.AsNoTracking().Where(n => n.UserId == userId);
            if (unreadOnly == true) query = query.Where(n => !n.IsRead);
            if (category.HasValue) query = query.Where(n => n.Category == category.Value);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<NotificationDto>(
                items.Select(InAppNotificationService.ToDto).ToList(), total, page, pageSize));
        }).WithPermission(Permission.None).RequireAuthorization();

        // 未读数（铃铛角标）。分类维度一并返回，供弹层里的分类页签显示计数
        group.MapGet("/unread-count", async (
            TestDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            var byCategory = await db.InAppNotifications.AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsRead)
                .GroupBy(n => n.Category)
                .Select(g => new NotificationCategoryCount((int)g.Key, g.Count()))
                .ToListAsync(ct);

            return Results.Ok(new NotificationUnreadDto(
                byCategory.Sum(c => c.Count), byCategory));
        }).WithPermission(Permission.None).RequireAuthorization();

        // 单条已读
        group.MapPost("/{id:guid}/read", async (
            Guid id, TestDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            // 条件更新带上 UserId：别人的消息连"改一下"都不该被允许
            var affected = await db.InAppNotifications
                .Where(n => n.Id == id && n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

            return affected > 0 ? Results.NoContent() : Results.Ok(new { alreadyRead = true });
        }).WithPermission(Permission.None).RequireAuthorization();

        // 全部已读（可按分类限定）
        group.MapPost("/read-all", async (
            TestDbContext db, HttpContext http, CancellationToken ct,
            [FromQuery] NotificationCategory? category = null) =>
        {
            var userId = http.User.GetUserId();
            var query = db.InAppNotifications.Where(n => n.UserId == userId && !n.IsRead);
            if (category.HasValue) query = query.Where(n => n.Category == category.Value);

            var affected = await query.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

            return Results.Ok(new { read = affected });
        }).WithPermission(Permission.None).RequireAuthorization();

        // 删除单条（消息不做软删，删了就是删了）
        group.MapDelete("/{id:guid}", async (
            Guid id, TestDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            var affected = await db.InAppNotifications
                .Where(n => n.Id == id && n.UserId == userId)
                .ExecuteDeleteAsync(ct);

            return affected > 0 ? Results.NoContent() : Results.NotFound();
        }).WithPermission(Permission.None).RequireAuthorization();

        return group;
    }
}
