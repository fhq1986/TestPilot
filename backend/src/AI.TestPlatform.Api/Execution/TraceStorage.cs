using Microsoft.Playwright;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// Playwright trace 存储（迭代 D）。
///
/// trace 是 Playwright 最有价值的排障产物：一个 zip 里含每步的 DOM 快照、网络请求/响应、
/// 控制台输出与截图，用 <c>npx playwright show-trace</c> 或 trace viewer 可逐步回放。
/// 之前失败只有一张最终截图 + 错误文本，接口类失败基本只能人工复现。
///
/// 存储策略：**默认只在失败时保留**。trace 带截图与快照，单个执行几 MB，
/// 全量保留会迅速撑爆磁盘，而成功的执行没有回放价值。
///
/// 对象 key：<c>traces/{executionId:N}.zip</c>。经 <see cref="IArtifactStore"/> 落地
/// （本地磁盘或 MinIO）。下载走受权端点 GET /api/executions/{id}/trace（安全审查 S1）。
/// </summary>
public class TraceStorage
{
    /// <summary>对象 key 前缀（local 实现下映射到 Execution:TracePath 目录）</summary>
    public const string Prefix = "traces/";
    /// <summary>本地 trace 目录：历史产物（对象存储启用前落盘的）兼容读取用</summary>
    private readonly string _legacyRoot;
    private readonly IArtifactStore _store;
    private readonly ILogger<TraceStorage> _logger;

    public TraceStorage(IArtifactStore store, IConfiguration configuration,
        IHostEnvironment environment, ILogger<TraceStorage> logger)
    {
        _store = store;
        _legacyRoot = Path.GetFullPath(configuration["Execution:TracePath"] ?? "traces",
            environment.ContentRootPath);
        _logger = logger;
    }

    public static string Key(Guid executionId) => $"{Prefix}{executionId:N}.zip";

    /// <summary>历史本地文件路径（对象存储启用前的落盘位置，兼容读取用）</summary>
    public string LegacyFilePath(Guid executionId) => Path.Combine(_legacyRoot, $"{executionId:N}.zip");

    /// <summary>trace 的对外 URL（受权端点，需登录且具备 ViewExecutions 权限）</summary>
    public static string Url(Guid executionId) => $"/api/executions/{executionId:N}/trace";

    /// <summary>停止记录并把 trace 落存储。返回文件大小（字节）；失败返回 null</summary>
    public async Task<long?> StopAndSaveAsync(ITracing tracing, Guid executionId)
    {
        // SDK 必须落一次临时文件（Playwright 只支持 zip 输出到路径），随后上传到对象存储
        var tmp = Path.Combine(Path.GetTempPath(), $"aitest-trace-{executionId:N}.zip");
        try
        {
            await tracing.StopAsync(new TracingStopOptions { Path = tmp });
            if (!new FileInfo(tmp).Exists) return null;
            if (new FileInfo(tmp).Length == 0)
            {
                // 空包说明没抓到任何内容（例如单步执行瞬间完成），留着只会误导用户
                new FileInfo(tmp).Delete();
                return null;
            }
            var bytes = await File.ReadAllBytesAsync(tmp);
            await _store.SaveAsync(Key(executionId), bytes, "application/zip", CancellationToken.None);
            return bytes.Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存 trace 失败 执行 {ExecutionId}", executionId);
            return null;
        }
        finally
        {
            TryDeleteLocal(tmp);
        }
    }

    /// <summary>丢弃正在记录的 trace（成功执行不保留时不落盘）</summary>
    public async Task DiscardAsync(ITracing tracing)
    {
        try
        {
            await tracing.StopAsync();
        }
        catch (Exception ex)
        {
            // 丢弃失败无所谓，但不要让异常影响执行结果落库
            _logger.LogDebug(ex, "丢弃 trace 时出错（无害）");
        }
    }

    public void Delete(Guid executionId) =>
        _ = _store.DeleteAsync(Key(executionId), CancellationToken.None);

    /// <summary>删除超出保留期的 trace，返回清理数量。由维护任务调用</summary>
    public Task<int> CleanupOlderThan(DateTime cutoffUtc) =>
        _store.DeletePrefixOlderThanAsync(Prefix, cutoffUtc, CancellationToken.None);

    private void TryDeleteLocal(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
