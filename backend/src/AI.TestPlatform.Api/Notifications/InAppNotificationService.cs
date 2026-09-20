using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Api.Hubs;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Notifications;

/// <summary>
/// 一条待写入的站内消息（不含接收人）。用 record 而不是一长串参数，
/// 调用点靠具名参数就能读懂，也不必为每种事件各写一个重载。
/// </summary>
public sealed record NotificationDraft(
    NotificationCategory Category,
    string Title,
    NotificationLevel Level = NotificationLevel.Info,
    string? Body = null,
    string? LinkUrl = null,
    string? LinkLabel = null,
    string? SourceType = null,
    Guid? SourceId = null);

/// <summary>
/// 站内消息（消息中心）写入与推送。
///
/// **三条硬约定**：
/// 1) **永不抛异常**。消息是业务的副产品，写不进去只记日志——与 NotificationService 同一立场。
///    调用方因此可以放心地在业务写库成功之后直接 await，不必包 try/catch。
/// 2) **不受 NotifyEnabled 总开关约束**。那道闸门是防外部渠道打扰的；
///    站内消息是「记录」，关掉记录只会让人回来时一头雾水。
/// 3) **只发给本来就有权看该资源的人**。消息标题/正文带实体名，误发给无权用户
///    等于绕过权限体系泄露信息——所以接收人一律从资源关系（负责人/创建人）
///    或权限位（按角色矩阵反查）推导，绝不做项目级广播。
///
/// 写入是同步 await 的（不像邮件那样 fire-and-forget）：本地一次 INSERT + 一次内存广播，
/// 没有网络等待，同步做反而省掉了自建 scope 与「消息可能丢」的复杂度。
/// </summary>
public class InAppNotificationService
{
    /// <summary>单次推送的接收人上限：防止误传"全体用户"把库写爆</summary>
    private const int MaxRecipients = 200;

    private readonly TestDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<InAppNotificationService> _logger;

    public InAppNotificationService(TestDbContext db, IHubContext<NotificationHub> hub,
        ILogger<InAppNotificationService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>发给一个接收人（可空：资源没有负责人/创建人时静默跳过）</summary>
    public Task PushAsync(Guid? userId, NotificationDraft draft, CancellationToken ct = default) =>
        userId.HasValue
            ? PushAsync(new[] { userId.Value }, draft, ct)
            : Task.CompletedTask;

    /// <summary>发给多个接收人（自动去重、剔除空值）</summary>
    public async Task PushAsync(IEnumerable<Guid>? userIds, NotificationDraft draft,
        CancellationToken ct = default)
    {
        if (userIds is null) return;
        var recipients = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (recipients.Count == 0) return;
        if (recipients.Count > MaxRecipients)
        {
            _logger.LogWarning("站内消息接收人 {Count} 超过上限 {Max}，已截断。标题：{Title}",
                recipients.Count, MaxRecipients, draft.Title);
            recipients = recipients.Take(MaxRecipients).ToList();
        }

        try
        {
            var rows = recipients.Select(userId => new InAppNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Category = draft.Category,
                Level = draft.Level,
                Title = Truncate(draft.Title, 200)!,
                Body = Truncate(draft.Body, 1000),
                LinkUrl = Truncate(draft.LinkUrl, 500),
                LinkLabel = Truncate(draft.LinkLabel, 30),
                SourceType = Truncate(draft.SourceType, 50),
                SourceId = draft.SourceId,
                CreatedAt = DateTime.UtcNow,
            }).ToList();

            _db.InAppNotifications.AddRange(rows);
            await _db.SaveChangesAsync(ct);

            await BroadcastAsync(rows, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 消息写不进去不能影响业务主流程
            _logger.LogWarning(ex, "站内消息写入失败。分类 {Category}，标题 {Title}",
                draft.Category, draft.Title);
        }
    }

    /// <summary>
    /// 按权限位找接收人：用于「没有单一责任人」的事件（如用例提交评审 → 所有能管用例的人）。
    /// 走角色矩阵反查，而不是查用户表里的权限列——权限矩阵只有一处定义（PermissionCatalog）。
    /// <paramref name="excludeUserId"/> 用于排掉操作人自己（自己提交的评审不该自己收到）。
    /// </summary>
    public async Task PushToPermissionAsync(Permission required, NotificationDraft draft,
        Guid? excludeUserId = null, CancellationToken ct = default)
    {
        try
        {
            var users = await _db.Users.AsNoTracking()
                .Where(u => u.IsActive)
                .Select(u => new { u.Id, u.Role })
                .ToListAsync(ct);

            var recipients = users
                .Where(u => PermissionCatalog.Has(PermissionCatalog.Of(u.Role), required))
                .Select(u => u.Id)
                .Where(id => id != excludeUserId)
                .ToList();

            await PushAsync(recipients, draft, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "按权限推送站内消息失败。所需权限 {Permission}，标题 {Title}",
                required, draft.Title);
        }
    }

    /// <summary>把刚写入的消息实时推给各自的接收人（离线用户下次拉列表自然能看到）</summary>
    private async Task BroadcastAsync(List<InAppNotification> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            try
            {
                await _hub.Clients.Group(NotificationHub.UserGroup(row.UserId))
                    .SendAsync("NotificationReceived", ToDto(row), ct);
            }
            catch (Exception ex)
            {
                // 推送失败无所谓：前端还有轮询未读数兜底
                _logger.LogDebug(ex, "站内消息实时推送失败 用户 {UserId}", row.UserId);
            }
        }
    }

    internal static NotificationDto ToDto(InAppNotification n) => new(
        n.Id, (int)n.Category, (int)n.Level, n.Title, n.Body, n.LinkUrl, n.LinkLabel,
        n.SourceType, n.SourceId, n.IsRead, n.CreatedAt);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}

/// <summary>站内消息（GET /api/notifications 列表项，也是 SignalR 推送的负载）</summary>
public sealed record NotificationDto(
    Guid Id,
    int Category,
    int Level,
    string Title,
    string? Body,
    string? LinkUrl,
    string? LinkLabel,
    string? SourceType,
    Guid? SourceId,
    bool IsRead,
    DateTime CreatedAt);

/// <summary>未读数（GET /api/notifications/unread-count）</summary>
public sealed record NotificationUnreadDto(int Total, IReadOnlyList<NotificationCategoryCount> ByCategory);

public sealed record NotificationCategoryCount(int Category, int Count);
