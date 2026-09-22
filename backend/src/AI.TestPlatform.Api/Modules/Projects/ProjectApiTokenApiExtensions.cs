using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Projects;

// 项目级 API Token 管理：创建（明文仅返回一次）/ 列表 / 吊销
// 鉴权走 JWT + ManageProjects 权限；令牌本身只用于 webhook 触发（见 WebhookApiExtensions）
public static class ProjectApiTokenApiExtensions
{
    public static RouteGroupBuilder MapProjectApiTokenApi(this RouteGroupBuilder group)
    {
        // ---- 列表：只回元数据，绝不含哈希或明文
        group.MapGet("/{projectId:guid}/api-tokens", async (
            Guid projectId, TestDbContext db, CancellationToken ct) =>
        {
            var exists = await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);
            if (!exists) return Results.NotFound(new { message = "项目不存在" });

            var items = await db.ProjectApiTokens.AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new ApiTokenDto(
                    t.Id, t.Name, t.Prefix, t.CreatedAt, t.ExpiresAt, t.RevokedAt, t.LastUsedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithPermission(Permission.ManageProjects).Produces<List<ApiTokenDto>>();

        // ---- 创建：唯一一次返回明文
        group.MapPost("/{projectId:guid}/api-tokens", async (
            Guid projectId, CreateApiTokenRequest request, TestDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                return Results.BadRequest(new { message = "请填写令牌用途（如「Jenkins 流水线」）" });
            if (name.Length > 100)
                return Results.BadRequest(new { message = "令牌用途不能超过 100 个字符" });
            if (request.ExpiresInDays is < 1 or > 3650)
                return Results.BadRequest(new { message = "有效期需在 1~3650 天之间" });

            var project = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == projectId, ct);
            if (!project) return Results.NotFound(new { message = "项目不存在" });

            var (plain, hash, prefix) = ApiTokenService.Generate();
            var token = new ProjectApiToken
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                TokenHash = hash,
                Prefix = prefix,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresInDays is int days ? DateTime.UtcNow.AddDays(days) : null,
            };
            db.ProjectApiTokens.Add(token);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/projects/{projectId}/api-tokens/{token.Id}",
                new ApiTokenCreatedDto(token.Id, token.Name, token.Prefix, plain,
                    token.CreatedAt, token.ExpiresAt));
        }).WithPermission(Permission.ManageProjects).WithAudit("Create", "ProjectApiToken");

        // ---- 吊销：软删除，保留记录供审计
        group.MapDelete("/{projectId:guid}/api-tokens/{tokenId:guid}", async (
            Guid projectId, Guid tokenId, TestDbContext db, CancellationToken ct) =>
        {
            var token = await db.ProjectApiTokens
                .FirstOrDefaultAsync(t => t.Id == tokenId && t.ProjectId == projectId, ct);
            if (token is null) return Results.NotFound(new { message = "令牌不存在" });
            if (token.RevokedAt is not null)
                return Results.Json(new { message = "令牌已吊销，无需重复操作" }, statusCode: 409);

            token.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "已吊销" });
        }).WithPermission(Permission.ManageProjects).WithAudit("Revoke", "ProjectApiToken");

        return group;
    }
}

/// <summary>列表项。Prefix 用于辨认「是哪把钥匙」；不含任何能还原令牌的字段</summary>
public record ApiTokenDto(
    Guid Id, string Name, string Prefix, DateTime CreatedAt,
    DateTime? ExpiresAt, DateTime? RevokedAt, DateTime? LastUsedAt);

/// <summary>创建响应。PlainToken 只在这一次响应里出现，服务端不存明文</summary>
public record ApiTokenCreatedDto(
    Guid Id, string Name, string Prefix, string PlainToken, DateTime CreatedAt, DateTime? ExpiresAt);

public record CreateApiTokenRequest(string? Name, int? ExpiresInDays);
