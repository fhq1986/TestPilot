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
/// **唯一例外：superadmin**。按产品要求，超级管理员可在消息中心查看**所有用户**的消息、
/// 并对其执行已读/删除操作（不受接收人限制）。该例外只对 <see cref="UserRole.SuperAdmin"/> 生效，
/// 普通管理员（Admin）仍只看自己的消息。
///
/// 刻意写成 <c>WithPermission(Permission.None)</c> 而不是省略：这是「本端点不要求任何权限位」
/// 的显式声明（PermissionCatalog.Has 对 None 恒为真），既让权限自检测试有据可依，
/// 也避免后来者误以为这里忘了挂权限。登录态仍由 RequireAuthorization 保证。
/// </summary>
public static class NotificationApiExtensions
{
    public static RouteGroupBuilder MapNotificationApi(this RouteGroupBuilder group)
    {
        // 我的消息列表（按时间倒序，可按分类/未读/项目/标题/时间范围筛选）；superadmin 看全部
        group.MapGet("/", async (
            TestDbContext db,
            ICurrentUser currentUser,
            CancellationToken ct,
            [FromQuery] bool? unreadOnly = null,
            [FromQuery] NotificationCategory? category = null,
            [FromQuery] Guid? projectId = null,
            [FromQuery] string? title = null,
            [FromQuery] DateTime? dateFrom = null,
            [FromQuery] DateTime? dateTo = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var isSuper = currentUser.Role == UserRole.SuperAdmin;
            var query = db.InAppNotifications.AsNoTracking().AsQueryable();
            // 非 superadmin 必须限定为本人收件箱（唯一的防泄露闸门）
            if (!isSuper) query = query.Where(n => n.UserId == currentUser.Id);
            if (unreadOnly == true) query = query.Where(n => !n.IsRead);
            if (category.HasValue) query = query.Where(n => n.Category == category.Value);
            if (projectId.HasValue) query = query.Where(n => n.ProjectId == projectId.Value);
            if (!string.IsNullOrWhiteSpace(title))
            {
                var kw = title.Trim();
                query = query.Where(n => EF.Functions.Like(n.Title, $"%{kw}%")
                                         || (n.Body != null && EF.Functions.Like(n.Body, $"%{kw}%")));
            }
            // Npgsql timestamptz 只接受 Kind=Utc；[FromQuery] 绑 URL "2026-09-22" → Kind=Unspecified → 必须转
            if (dateFrom.HasValue)
            {
                var fromUtc = DateTimeUtcHelper.SpecifyUtc(dateFrom.Value);
                query = query.Where(n => n.CreatedAt >= fromUtc);
            }
            if (dateTo.HasValue)
            {
                // dateTo 是"当天 23:59:59.999"的语义 — 用户选 9/21 想包含当天所有消息
                var toExclusiveUtc = DateTimeUtcHelper.SpecifyUtc(dateTo.Value.Date.AddDays(1));
                query = query.Where(n => n.CreatedAt < toExclusiveUtc);
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            // superadmin 全域查看时补上接收人显示名（否则分不清是谁的收件箱）
            var names = new Dictionary<Guid, string>();
            if (isSuper && items.Count > 0)
            {
                var ids = items.Select(n => n.UserId).Distinct().ToList();
                names = await db.Users.AsNoTracking()
                    .Where(u => ids.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id,
                        u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName!, ct);
            }

            // 补上项目显示名（前端筛选下拉 + 列表展示需要）
            var projectNames = new Dictionary<Guid, string>();
            var pids = items.Where(n => n.ProjectId.HasValue).Select(n => n.ProjectId!.Value).Distinct().ToList();
            if (pids.Count > 0)
            {
                projectNames = await db.Projects.AsNoTracking()
                    .Where(p => pids.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            }

            // 用显式循环而不是 LINQ Select：ToDto 带可选参数，方法组/委托推断容易与
            // Select 的「带索引」重载撞上（CS0411），显式循环最稳。
            var dtos = new List<NotificationDto>(items.Count);
            foreach (var n in items)
                dtos.Add(InAppNotificationService.ToDto(n,
                    names.TryGetValue(n.UserId, out var name) ? name : null,
                    n.ProjectId.HasValue && projectNames.TryGetValue(n.ProjectId.Value, out var pname) ? pname : null));

            return Results.Ok(new PagedResult<NotificationDto>(dtos, total, page, pageSize));
        }).WithPermission(Permission.None).Produces<PagedResult<NotificationDto>>().RequireAuthorization();

        // 未读数（铃铛角标）。分类维度一并返回，供弹层里的分类页签显示计数
        group.MapGet("/unread-count", async (
            TestDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var isSuper = currentUser.Role == UserRole.SuperAdmin;
            var query = db.InAppNotifications.AsNoTracking().Where(n => !n.IsRead);
            if (!isSuper) query = query.Where(n => n.UserId == currentUser.Id);

            var byCategory = await query
                .GroupBy(n => n.Category)
                .Select(g => new NotificationCategoryCount((int)g.Key, g.Count()))
                .ToListAsync(ct);

            return Results.Ok(new NotificationUnreadDto(
                byCategory.Sum(c => c.Count), byCategory));
        }).WithPermission(Permission.None).Produces<NotificationUnreadDto>().RequireAuthorization();

        // 单条已读（superadmin 可操作任意消息）
        group.MapPost("/{id:guid}/read", async (
            Guid id, TestDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var isSuper = currentUser.Role == UserRole.SuperAdmin;
            // 条件更新带上 UserId：别人的消息连"改一下"都不该被允许（superadmin 除外）
            var query = db.InAppNotifications.Where(n => n.Id == id && !n.IsRead);
            if (!isSuper) query = query.Where(n => n.UserId == currentUser.Id);

            var affected = await query.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

            return affected > 0 ? Results.NoContent() : Results.Ok(new { alreadyRead = true });
        }).WithPermission(Permission.None).RequireAuthorization();

        // 全部已读（可按分类限定；superadmin 作用于全部用户）
        group.MapPost("/read-all", async (
            TestDbContext db, ICurrentUser currentUser, CancellationToken ct,
            [FromQuery] NotificationCategory? category = null) =>
        {
            var isSuper = currentUser.Role == UserRole.SuperAdmin;
            var query = db.InAppNotifications.Where(n => !n.IsRead);
            if (!isSuper) query = query.Where(n => n.UserId == currentUser.Id);
            if (category.HasValue) query = query.Where(n => n.Category == category.Value);

            var affected = await query.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

            return Results.Ok(new { read = affected });
        }).WithPermission(Permission.None).RequireAuthorization();

        // 删除单条（消息不做软删，删了就是删了；superadmin 可删任意）
        group.MapDelete("/{id:guid}", async (
            Guid id, TestDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var isSuper = currentUser.Role == UserRole.SuperAdmin;
            var query = db.InAppNotifications.Where(n => n.Id == id);
            if (!isSuper) query = query.Where(n => n.UserId == currentUser.Id);

            var affected = await query.ExecuteDeleteAsync(ct);

            return affected > 0 ? Results.NoContent() : Results.NotFound();
        }).WithPermission(Permission.None).RequireAuthorization();

        return group;
    }
}
