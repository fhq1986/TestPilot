using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

/// <summary>用户管理 DTO</summary>
public record UserViewDto(
    Guid Id, string Username, string DisplayName, string? Email,
    UserRole Role, string RoleName, bool IsActive,
    DateTime? LastLoginAt, string? LastLoginIp,
    DateTime CreatedAt, DateTime? UpdatedAt,
    string? SsoProvider = null);

/// <summary>下拉用的用户选项（不含邮箱明文，只标明是否已配置）</summary>
public record UserOptionDto(Guid Id, string Name, bool HasEmail, bool IsActive);

public record CreateUserRequest(
    string Username, string Password, string DisplayName, UserRole Role,
    string? Email = null);

public record UpdateUserRequest(
    string DisplayName, UserRole Role, bool IsActive,
    string? Email = null);
public record ResetPasswordRequest(string NewPassword);
public record ChangeOwnPasswordRequest(string OldPassword, string NewPassword);

/// <summary>
/// 用户管理服务（迭代 C）。
///
/// 关键约定：**任何影响权限或凭据的变更都必须自增 TokenVersion**，
/// 否则已签发的 token 在有效期内仍可继续使用（角色改了但权限没变，是典型的越权窗口）。
/// 覆盖的操作：改角色、停用 / 启用、重置密码、修改本人密码。
/// </summary>
public class UserService
{
    public const int MinPasswordLength = 8;
    public const int MaxUsernameLength = 50;
    public const int MaxDisplayNameLength = 100;

    private readonly TestDbContext _db;
    private readonly IAuthService _auth;

    public UserService(TestDbContext db, IAuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    /// <summary>
    /// 用户列表（分页 + 服务端筛选）。
    ///
    /// 之前是全量返回：单团队几十个账号没问题，但用户数一多，
    /// 列表接口就会变成一次全表拉取，前端也不得不把所有行渲染出来。
    /// </summary>
    public async Task<PagedResult<UserViewDto>> ListAsync(
        string? search, UserRole? role, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(u => u.Username.Contains(keyword) || u.DisplayName.Contains(keyword));
        }
        if (role is not null)
            query = query.Where(u => u.Role == role);

        var current = Math.Max(page, 1);
        var size = Math.Clamp(pageSize, 1, 200);
        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.Role).ThenBy(u => u.Username)
            .Skip((current - 1) * size).Take(size)
            .ToListAsync(ct);

