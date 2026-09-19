namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 截图/基线对外服务中间件（替代原 /screenshots 静态目录）。
///
/// 产物已迁移到 IArtifactStore（本地磁盘或 MinIO），URL 形态保持 /screenshots/{key}：
/// - 对象存储里有 → 直接流式返回；
/// - miss → 回退本地历史目录（对象存储启用前落盘的存量截图，免迁移平滑过渡）。
///
/// 公开访问语义不变（公开分享页要展示），防枚举仍靠文件名里的执行级随机串（见 ScreenshotStorage）。
/// </summary>
public class ArtifactMiddleware(RequestDelegate next, IArtifactStore store,
    ILogger<ArtifactMiddleware> logger)
{
    private readonly string _legacyRoot = Path.GetFullPath("screenshots");

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/screenshots"))
        {
            await next(context);
            return;
        }

        // /screenshots/{key...}
        var key = path.Value!["/screenshots/".Length..].TrimStart('/');
        if (string.IsNullOrWhiteSpace(key) || key.Contains(".."))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            var bytes = await store.ReadAsync(key, context.RequestAborted);
            byte[]? legacyBytes = null;
            if (bytes is null)
            {
                var legacy = Path.GetFullPath(Path.Combine(_legacyRoot, key.Replace('/', Path.DirectorySeparatorChar)));
                if (legacy.StartsWith(_legacyRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(legacy))
                    bytes = legacyBytes = await File.ReadAllBytesAsync(legacy, context.RequestAborted);
            }

            if (bytes is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "image/png";
            // 截图基本不变，允许浏览器缓存，减少对象存储往返
            context.Response.Headers.CacheControl = "public, max-age=86400";
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端中断（图片未加载完就关页面），无需处理
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "提供产物失败 {Key}", key);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}
