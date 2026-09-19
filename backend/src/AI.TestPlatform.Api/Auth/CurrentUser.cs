using System.Security.Claims;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Auth;

/// <summary>
/// 当前登录用户（从已通过校验的 JWT 里读，不查库）。
/// 权限位图由 JWT <c>perm</c> 声明携带，配合 <see cref="IAuthService"/> 的 TokenVersion 校验，
/// 角色变更后旧 token 会在 30 秒内失效，因此这里读到的权限是「近实时且可信」的。
/// </summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Username { get; }
    UserRole? Role { get; }
    Permission Permissions { get; }
    bool IsAuthenticated { get; }
    bool Has(Permission permission);
}

public sealed class CurrentUser : ICurrentUser
{
    public const string PermissionClaimType = "perm";

    /// <summary>
    /// 角色声明的名字。
    /// 刻意不用 "role"：JwtBearer 默认的 <c>TokenValidationParameters.RoleClaimType</c> 就是 "role"，
    /// 会把它当作角色声明转移到身份上，导致 <c>FindFirstValue("role")</c> 取到 null
    /// （表现为「权限正确但角色读成默认值」这种很难一眼看出的错位）。
    /// </summary>
    public const string RoleClaimType = "approle";

    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? Id
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Username => Principal?.FindFirstValue(ClaimTypes.Name);

    public UserRole? Role
    {
        get
        {
            var raw = Principal?.FindFirstValue(RoleClaimType);
            return Enum.TryParse<UserRole>(raw, out var role) ? role : null;
        }
    }

    public Permission Permissions =>
        PermissionCatalog.Parse(Principal?.FindFirstValue(PermissionClaimType));

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool Has(Permission permission) =>
        permission == Permission.None || PermissionCatalog.Has(Permissions, permission);
}
