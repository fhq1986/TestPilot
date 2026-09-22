using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

/// <summary>
/// 项目级授权判定（迭代 E·①）。
///
/// 判定口径（唯一权威，前端/端点都以此为准）：
/// 1. 平台级管理员（SuperAdmin / Admin）**全局放行**——保持既有"管理员可管所有项目"的行为，
///    避免引入项目成员后把管理员挡在门外。
/// 2. 「成员激活式」：项目**没有任何成员**时放行（退化为全局角色，由 <see cref="PermissionFilter"/> 继续把关），
///    这样历史项目与既有行为零回归；一旦项目被加入成员，该项目即进入"仅成员可见/可操作"模式。
/// 3. 项目已有成员时：要求当前用户是该项目的成员，且项目角色 <c>&gt;= required</c>。
/// </summary>
public interface IProjectAuthorization
{
    /// <summary>当前用户在该项目的角色；非成员返回 null。</summary>
    Task<ProjectRole?> GetRoleAsync(Guid userId, Guid projectId, CancellationToken ct = default);

    /// <summary>该项目是否已启用成员管理（即是否已有成员）。</summary>
    Task<bool> IsScopedAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>是否允许访问：见类型注释的判定口径。</summary>
    Task<bool> CanAccessAsync(Guid userId, UserRole? globalRole, Guid projectId, ProjectRole required,
        CancellationToken ct = default);
}

public sealed class ProjectAuthorization : IProjectAuthorization
{
    private readonly TestDbContext _db;

    public ProjectAuthorization(TestDbContext db) => _db = db;

    public async Task<ProjectRole?> GetRoleAsync(Guid userId, Guid projectId, CancellationToken ct = default) =>
        await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => (ProjectRole?)m.Role)
            .FirstOrDefaultAsync(ct);

    public Task<bool> IsScopedAsync(Guid projectId, CancellationToken ct = default) =>
        _db.ProjectMembers.AsNoTracking().AnyAsync(m => m.ProjectId == projectId, ct);

    public async Task<bool> CanAccessAsync(Guid userId, UserRole? globalRole, Guid projectId,
        ProjectRole required, CancellationToken ct = default)
    {
        // 1. 平台管理员全局放行
        if (globalRole is UserRole.SuperAdmin or UserRole.Admin)
            return true;

        // 2. 成员激活式：无成员的项目退化为全局角色（历史兼容）
        if (!await IsScopedAsync(projectId, ct))
            return true;

        // 3. 有成员：必须是成员且角色足够
        var role = await GetRoleAsync(userId, projectId, ct);
        return role.HasValue && role.Value >= required;
    }
}
