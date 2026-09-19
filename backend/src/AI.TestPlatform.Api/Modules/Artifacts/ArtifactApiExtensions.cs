using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Modules.Artifacts;

/// <summary>
/// 富文本里插入的图片上传（需求「说明」、缺陷「描述」）。
///
/// 复用既有的 <see cref="IArtifactStore"/>，不另起一套存储：
/// 产物对外只有 `/screenshots/{key}` 这一个通道，本地磁盘与 MinIO 两种实现都已经在它后面，
/// 再搞一份就等于要同时维护静态文件、鉴权、部署挂载、MinIO 四件事。
/// </summary>
public static class ArtifactApiExtensions
{
    /// <summary>
    /// 富文本图片的存储前缀。
    ///
    /// ⚠ **必须与截图清理策略的排除列表共用同一个常量**：产物 key 直接挂在根下，
    /// 而截图保留策略会按时间清理根级对象（默认 90 天）。
    /// 漏掉这个前缀，需求/缺陷正文里的图会在一段时间后被**静默删掉**——
    /// 表现为"以前贴的图打不开了"，而且很难联想到是清理任务干的。
    /// </summary>
    public const string RichTextPrefix = "uploads/";

    /// <summary>单张图上限。够贴截图，但不至于让人往库里灌大文件</summary>
    private const long MaxBytes = 5 * 1024 * 1024;

    public static RouteGroupBuilder MapArtifactApi(this RouteGroupBuilder group)
    {
        group.MapPost("/image", async (
            IFormFile file, IArtifactStore store, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "没有收到文件" });
            if (file.Length > MaxBytes)
                return Results.BadRequest(new { message = "图片不能超过 5 MB" });

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();

            // **按内容判断类型，不看 file.ContentType**：声明的类型随便写，
            // 一个可执行文件改名就能以 image/png 提交上来；而产物目录是对外可访问的，
            // 存进去等于托管了一个任意文件。只认这四种图片的文件头。
            var extension = SniffImageExtension(bytes);
            if (extension is null)
                return Results.BadRequest(new { message = "只支持 PNG / JPEG / GIF / WebP 图片" });

            var key = $"{RichTextPrefix}{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid():N}{extension}";
            await store.SaveAsync(key, bytes, ContentTypeFor(extension), ct);

            // URL 形态沿用 /screenshots/{key}（产物对外的唯一通道，路径名是历史遗留）
            return Results.Ok(new { url = $"/screenshots/{key}" });
        }).WithPermission(Permission.ManageTestCases)
          .WithAudit("UploadImage", "Artifact", captureBody: false)
          // 接口用 JWT 鉴权、不走 Cookie，因此不需要防伪令牌；
          // 不显式关掉的话，IFormFile 绑定会要求 antiforgery 而直接 400。
          .DisableAntiforgery()
          // 在读取之前就挡住超大请求，避免先把 1GB 缓冲进内存再判断
          .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxBytes + 4096));

        return group;
    }

    /// <summary>按文件头判断图片类型；不属于这四种就返回 null</summary>
    private static string? SniffImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ".gif";

        // RIFF....WEBP
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ".webp";

        return null;
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "image/webp",
    };
}
