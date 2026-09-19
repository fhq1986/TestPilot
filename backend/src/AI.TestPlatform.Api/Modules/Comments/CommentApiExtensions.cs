using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Comments;

// 通用评论：GET 列表 / POST 新增 / DELETE 删除（作者本人或管理员）。
// 挂载对象的存在性在写入时校验（防止给不存在的对象挂评论）；
// @提及的解析在前端做高亮展示，站内提醒的联动（邮件/企微）留待下一步。
public static class CommentApiExtensions
{
    public static RouteGroupBuilder MapCommentApi(this RouteGroupBuilder group)
    {
        // 列表：按挂载对象倒序。登录即可看（评论可见性跟随挂载对象的可见性，
        // 第一版不另做对象级权限校验——平台内所有用例/缺陷/计划对登录用户可见的口径与现状一致）
        group.MapGet("/", async (
            CommentTarget? target, Guid? targetId,
            TestDbContext db, CancellationToken ct) =>
        {
            if (target is null || targetId is null)
                return Results.BadRequest(new { message = "缺少 target 或 targetId" });

            var items = await db.Comments.AsNoTracking()
                .Where(c => c.Target == target && c.TargetId == targetId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CommentDto(c.Id, c.Author.DisplayName, c.Body, c.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization();

        group.MapPost("/", async (
            CreateCommentRequest request,
            TestDbContext db,
            HttpContext http,
            IServiceScopeFactory scopeFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Body))
                return Results.BadRequest(new { message = "评论内容不能为空" });
            var body = request.Body.Trim();
            if (body.Length > 2000)
                return Results.BadRequest(new { message = "评论不能超过 2000 个字符" });

            // 挂载对象存在性：写评论前先确认对象存在
            var exists = request.Target switch
            {
                CommentTarget.TestCase => await db.TestCases.AsNoTracking()
                    .AnyAsync(t => t.Id == request.TargetId, ct),
                CommentTarget.Defect => await db.Defects.AsNoTracking()
                    .AnyAsync(d => d.Id == request.TargetId, ct),
                CommentTarget.TestPlan => await db.TestPlans.AsNoTracking()
                    .AnyAsync(p => p.Id == request.TargetId, ct),
                _ => false,
            };
            if (!exists)
                return Results.NotFound(new { message = "评论的对象不存在" });

            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                Target = request.Target,
                TargetId = request.TargetId,
                AuthorId = http.User.GetUserId(),
                Body = body,
            };
            db.Comments.Add(comment);
            await db.SaveChangesAsync(ct);

            var authorName = await db.Users.AsNoTracking()
                .Where(u => u.Id == comment.AuthorId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);

            // @提及提醒 fire-and-forget：SMTP 最多 15s，同步等会把评论提交卡住；
            // 自建 scope（当前请求 scope 随响应结束释放），失败只记日志
            var commentId = comment.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<NotificationService>();
                    await notifier.NotifyCommentMentionsAsync(commentId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[comment-mention] 提醒失败: {ex.Message}");
                }
            });

            return Results.Created($"/api/comments/{comment.Id}",
                new CommentDto(comment.Id, authorName ?? "未知", comment.Body, comment.CreatedAt));
        }).RequireAuthorization().WithAudit("Create", "Comment");

        group.MapDelete("/{id:guid}", async (
            Guid id, TestDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var comment = await db.Comments.FindAsync(new object[] { id }, ct);
            if (comment is null) return Results.NotFound(new { message = "评论不存在" });

            // 只有作者本人或管理员能删——评论是协作留痕，不能被无关人清掉
            var userId = http.User.GetUserId();
            var isAdmin = http.User.FindFirst(CurrentUser.RoleClaimType)?.Value == nameof(UserRole.Admin);
            if (comment.AuthorId != userId && !isAdmin)
                return Results.Json(new { message = "只有作者本人或管理员可以删除评论" }, statusCode: 403);

            db.Comments.Remove(comment);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithAudit("Delete", "Comment");

        return group;
    }
}

public record CommentDto(Guid Id, string AuthorName, string Body, DateTime CreatedAt);

public record CreateCommentRequest(CommentTarget Target, Guid TargetId, string? Body);
