using System.Diagnostics;
using System.Text.Json;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Audit;

/// <summary>
/// 审计端点过滤器：<c>WithAudit("Create", "Project")</c> 挂到写端点上。
///
/// 职责：
/// 1) 记录动作 / 资源 / 结果；
/// 2) 尽力从路由参数与响应体里提取「资源名称」，让日志不需要 JOIN 业务表也能读懂；
/// 3) 捕获请求体作为变更摘要（脱敏交给 <see cref="AuditLogService"/>）；
/// 4) 捕获响应结果作为返回摘要——只有请求内容时，分不清"保存失败"和"保存成功但前端没刷新"。
///
/// 过滤器**不吞异常**：业务抛错时先落一条失败记录再向上抛，交由全局异常处理器统一处理。
/// </summary>
public sealed class AuditFilter : IEndpointFilter
{
    private readonly ILogger<AuditFilter> _logger;

    public AuditFilter(ILogger<AuditFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        var audit = http.RequestServices.GetRequiredService<AuditLogService>();
        var sw = Stopwatch.StartNew();

        // 请求期读取审计元数据（工厂阶段读不到，见 AuditEndpointExtensions 注释）
        var metadata = http.GetEndpoint()?.Metadata.OfType<AuditMetadata>().FirstOrDefault();
        if (metadata is null)
            return await next(context);

        // 请求体已被全局缓冲中间件（Program.cs，S2）开启缓冲；
        // 但过滤器在模型绑定之后执行，绑定可能把流读到了末尾——先回到开头再读。
        // 若中间件未覆盖（如 chunked 无 Content-Length），这里兜底再开一次，
        // 读不到内容时行为与旧行为一致（Detail 为空）。
        string? body = null;
        if (metadata.CaptureBody && http.Request.ContentLength is > 0 and < 65536)
        {
            http.Request.EnableBuffering();
            if (http.Request.Body.CanSeek)
                http.Request.Body.Position = 0;
            using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            http.Request.Body.Position = 0;
        }

        string? resourceId = null;
        if (http.Request.RouteValues.TryGetValue("id", out var rawId) && rawId is string idText)
            resourceId = idText;

        // 响应体的序列化必须与真实响应同档，所以取框架自己那份 JsonOptions
        var jsonOptions = metadata.CaptureResponse
            ? http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions
            : null;

        try
        {
            var result = await next(context);
            sw.Stop();

            await audit.WriteAsync(new AuditEntry(
                user.Id, user.Username, user.Role?.ToString(),
                metadata.Action, metadata.ResourceType, resourceId, ExtractName(body),
                http.Request.Method, http.Request.Path.Value ?? string.Empty,
                ResolveStatusCode(result), ResolveSucceeded(result),
                body, http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString(), (int)sw.ElapsedMilliseconds,
                jsonOptions is null ? null : CaptureResponse(result, jsonOptions)),
                CancellationToken.None);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await audit.WriteAsync(new AuditEntry(
                user.Id, user.Username, user.Role?.ToString(),
                metadata.Action, metadata.ResourceType, resourceId, ExtractName(body),
                http.Request.Method, http.Request.Path.Value ?? string.Empty,
                500, false, body, http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString(), (int)sw.ElapsedMilliseconds),
                CancellationToken.None);
            _logger.LogWarning(ex, "审计：{Action} {ResourceType} 抛出异常", metadata.Action, metadata.ResourceType);
            throw;
        }
    }

    /// <summary>从响应对象里猜 HTTP 状态码（Minimal API 返回 IResult，无法直接读状态）</summary>
    private static int ResolveStatusCode(object? result) => result switch
    {
        IStatusCodeHttpResult { StatusCode: int code } => code,
        _ => 200,
    };

    private static bool ResolveSucceeded(object? result) =>
        ResolveStatusCode(result) is >= 200 and < 400;

    /// <summary>
    /// 采集响应结果摘要。
    ///
    /// **为什么能从 IResult 里取到值**：Minimal API 的处理器返回的是 IResult 对象，
    /// 真正的响应体是在**所有过滤器都返回之后**才由框架写出的——所以在过滤器里读
    /// <c>HttpContext.Response.Body</c> 只会读到空气（这也正是审计一开始没有响应内容的原因）。
    /// 但绝大多数结果（Ok / Created / BadRequest / NotFound 带值的那批）都实现了
    /// <see cref="IValueHttpResult"/>，把待序列化的值挂在 <c>Value</c> 上，这里取出来自己序列化即可。
    ///
    /// 用**框架自己那套 JsonOptions**（由调用方从 DI 取）而不是另建一份：
    /// 否则命名策略、枚举格式一旦不同，审计里记的报文就与客户端真实收到的对不上，
    /// 而审计内容唯一的价值就是"你看到的就是当时返回的"。
    ///
    /// 采不到就返回 null，绝不因为"记日志"把业务响应搞挂。
    /// </summary>
    private static string? CaptureResponse(object? result, JsonSerializerOptions options)
    {
        if (result is not IValueHttpResult { Value: not null } valueResult)
            return null;

        try
        {
            return JsonSerializer.Serialize(valueResult.Value, options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException
                                       or InvalidOperationException or NotImplementedException)
        {
            // 循环引用 / 无法解析运行时类型等。这里只吞"序列化"这一类异常，
            // 别把业务异常也吃掉——那是一层套一层的写法。
            return null;
        }
    }

    /// <summary>从请求体里抽一个「像名称」的字段作为资源名，纯粹为了日志可读性</summary>
    private static string? ExtractName(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            foreach (var key in new[] { "name", "username", "displayName", "title" })
            {
                if (doc.RootElement.TryGetProperty(key, out var value) &&
                    value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 非 JSON 请求体（如文件上传）无需提取
        }

        return null;
    }
}

public static class AuditEndpointExtensions
{
    /// <summary>
    /// 声明端点审计信息。默认捕获请求体作为变更摘要、捕获响应结果作为返回摘要。
    ///
    /// 两个约束与 <c>WithPermission</c> 同理：
    /// - 泛型/参数类型必须是 <c>RouteHandlerBuilder</c>（<c>AddEndpointFilterFactory</c> 是它的扩展方法，
    ///   约束到接口会静默不注册过滤器）；
    /// - 审计元数据在**请求期**从 <c>HttpContext.GetEndpoint().Metadata</c> 读取
    ///   （过滤器工厂拿不到 WithMetadata 写入的元数据）。
    ///
    /// <paramref name="captureResponse"/> 只在返回体巨大又无审计价值时才关
    /// （批量导出、报告聚合）——默认关会让审计重新变成"只有请求没有响应"。
    /// </summary>
    public static RouteHandlerBuilder WithAudit(this RouteHandlerBuilder builder, string action,
        string resourceType, bool captureBody = true, bool captureResponse = true)
    {
        builder.WithMetadata(new AuditMetadata(action, resourceType, captureBody, captureResponse));
        builder.AddEndpointFilterFactory((routeContext, next) =>
        {
            var logger = routeContext.ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<AuditFilter>();
            var filter = new AuditFilter(logger);
            return invocationContext => filter.InvokeAsync(invocationContext, next);
        });
        return builder;
    }
}
