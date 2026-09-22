using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Projects;

/// <summary>
/// 项目成员管理（迭代 E·①）。挂载在 <c>/api/projects</c> 组下，并叠加 <c>WithProjectScope()</c>，
/// 因此每个端点的**项目角色**由 <see cref="ProjectRoleMetadata"/> 显式声明为 Owner。
///
/// 全局权限门与项目角色门是**两层**：
/// - 全局 <c>ManageProjects</c>：至少是平台内的 Tester/Admin（挡掉只读访客）；
/// - 项目 <c>Owner</c>：在该项目内必须是 Owner（成员激活式：项目尚无成员时放行，允许"加第一个成员"完成激活）。
/// </summary>
public static class ProjectMemberApiExtensions
{
    public static RouteGroupBuilder MapProjectMemberApi(this RouteGroupBuilder group)
    {
        // 成员列表：能看项目的人（Viewer 起）都能看成员
        group.MapGet("/{projectId:guid}/members", async (Guid projectId, TestDbContext db, CancellationToken ct) =>
        {
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
                return Results.NotFound();

            var members = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.Role).ThenBy(m => m.CreatedAt)
                .Select(m => new ProjectMemberDto(m.Id, m.UserId,
                    m.User.Username, m.User.DisplayName, m.Role, m.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(members);
        }).WithPermission(Permission.ViewProjects).Produces<List<ProjectMemberDto>>();

        // 成员候选：供加成员时搜人（只暴露 id/用户名/显示名，不暴露用户管理全量）
        group.MapGet("/{projectId:guid}/members/candidates", async (
            Guid projectId, TestDbContext db, CancellationToken ct, [FromQuery] string? search = null) =>
        {
            var query = db.Users.AsNoTracking().Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(u => u.Username.Contains(s) || u.DisplayName.Contains(s));
            }

            var items = await query
                .OrderBy(u => u.Username)
                .Take(20)
                .Select(u => new UserCandidateDto(u.Id, u.Username, u.DisplayName))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithPermission(Permission.ManageProjects).Produces<List<UserCandidateDto>>().WithProjectRole(ProjectRole.Owner);

        // 加成员
        group.MapPost("/{projectId:guid}/members", async (
            Guid projectId, AddProjectMemberRequest request, TestDbContext db,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
                return Results.NotFound();
            if (!Enum.IsDefined(request.Role))
                return Results.BadRequest(new { message = "非法的项目角色" });
            if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
                return Results.BadRequest(new { message = "用户不存在" });
            if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == request.UserId, ct))
                return Results.Conflict(new { message = "该用户已是项目成员" });

            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId = request.UserId,
                Role = request.Role,
                CreatedById = user.Id,
            });
            await db.SaveChangesAsync(ct);

            var dto = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.UserId == request.UserId)
                .Select(m => new ProjectMemberDto(m.Id, m.UserId,
                    m.User.Username, m.User.DisplayName, m.Role, m.CreatedAt))
                .FirstAsync(ct);
            return Results.Created($"/api/projects/{projectId}/members/{request.UserId}", dto);
        }).WithPermission(Permission.ManageProjects).WithProjectRole(ProjectRole.Owner)
          .WithAudit("AddMember", "Project");

        // 改成员角色
        group.MapPut("/{projectId:guid}/members/{userId:guid}", async (
            Guid projectId, Guid userId, UpdateProjectMemberRoleRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(request.Role))
                return Results.BadRequest(new { message = "非法的项目角色" });

            var member = await db.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
            if (member is null)
                return Results.NotFound();

            // 不允许把最后一个 Owner 降级：否则项目"无主"，只剩平台管理员能救
            if (member.Role == ProjectRole.Owner && request.Role != ProjectRole.Owner &&
                await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Owner, ct) <= 1)
                return Results.BadRequest(new { message = "项目至少需要保留一名 Owner" });

            member.Role = request.Role;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageProjects).WithProjectRole(ProjectRole.Owner)
          .WithAudit("UpdateMemberRole", "Project");

        // 移出成员
        group.MapDelete("/{projectId:guid}/members/{userId:guid}", async (
            Guid projectId, Guid userId, TestDbContext db, CancellationToken ct) =>
        {
            var member = await db.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
            if (member is null)
                return Results.NotFound();

            if (member.Role == ProjectRole.Owner &&
                await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Owner, ct) <= 1)
                return Results.BadRequest(new { message = "项目至少需要保留一名 Owner" });

            db.ProjectMembers.Remove(member);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageProjects).WithProjectRole(ProjectRole.Owner)
          .WithAudit("RemoveMember", "Project");

        return group;
    }
}
