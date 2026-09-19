using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Playwright;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 截图存储：执行步骤截图（按执行 ID 分目录）与视觉基线（按用例 + 步骤分目录）。
///
/// 产物统一经 <see cref="IArtifactStore"/> 落地（本地磁盘或 MinIO 对象存储，按 Storage:Provider 配置），
/// 对外 URL 形态保持 <c>/screenshots/{key}</c> 不变——存量库里的 URL 不需要迁移。
///
/// 安全（审查报告 S1）：/screenshots 是公开分享页要展示的，不能整体加鉴权；
/// 防枚举靠**文件名里的执行级随机串**——目录是 GUID（不可猜），
/// 文件名原来是 001/002/003 完全可枚举，拿到任意一张截图 URL 就能把同一次执行的
/// 其余截图（含报告刻意没展示的步骤与视觉比对图）全部枚举出来。
/// 现在文件名形如 <c>001-a3f9c2d1.png</c>，随机串每次执行随机生成、进程内缓存复用。
/// </summary>
public class ScreenshotStorage
{
    public const string UrlPrefix = "/screenshots/";
    private const string BaselineFolder = "_baselines";

    private readonly IArtifactStore _store;
    /// <summary>本地截图目录：历史产物（对象存储启用前落盘的）兼容读取用</summary>
    private readonly string _legacyRoot;
    private readonly ILogger<ScreenshotStorage> _logger;

    /// <summary>执行级文件名随机串缓存（8 位 hex），同一次执行的所有产物共用</summary>
    private readonly ConcurrentDictionary<Guid, string> _executionTokens = new();

    public ScreenshotStorage(IArtifactStore store, IConfiguration configuration,
        IHostEnvironment environment, ILogger<ScreenshotStorage> logger)
    {
        _store = store;
        _legacyRoot = Path.GetFullPath(configuration["Screenshots:Path"] ?? "screenshots",
            environment.ContentRootPath);
        _logger = logger;
    }

    /// <summary>
    /// 执行级随机串（8 位 hex）。仅进程内缓存：同一执行的首图与差异图在同一次运行里生成，
    /// 必然命中同一串；进程重启后即使换了串，URL 已持久化在库里、按 URL 寻址不受影响。
    /// </summary>
    private string ExecutionToken(Guid executionId) =>
        _executionTokens.GetOrAdd(executionId,
            _ => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant());

    public async Task<string> SaveAsync(Guid executionId, int stepOrder, IPage page, CancellationToken ct)
    {
        var key = $"{executionId:N}/{stepOrder:000}-{ExecutionToken(executionId)}.png";
        // ScreenshotAsync 不带 Path 时返回字节，正好按"先得字节再落存储"的顺序走抽象
        var png = await page.ScreenshotAsync();
        await _store.SaveAsync(key, png, "image/png", ct);
        return UrlPrefix + key;
    }

    /// <summary>保存差异图（与步骤截图同目录，文件名带 -diff 后缀）</summary>
    public async Task<string> SaveDiffAsync(Guid executionId, int stepOrder, byte[] png, CancellationToken ct)
    {
        var key = $"{executionId:N}/{stepOrder:000}-{ExecutionToken(executionId)}-diff.png";
        await _store.SaveAsync(key, png, "image/png", ct);
        return UrlPrefix + key;
    }

    /// <summary>
    /// 基线图片的对象 key（目录概念在对象存储里即前缀）。
    /// browser 非空时追加后缀——基线按浏览器分存（字体渲染差异足以造成误报）；
    /// null 保持旧路径形状，历史基线无需迁移。
    /// </summary>
    public string BaselineKey(Guid testCaseId, int stepOrder, string? browser = null) =>
        $"{BaselineFolder}/{testCaseId:N}/{stepOrder:000}{BrowserSuffix(browser)}.png";

    /// <summary>基线图片的对外 URL</summary>
    public static string BaselineUrl(Guid testCaseId, int stepOrder, string? browser = null) =>
        $"{UrlPrefix}{BaselineFolder}/{testCaseId:N}/{stepOrder:000}{BrowserSuffix(browser)}.png";

