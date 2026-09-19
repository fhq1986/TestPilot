using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Auth;

public record LoginRequest(string Username, string Password);

/// <summary>登录返回的用户信息（含角色与展开后的权限名，供前端菜单/按钮渲染）</summary>
public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    UserRole Role,
    string RoleName,
    int Permissions,
    IReadOnlyList<string> PermissionNames);

public record AuthResult(string Token, UserDto User);
