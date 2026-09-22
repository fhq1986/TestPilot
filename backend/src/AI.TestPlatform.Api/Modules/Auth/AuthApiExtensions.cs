using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Auth.Sso;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Modules.Auth;

public static class AuthApiExtensions
{
    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder group)
    {
        // 独立限流（安全审查 S4）：匿名端点按 IP 5 次/分钟（RateLimit:LoginPermitLimit 可调），
        // 与账号级失败锁定（AuthService）双保险，弱密码无法被持续爆破
        group.MapPost("/login", async (
            LoginRequest request,
            IAuthService authService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var result = await authService.LoginAsync(request.Username, request.Password, ip, ct);
            return result is null
                ? Results.Json(new { message = "用户名或密码错误，或账号已被停用" },
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(result);
        }).RequireRateLimiting("login");

        // ------------------------------ SSO 扫码登录
        // 所有 /sso 端点匿名可访问（与 /login 同级）：登录发生前还没有平台会话

        // 登录页启动时拉取已启用的 Provider，渲染「企业微信 / 钉钉」按钮
        group.MapGet("/sso/providers", async (SsoLoginService sso, CancellationToken ct) =>
        {
            var options = await sso.GetEffectiveOptionsAsync(ct);
            var providers = new[] { "wecom", "dingtalk", "oidc", "mock" }
                .Select(id => sso.ResolveProvider(id, options))
                .Where(p => p is not null)
                .Select(p => new SsoProviderInfo(
                    p!.Id, p.DisplayName, $"/api/auth/sso/{p.Id}/authorize"))
                .ToList();
            return Results.Ok(providers);
        });

        // 跳转到企业授权页（服务端生成 state 存缓存，回调必须原样带回）。
        // mode=bind 时回调到前端 /login/sso/bind，完成「已登录用户绑定企业身份」流程。
        group.MapGet("/sso/{provider}/authorize", async (
            string provider, SsoLoginService sso, string? mode, CancellationToken ct) =>
        {
            var options = await sso.GetEffectiveOptionsAsync(ct);
            var p = sso.ResolveProvider(provider, options);
            if (p is null)
                return Results.NotFound(new { message = $"登录方式 {provider} 未启用" });

            var m = mode == "bind" ? "bind" : "login";
            var state = sso.NewState(provider, m);
            // provider 必须编进回调 URL：企业授权完成后按 OAuth2 规范原样保留 redirect_uri 的
            // query（code/state 用 & 追加），前端回调页靠它确定调哪个 Provider 的 login/bind 接口——
            // 不带的话回调页永远报「回调参数不完整」。同一地址在换令牌时也要用（OIDC 严格校验）。
            var redirect = SsoLoginService.CallbackRedirectUrl(options, provider, m);
            return Results.Redirect(await p.BuildAuthorizeUrlAsync(redirect, state, ct), permanent: false);
        });

        // 企业授权回调：前端回调页拿 URL 上的 code+state 提交到这里换平台 JWT
        group.MapPost("/sso/{provider}/login", async (
            string provider,
            SsoLoginRequest request,
            SsoLoginService sso,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!sso.TryConsumeState(request.State, provider, out var stateMode) || stateMode != "login")
                return Results.Json(new { message = "登录状态已过期，请重新扫码" },
                    statusCode: StatusCodes.Status400BadRequest);

            var ip = http.Connection.RemoteIpAddress?.ToString();
            var (result, failure) = await sso.LoginAsync(provider, request.Code, request.State, ip, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.Json(new
                {
                    message = failure == SsoLoginFailure.InvalidCode
                        ? "授权码无效或已过期，请重新扫码"
                        : "该企业账号尚未绑定平台用户，请联系管理员或改用密码登录",
                }, statusCode: StatusCodes.Status401Unauthorized);
        });

        // 已登录用户扫码绑定自己的企业身份（个人自助，无需管理员）
        group.MapPost("/sso/{provider}/bind", async (
            string provider,
            SsoBindRequest request,
            SsoLoginService sso,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id is null)
                return Results.Unauthorized();

            var (ok, message) = await sso.BindIdentityAsync(provider, request.Code, request.State, currentUser.Id.Value, ct);
            return ok ? Results.Ok(new { message }) : Results.Json(new { message }, statusCode: 400);
        }).RequireAuthorization();

        // 取当前登录用户：前端刷新页面后校验会话有效性并拿到最新权限
        group.MapGet("/me", (ICurrentUser user) =>
        {
            if (!user.IsAuthenticated || user.Id is null)
                return Results.Unauthorized();

            var role = user.Role ?? UserRole.Viewer;
            var permissions = user.Permissions;
            return Results.Ok(new UserDto(
                user.Id.Value,
                user.Username ?? string.Empty,
                user.Username ?? string.Empty,
                role,
                PermissionCatalog.DisplayName(role),
                (int)permissions,
                PermissionCatalog.ExpandNames(permissions)));
        }).RequireAuthorization();

        return group;
    }
}

/// <summary>登录页 Provider 按钮（GET /sso/providers）</summary>
public sealed record SsoProviderInfo(string Id, string DisplayName, string AuthorizeUrl);

/// <summary>企业授权回调提交（POST /sso/{provider}/login）</summary>
public sealed record SsoLoginRequest(string Code, string State);

/// <summary>已登录用户绑定企业身份（POST /sso/{provider}/bind）</summary>
public sealed record SsoBindRequest(string Code, string State);
