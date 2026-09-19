using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AI.TestPlatform.Api.Modules.Users;

// 用户管理端点（/api/users，仅管理员）与个人密码修改
public static class UserApiExtensions
{
    public static RouteGroupBuilder MapUserApi(this RouteGroupBuilder group)
    {
        // 分页 + 服务端筛选（之前是全量返回，账号一多就变成一次全表拉取）
        group.MapGet("/", async (
            UserService users,
            CancellationToken ct,
            [FromQuery] string? search = null,
            [FromQuery] UserRole? role = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
            Results.Ok(await users.ListAsync(search, role, page, pageSize, ct)))
            .WithPermission(Permission.ManageUsers);

        // 各角色人数：列表页概览徽标用，避免前端为此再拉一次全量
        group.MapGet("/role-counts", async (UserService users, CancellationToken ct) =>
            Results.Ok(await users.CountByRoleAsync(ct)))
            .WithPermission(Permission.ManageUsers);

        // 用户选项：供「项目负责人 / 测试负责人」下拉用。
        // 权限挂在 ManageProjects 上（安全审查 S6）——挂在 ViewProjects 时最低权限的
        // 只读访客也能拉全量用户列表；能打开项目表单的人必然有 ManageProjects，
        // 功能不受影响。支持关键字搜索、上限 500，避免无界返回。
        group.MapGet("/options", async (
            UserService users,
            CancellationToken ct,
            [FromQuery] string? search = null) =>
            Results.Ok(await users.ListOptionsAsync(search, ct)))
            .WithPermission(Permission.ManageProjects);

        group.MapGet("/{id:guid}", async (Guid id, UserService users, CancellationToken ct) =>
        {
            var user = await users.GetAsync(id, ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).WithPermission(Permission.ManageUsers);

        group.MapPost("/", async (
            CreateUserRequest request,
            UserService users,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            var trimmed = request with { Username = request.Username?.Trim() ?? string.Empty };
            if (string.IsNullOrWhiteSpace(trimmed.Username))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["username"] = ["用户名不能为空"],
                });
            if (trimmed.Username.Length > UserService.MaxUsernameLength)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["username"] = [$"用户名长度不能超过 {UserService.MaxUsernameLength} 位"],
                });

            var passwordError = UserService.ValidatePassword(request.Password);
            if (passwordError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = [passwordError],
                });

            var emailError = ValidateEmail(request.Email);
            if (emailError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["email"] = [emailError],
                });

            var created = await users.CreateAsync(trimmed, current.Id, ct);
            return created is null
                ? Results.Json(new { message = $"用户名「{trimmed.Username}」已存在" },
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Created($"/api/users/{created.Id}", created);
        }).WithPermission(Permission.ManageUsers).WithAudit("Create", "User");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            UserService users,
            CancellationToken ct) =>
        {
            var emailError = ValidateEmail(request.Email);
            if (emailError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["email"] = [emailError],
                });

            var updated = await users.UpdateAsync(id, request, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithPermission(Permission.ManageUsers).WithAudit("Update", "User");

        group.MapPost("/{id:guid}/reset-password", async (
            Guid id,
            ResetPasswordRequest request,
            UserService users,
            CancellationToken ct) =>
        {
            var passwordError = UserService.ValidatePassword(request.NewPassword);
            if (passwordError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [passwordError],
                });

            var ok = await users.ResetPasswordAsync(id, request.NewPassword, ct);
            return ok
                ? Results.Ok(new { message = "密码已重置，该用户的其它登录会话已失效" })
                : Results.NotFound();
        }).WithPermission(Permission.ManageUsers).WithAudit("ResetPassword", "User");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            UserService users,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            var (ok, error) = await users.DeleteAsync(id, current.Id, ct);
            if (ok)
                return Results.NoContent();
            return error == "用户不存在"
                ? Results.NotFound()
                : Results.Json(new { message = error }, statusCode: StatusCodes.Status400BadRequest);
        }).WithPermission(Permission.ManageUsers).WithAudit("Delete", "User");

        // 修改本人密码：任何登录用户都可访问（不需要 ManageUsers）
        group.MapPost("/me/password", async (
            ChangeOwnPasswordRequest request,
            UserService users,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            if (current.Id is null)
                return Results.Unauthorized();

            var passwordError = UserService.ValidatePassword(request.NewPassword);
            if (passwordError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [passwordError],
                });

            var (ok, error) = await users.ChangeOwnPasswordAsync(current.Id.Value, request, ct);
            return ok
                ? Results.Ok(new { message = "密码已修改，请重新登录" })
                : Results.Json(new { message = error }, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization();

        // 本人 SSO 绑定状态（个人中心「账号绑定」卡片用，任何登录用户可查自己）
        group.MapGet("/me/sso", async (
            UserService users,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            if (current.Id is null)
                return Results.Unauthorized();

            var me = await users.GetAsync(current.Id.Value, ct);
            return me is null
                ? Results.Unauthorized()
                : Results.Ok(new { ssoProvider = me.SsoProvider });
        }).RequireAuthorization();

        // 角色与权限矩阵（供前端展示「三种角色分别能做什么」）
        group.MapGet("/roles", () => Results.Ok(BuildRoleMatrix()))
            .WithPermission(Permission.ManageUsers);

        return group;
    }

    /// <summary>邮箱可选：留空允许（下发验收邮件时会跳过），填了就必须是合法裸地址</summary>
    public static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        if (email.Trim().Length > 200) return "邮箱不能超过 200 个字符";
        return EmailAddress.IsValid(email) ? null : "邮箱格式不正确";
    }

    /// <summary>角色 → 权限矩阵视图，前端设置页直接渲染成表格</summary>
    public static IReadOnlyList<RoleMatrixDto> BuildRoleMatrix() =>
        Enum.GetValues<UserRole>().Select(role =>
        {
            var permissions = PermissionCatalog.Of(role);
            return new RoleMatrixDto(
                role.ToString(),
                PermissionCatalog.DisplayName(role),
                (int)permissions,
                PermissionCatalog.ExpandNames(permissions),
                Enum.GetValues<Permission>()
                    .Where(p => p != Permission.None)
                    .ToDictionary(p => p.ToString(), p => permissions.HasFlag(p)));
        }).ToList();

    public record RoleMatrixDto(
        string Role, string RoleName, int Permissions,
        IReadOnlyList<string> PermissionNames,
        Dictionary<string, bool> Matrix);
}
