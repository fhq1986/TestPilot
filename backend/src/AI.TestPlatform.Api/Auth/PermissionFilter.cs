using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace AI.TestPlatform.Api.Auth;

/// <summary>
/// 权限校验端点过滤器：<c>WithPermission(Permission.ManageUsers)</c> 挂到端点上。
///
/// 为什么用端点过滤器而不是授权策略：
/// - 策略（<c>RequireAuthorization("policy")</c>）需要为每个权限点预先注册一条策略，
///   新增权限点要改两处（枚举 + 策略注册），容易漏；
/// - 端点过滤器一处声明即生效，并且能在拒绝时给出「缺哪个权限」的可读提示。
///
/// 注意：本过滤器只做「有没有权限」，不负责「有没有登录」。
/// 未登录由管线上更早的 <c>RequireAuthorization()</c> 拦成 401，两者职责分离。
/// </summary>
public sealed class PermissionFilter : IEndpointFilter
{
    private readonly ILogger<PermissionFilter> _logger;

    public PermissionFilter(ILogger<PermissionFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // 在**请求期**从当前端点读取权限声明。
        // 不能放在过滤器工厂（EndpointFilterFactoryContext）里读——那里只暴露 MethodInfo 与
        // ApplicationServices，拿不到 WithMetadata 写入的端点元数据（见 WithPermission 注释）。
        var required = context.HttpContext.GetEndpoint()?.Metadata
            .OfType<PermissionMetadata>()
            .Select(m => m.Required)
            .Aggregate(Permission.None, (acc, p) => acc | p) ?? Permission.None;

        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (user.Has(required))
            return await next(context);

        var username = user.Username ?? "(匿名)";
        _logger.LogWarning("权限拒绝：用户 {Username}（角色 {Role}）访问 {Path} 需要权限 {Required}",
            username, user.Role?.ToString() ?? "-", context.HttpContext.Request.Path,
            PermissionCatalog.Describe(required));

        return Results.Json(new
        {
            message = $"当前账号无权执行此操作（需要「{PermissionCatalog.Describe(required)}」权限）",
            required = required.ToString(),
            role = user.Role?.ToString(),
        }, statusCode: StatusCodes.Status403Forbidden);
    }
}

/// <summary>端点元数据：记录该端点需要的权限，供过滤器与自检测试读取</summary>
public sealed record PermissionMetadata(Permission Required);

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// 声明端点所需权限。
    ///
    /// 关键实现约束（真实踩坑记录，注释务必保留）：
    ///
    /// <b>权限值必须在**请求期**从 <c>HttpContext.GetEndpoint().Metadata</c> 读取，
    /// 不能在过滤器工厂里用反射从处理器 <c>MethodInfo</c> 上读。</b>
    /// <c>WithMetadata</c> 写入的值只存在于端点的 Metadata 集合中，不会变成 lambda 上的 CLR 特性；
    /// 而工厂拿到的 <c>EndpointFilterFactoryContext</c> 只暴露 <c>MethodInfo</c> 与
    /// <c>ApplicationServices</c>（没有 EndpointMetadata 属性），所以工厂阶段读到的权限恒为
    /// <see cref="Permission.None"/> → 过滤器永远放行 → 端点照常 200，权限静默失效。
    /// 这个缺陷编译器发现不了，单测也发现不了，只有端到端请求才暴露。
    ///
    /// 参数类型显式用 <see cref="RouteHandlerBuilder"/>（而非泛型约束到
    /// <c>IEndpointConventionBuilder</c>），以保证语义清晰、调用点类型可控。
    /// </summary>
    public static RouteHandlerBuilder WithPermission(this RouteHandlerBuilder builder, Permission permission)
    {
        if (permission == Permission.None)
            return builder; // 无需权限（仅要求登录）

        builder.WithMetadata(new PermissionMetadata(permission));
        builder.AddEndpointFilterFactory((routeContext, next) =>
        {
            var logger = routeContext.ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<PermissionFilter>();

            var filter = new PermissionFilter(logger);
            return invocationContext => filter.InvokeAsync(invocationContext, next);
        });
        return builder;
    }
}
