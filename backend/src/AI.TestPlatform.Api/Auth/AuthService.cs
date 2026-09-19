using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace AI.TestPlatform.Api.Auth;

public interface IAuthService
{
    Task<AuthResult?> LoginAsync(string username, string password, string? ip, CancellationToken ct);

    /// <summary>
    /// 为已通过鉴别的用户（密码登录或 SSO 扫码登录）签发平台 JWT。
    /// 调用方负责先完成身份验证并更新 LastLoginAt 等审计字段。
    /// </summary>
    Task<AuthResult> IssueForAsync(User user, CancellationToken ct);

    /// <summary>
    /// 校验 token 声明的会话版本是否仍然有效（改角色 / 重置密码 / 停用后旧 token 立即失效）。
    /// 命中缓存时不查库，缓存窗口见 <see cref="AuthService.SessionCacheSeconds"/>。
    /// </summary>
    Task<bool> IsSessionValidAsync(Guid userId, int tokenVersion, CancellationToken ct);

    /// <summary>
    /// 立即清除某用户的会话缓存。
    ///
    /// 为什么必须有这个方法：会话校验结果会缓存 <see cref="AuthService.SessionCacheSeconds"/> 秒，
    /// 若只在改角色时自增 TokenVersion 而不清缓存，旧的「有效」判断会继续命中缓存，
    /// 导致**被停用的账号在最长 30 秒内仍可继续访问**——对「紧急停用某个账号」是安全缺口。
    /// 因此任何影响凭据/权限的写操作都必须调用它。
    /// </summary>
    void InvalidateSessionCache(Guid userId);
}

public class AuthService : IAuthService
{
    /// <summary>
    /// 会话校验结果缓存时长。权衡：越短越实时，越长越省库。
    /// 30 秒意味着「改角色 → 旧 token 失效」最坏有 30 秒延迟，对内部平台足够。
    /// </summary>
    public const int SessionCacheSeconds = 30;

    private readonly TestDbContext _db;
    private readonly JwtOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;

    public AuthService(TestDbContext db, JwtOptions options, IMemoryCache cache, ILogger<AuthService> logger)
    {
        _db = db;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AuthResult?> LoginAsync(string username, string password, string? ip, CancellationToken ct)
    {
        // 账号级失败锁定（安全审查 S4，与 /login 的 IP 限流双保险）：
        // 连续失败达阈值的账号在窗口期内直接拒绝，不管密码对不对——
        // 用 MemoryCache 而不是 DB：锁定是短期的防爆破手段，不需要持久化也不该被攻击者观察到
        var lockKey = LoginFailCacheKey(username);
        if (_cache.TryGetValue(lockKey, out int failCount) && failCount >= LoginFailLockThreshold)
            return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            // 失败（含用户名不存在）也计数：按提交的用户名分区，避免真实账号被旁路试探
            _cache.Set(lockKey, failCount + 1, TimeSpan.FromMinutes(LoginFailLockMinutes));
            return null;
        }

        // 停用账号：凭据正确也拒绝登录（与 token 失效机制配合，覆盖「已登录后被停用」的场景）
        if (!user.IsActive)
            return null;

        _cache.Remove(lockKey);
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ip;
        await _db.SaveChangesAsync(ct);

        // 登录成功后清掉旧的会话缓存，避免刚登录就因缓存里的旧版本号被踢
        _cache.Remove(SessionCacheKey(user.Id));

        return await IssueForAsync(user, ct);
    }

    /// <inheritdoc />
    public Task<AuthResult> IssueForAsync(User user, CancellationToken ct)
    {
        var permissions = PermissionCatalog.Of(user.Role);
        return Task.FromResult(new AuthResult(
            GenerateToken(user, permissions),
            new UserDto(user.Id, user.Username, user.DisplayName, user.Role,
                PermissionCatalog.DisplayName(user.Role), (int)permissions,
                PermissionCatalog.ExpandNames(permissions))));
    }

    public async Task<bool> IsSessionValidAsync(Guid userId, int tokenVersion, CancellationToken ct)
    {
        var key = SessionCacheKey(userId);
        if (_cache.TryGetValue(key, out SessionSnapshot? cached) && cached is not null)
            return cached.Version == tokenVersion && cached.IsActive;

        var state = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TokenVersion, u.IsActive })
            .FirstOrDefaultAsync(ct);

        // 用户已被删除：视为失效
        if (state is null)
            return false;

        _cache.Set(key, new SessionSnapshot(state.TokenVersion, state.IsActive),
            TimeSpan.FromSeconds(SessionCacheSeconds));

        return state.TokenVersion == tokenVersion && state.IsActive;
    }

    private static string SessionCacheKey(Guid userId) => $"auth:session:{userId:N}";

    // 登录失败锁定参数（安全审查 S4）：15 分钟内连续失败 5 次锁 15 分钟
    private const int LoginFailLockThreshold = 5;
    private const int LoginFailLockMinutes = 15;
    private static string LoginFailCacheKey(string username) => $"auth:login-fail:{username.Trim().ToLowerInvariant()}";

    public void InvalidateSessionCache(Guid userId) => _cache.Remove(SessionCacheKey(userId));

    private record SessionSnapshot(int Version, bool IsActive);

    private string GenerateToken(User user, Permission permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            // 角色与权限位图都进 token：鉴权路径零查库，配合 TokenVersion 保证「改角色即时生效」
            new(CurrentUser.RoleClaimType, user.Role.ToString()),
            new(CurrentUser.PermissionClaimType, ((int)permissions).ToString()),
            new(TokenVersionClaimType, user.TokenVersion.ToString()),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpireMinutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>会话版本声明的名字（与 <see cref="CurrentUser"/> 的解码逻辑对应）</summary>
    public const string TokenVersionClaimType = "ver";
}