        return new PagedResult<UserViewDto>(users.Select(ToView).ToList(), total, current, size);
    }

    /// <summary>各角色人数（列表页顶部的概览徽标用，避免前端为此再拉一次全量）</summary>
    public async Task<Dictionary<string, int>> CountByRoleAsync(CancellationToken ct)
    {
        var counts = await _db.Users.AsNoTracking()
            .GroupBy(u => u.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // 三个角色都要出现（哪怕是 0），前端不必自己补缺
        return Enum.GetValues<UserRole>().ToDictionary(
            r => r.ToString(),
            r => counts.FirstOrDefault(c => c.Role == r)?.Count ?? 0);
    }

    public async Task<UserViewDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : ToView(user);
    }

    /// <summary>
    /// 用户选项（供「项目负责人 / 测试负责人」这类下拉用）。
    ///
    /// 刻意**不复用** <c>/api/users</c>：那个接口要求 ManageUsers（实际上只有管理员有），
    /// 而"谁能被选为负责人"是项目维度的信息，不该被用户管理权限绑住——
    /// 否则非管理员建项目时负责人下拉永远是空的。
    ///
    /// 只给出 HasEmail 而不回带邮箱本身：这是**全量**用户列表，
    /// 把每个人的邮箱都摊给所有能看项目的人没有必要；
    /// 真正需要看到具体地址的只有「本项目的测试负责人」一个人，那个走 ProjectDto。
    /// 也带出 IsActive，让表单能在保存**之前**就提示「这个人已停用」。
    /// </summary>
    public async Task<IReadOnlyList<UserOptionDto>> ListOptionsAsync(string? search, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking();
        // 关键字过滤（安全审查 S6）：下拉支持远程搜索时避免全量拉取
        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(u =>
                u.Username.Contains(keyword) ||
                (u.DisplayName != null && u.DisplayName.Contains(keyword)));
        }

        var users = await query
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.DisplayName).ThenBy(u => u.Username)
            // 返回上限：下拉场景足够，同时兜底防止无界列表
            .Take(500)
            .Select(u => new { u.Id, u.DisplayName, u.Username, u.Email, u.IsActive })
            .ToListAsync(ct);

        return users.Select(u => new UserOptionDto(
            u.Id,
            string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName,
            !string.IsNullOrWhiteSpace(u.Email),
            u.IsActive)).ToList();
    }

    /// <summary>创建用户。用户名冲突返回 null（由端点转 409）。</summary>
    public async Task<UserViewDto?> CreateAsync(CreateUserRequest request, Guid? createdById, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            return null;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
            Email = NormalizeEmail(request.Email),
            Role = request.Role,
            IsActive = true,
            TokenVersion = 0,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return ToView(user);
    }

    /// <summary>
    /// 更新用户信息。角色或启用状态发生变化时自增 TokenVersion（旧 token 立即失效）。
    /// 返回 null 表示用户不存在。
    /// </summary>
    public async Task<UserViewDto?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;

        var invalidateSessions = user.Role != request.Role || user.IsActive != request.IsActive;

        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? user.Username : request.DisplayName.Trim();
        user.Email = NormalizeEmail(request.Email);
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        if (invalidateSessions)
        {
            user.TokenVersion++;
            // 立即清缓存：否则旧的「会话有效」判断还会命中缓存，停用/降权最长 30 秒才生效
            _auth.InvalidateSessionCache(user.Id);
        }

        await _db.SaveChangesAsync(ct);
        return ToView(user);
    }

    /// <summary>重置密码（管理员操作），同时踢掉该用户所有在线会话</summary>
    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _auth.InvalidateSessionCache(user.Id);
        return true;
    }

    /// <summary>修改本人密码（需校验旧密码），成功后踢掉其它会话</summary>
    public async Task<(bool Ok, string? Error)> ChangeOwnPasswordAsync(
        Guid userId, ChangeOwnPasswordRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return (false, "用户不存在");

        if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
            return (false, "原密码不正确");

        if (request.OldPassword == request.NewPassword)
            return (false, "新密码不能与原密码相同");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _auth.InvalidateSessionCache(user.Id);
        return (true, null);
    }

    /// <summary>
    /// 删除用户。保留最后一个启用中的管理员——否则会把自己或所有人锁在系统外面，
    /// 这是自建权限系统最常见、也最难恢复的运维事故。
    /// </summary>
    public async Task<(bool Ok, string? Error)> DeleteAsync(Guid id, Guid? currentUserId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return (false, "用户不存在");

        if (user.Id == currentUserId)
            return (false, "不能删除当前登录的账号");

        if (user.Role == UserRole.Admin && user.IsActive)
        {
            var otherActiveAdmins = await _db.Users.CountAsync(
                u => u.Id != id && u.Role == UserRole.Admin && u.IsActive, ct);
            if (otherActiveAdmins == 0)
                return (false, "系统必须保留至少一个启用中的管理员");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>密码强度校验：长度 + 至少两类字符</summary>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "密码不能为空";
        if (password.Length < MinPasswordLength)
            return $"密码长度不能少于 {MinPasswordLength} 位";

        var categories = 0;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;
        return categories < 2 ? "密码需至少包含大写字母、小写字母、数字、符号中的两类" : null;
    }

    /// <summary>
    /// 规范化邮箱：空白一律存 null，不要出现「空串」和「null」两种"没有邮箱"的表示。
    /// 格式合法性由端点先行拦下（<see cref="Modules.Users.UserApiExtensions.ValidateEmail"/>），
    /// 这里不再重复判断——同一件事两个地方各判一次，早晚会分叉。
    /// </summary>
    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private static UserViewDto ToView(User user) => new(
        user.Id, user.Username, user.DisplayName, user.Email,
        user.Role, PermissionCatalog.DisplayName(user.Role), user.IsActive,
        user.LastLoginAt, user.LastLoginIp, user.CreatedAt, user.UpdatedAt,
        user.SsoProvider);
}