    private static string BrowserSuffix(string? browser) =>
        string.IsNullOrWhiteSpace(browser) ? "" : $"-{browser.Trim().ToLowerInvariant()}";

    /// <summary>把 URL（/screenshots/...）解析为对象 key；不是本前缀的 URL 返回 null</summary>
    public static string? KeyOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase)
            ? url[UrlPrefix.Length..]
            : null;
    }

    /// <summary>读取图片为 base64（用于调用 AI Worker 做比对）；不存在返回 null。
    /// 对象存储 miss 时回退本地历史目录——对象存储启用前落盘的截图仍可读。</summary>
    public async Task<string?> ReadBase64Async(string? url)
    {
        var key = KeyOf(url);
        if (key is null) return null;
        try
        {
            var bytes = await _store.ReadAsync(key, ct: default);
            if (bytes is not null) return Convert.ToBase64String(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取截图失败 {Key}", key);
        }
        // 兼容：对象存储启用前的本地历史文件
        try
        {
            var legacy = Path.Combine(_legacyRoot, key.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(legacy)) return Convert.ToBase64String(await File.ReadAllBytesAsync(legacy));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取历史截图失败 {Key}", key);
        }
        return null;
    }

    /// <summary>按 URL 读取产物字节（PNG 尺寸解析等用）；不存在返回 null（含本地历史回退）</summary>
    public async Task<byte[]?> ReadBytesAsync(string? url, CancellationToken ct = default)
    {
        var key = KeyOf(url);
        if (key is null) return null;
        var bytes = await _store.ReadAsync(key, ct);
        if (bytes is not null) return bytes;
        try
        {
            var legacy = Path.Combine(_legacyRoot, key.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(legacy) ? await File.ReadAllBytesAsync(legacy, ct) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>把执行步骤截图复制为基线（读源 key → 写基线 key）。成功返回 true</summary>
    public async Task<bool> PromoteToBaselineAsync(Guid testCaseId, int stepOrder, string? screenshotUrl,
        string? browser = null, CancellationToken ct = default)
    {
        var sourceKey = KeyOf(screenshotUrl);
        if (sourceKey is null) return false;
        try
        {
            var bytes = await _store.ReadAsync(sourceKey, ct);
            // 源不在对象存储时回退本地历史文件
            if (bytes is null)
            {
                var legacy = Path.Combine(_legacyRoot, sourceKey.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(legacy)) return false;
                bytes = await File.ReadAllBytesAsync(legacy, ct);
            }
            await _store.SaveAsync(BaselineKey(testCaseId, stepOrder, browser), bytes, "image/png", ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入视觉基线失败 用例 {TestCaseId} 步骤 {StepOrder}", testCaseId, stepOrder);
            return false;
        }
    }

    /// <summary>按 URL 删除产物（基线删除用，尽力而为）。兼容历史本地路径</summary>
    public void DeleteByUrl(string? url)
    {
        var key = KeyOf(url);
        if (key is not null)
        {
            _ = _store.DeleteAsync(key, CancellationToken.None);
            return;
        }
        // 历史值：VisualBaseline.FilePath 存的是本地绝对路径
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var full = Path.GetFullPath(url);
            if (full.StartsWith(_legacyRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                File.Delete(full);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>删除某次执行的全部步骤截图（批量删除执行记录时调用，尽力而为）</summary>
    public void Delete(Guid executionId) =>
        // key 形态是 "{executionId:N}/000-xxx.png"（时间戳前缀），所以前缀删到 "{id}/" 为止。
        // 之前写成 "{id}/x" 永远匹配不到任何 key，批删执行的"连带删图"实际是空操作（审查发现）
        _ = _store.DeletePrefixOlderThanAsync(
            executionId.ToString("N") + "/", DateTime.UtcNow + TimeSpan.FromDays(1), CancellationToken.None);
}
