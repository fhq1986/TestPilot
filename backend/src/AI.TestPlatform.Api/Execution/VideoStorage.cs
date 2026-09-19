using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 执行录像存储。
///
/// 与 <see cref="TraceStorage"/> 同一套取舍：**默认只在失败时保留**。
/// 录像比 trace 更大（一分钟 1280x720 大约几 MB~十几 MB），全量保留会迅速撑爆磁盘，
/// 而成功的执行没有人会去回放。
///
/// 为什么还要录像（已经有 trace + 截图）：trace 是"结构化回放"，适合查元素与网络；
/// 但很多失败是**时序与交互过程**问题（动画未完就点了、焦点跳走了、滚动把元素挡了），
/// 这类问题截图看不出来，trace 也要一步步点才能看清。录像一眼就能看明白。
///
/// 对象 key：<c>videos/{executionId:N}.webm</c>。
/// 注意 key **不带 <c>traces/</c> 前缀**，有自己的保留期（VideoRetentionDays，默认 30 天），
/// 由维护任务显式清理，并**已从截图清理的排除列表保护起来**——
/// 之前靠截图清理顺带覆盖（90 天），两个保留期口径不一致时谁先谁后全靠运气。
/// 清理文件的同时维护任务会把对应 Executions.VideoUrl 置空，
/// 否则老执行详情页会一直显示一个点开就 404 的播放器。
/// （富文本图片之所以要进排除列表，是因为它是正文内容，性质完全不同。）
///
/// 下载走受权端点 <c>GET /api/executions/{id}/video</c>，与 trace 一致，不进静态目录。
/// </summary>
public class VideoStorage
{
    /// <summary>对象 key 前缀</summary>
    public const string Prefix = "videos/";

    /// <summary>录像内容类型（Playwright 输出 webm）</summary>
    public const string ContentType = "video/webm";

    private readonly IArtifactStore _store;
    private readonly ILogger<VideoStorage> _logger;
    private readonly string _tempRoot;

    public VideoStorage(IArtifactStore store, IOptions<ExecutionOptions> options,
        IHostEnvironment environment, ILogger<VideoStorage> logger)
    {
        _store = store;
        _logger = logger;
        // 临时目录：Playwright 必须先落到真实文件，失败才搬到产物存储
        var configured = options.Value.VideoTempPath;
        _tempRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "aitest-video")
            : Path.GetFullPath(configured, environment.ContentRootPath);
    }

    public static string Key(Guid executionId) => $"{Prefix}{executionId:N}.webm";

    /// <summary>录像的对外 URL（受权端点，需登录且具备 ViewExecutions 权限）</summary>
    public static string Url(Guid executionId) => $"/api/executions/{executionId:N}/video";

    /// <summary>
    /// 本次录像的临时目录。**按执行 id 分开**：Playwright 用页面 GUID 命名视频文件，
    /// 但不同执行共用同一个目录仍有互相干扰的风险（清理时会误删别人正在写的文件）。
    /// </summary>
    public string TempDirFor(Guid executionId) => Path.Combine(_tempRoot, executionId.ToString("N"));

    /// <summary>
    /// 把 Playwright 落盘的录像搬进产物存储，返回字节数；没有文件或失败返回 null。
    /// </summary>
    /// <param name="maxBytes">超过该大小则丢弃并告警（防止异常长的执行把内存/存储打爆）</param>
    public async Task<long?> SaveFromAsync(string localPath, Guid executionId, long maxBytes)
    {
        try
        {
            var info = new FileInfo(localPath);
            if (!info.Exists || info.Length == 0)
            {
                // 空文件说明没录到内容（例如页面秒开秒关），留着只会让用户点开一片黑
                return null;
            }

            if (maxBytes > 0 && info.Length > maxBytes)
            {
                _logger.LogWarning(
                    "录像超过上限被丢弃：执行 {ExecutionId} 大小 {Size} 字节 > {Max} 字节",
                    executionId, info.Length, maxBytes);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(localPath);
            await _store.SaveAsync(Key(executionId), bytes, ContentType, CancellationToken.None);
            return bytes.Length;
        }
        catch (Exception ex)
        {
            // 录像只是增强能力，存不上不能影响执行结果落库
            _logger.LogWarning(ex, "保存执行录像失败 执行 {ExecutionId}", executionId);
            return null;
        }
    }

    /// <summary>删除某次执行的录像（删除执行记录时调用）</summary>
    public void Delete(Guid executionId) =>
        _ = _store.DeleteAsync(Key(executionId), CancellationToken.None);

    /// <summary>删除超出保留期的录像，返回清理数量。由维护任务调用</summary>
    public Task<int> CleanupOlderThan(DateTime cutoffUtc) =>
        _store.DeletePrefixOlderThanAsync(Prefix, cutoffUtc, CancellationToken.None);

    /// <summary>删掉本次执行的临时目录（保留或丢弃都要删，否则临时目录会越积越多）</summary>
    public void DeleteTemp(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "清理录像临时目录失败（无害）：{Dir}", dir);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "清理录像临时目录失败（无害）：{Dir}", dir);
        }
    }

    /// <summary>是否启用录像（开关 + 模式双重判断，与 trace 的语义保持一致）</summary>
    public static bool IsEnabled(ExecutionOptions options, out string mode)
    {
        mode = (options.VideoMode ?? "on-failure").Trim().ToLowerInvariant();
        return options.VideoEnabled && mode is not ("off" or "none");
    }
}
