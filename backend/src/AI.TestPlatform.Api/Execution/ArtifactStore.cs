using System.Collections.Concurrent;
using System.Net.Http;
using Minio;
using Minio.DataModel;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 测试产物（截图 / 视觉基线 / trace 包）的存储抽象。
///
/// key 约定（与对外 URL 一一对应，URL 形态保持存量兼容）：
/// - 截图：  <c>{executionId:N}/{step:000}-{token}.png</c> → URL /screenshots/{key}
/// - 基线：  <c>_baselines/{testCaseId:N}/{step:000}.png</c> → URL /screenshots/{key}
/// - trace：<c>traces/{executionId:N}.zip</c>（受权端点提供下载）
///
/// 双实现：<see cref="LocalArtifactStore"/>（本地磁盘，单机部署默认）与
/// <see cref="MinioArtifactStore"/>（MinIO/S3 对象存储，容器化部署）。
/// 配置 Storage:Provider = local | minio。
/// </summary>
public interface IArtifactStore
{
    Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct);
    /// <summary>读取产物；不存在返回 null</summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    /// <summary>打开只读流（trace 大文件流式下载用）；调用方负责释放</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    /// <summary>删除某前缀下全部对象（如删除执行记录时清空其截图）</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct);
    /// <summary>删除前缀下早于 cutoff 的对象，返回删除数量（保留策略用）</summary>
    Task<int> DeletePrefixOlderThanAsync(string prefix, DateTime cutoffUtc, CancellationToken ct);
    /// <summary>
    /// 根级保留清理：删除早于 cutoff 的对象但排除受保护前缀（_baselines 基线 / traces 受 trace 保留策略管理）。
    /// 截图 key 直接挂在根下（{executionId}/...），按时间批量清理由此实现。
    /// </summary>
    Task<int> DeleteRootOlderThanAsync(DateTime cutoffUtc, string[] excludePrefixes, CancellationToken ct);
}

/// <summary>
/// 本地磁盘实现：单机模式的默认行为。
/// 兼容历史落盘布局：截图/基线在 Screenshots:Path（默认 screenshots），
/// trace 在 Execution:TracePath（默认 traces）——本地模式下路径与旧版本完全一致，
/// 静态中间件 / 既有数据零迁移。
/// </summary>
public class LocalArtifactStore : IArtifactStore
{
    private readonly string _shotsRoot;
    private readonly string _traceRoot;
    private readonly ILogger<LocalArtifactStore> _logger;

    public LocalArtifactStore(IConfiguration configuration, IHostEnvironment environment,
        ILogger<LocalArtifactStore> logger)
    {
        _shotsRoot = Path.GetFullPath(configuration["Screenshots:Path"] ?? "screenshots",
            environment.ContentRootPath);
        _traceRoot = Path.GetFullPath(configuration["Execution:TracePath"] ?? "traces",
            environment.ContentRootPath);
        Directory.CreateDirectory(_shotsRoot);
        Directory.CreateDirectory(_traceRoot);
        _logger = logger;
    }

    private string PathOf(string key)
    {
        // trace 走独立根目录（与历史布局一致），其余落在截图根下
        var root = key.StartsWith("traces/", StringComparison.OrdinalIgnoreCase) ? _traceRoot : _shotsRoot;
        var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        // 防目录穿越：key 拼出的路径必须仍在对应根目录下
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"非法产物 key: {key}");
        return full;
    }

    public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct);
    }

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var path = PathOf(key);
            return Task.FromResult<byte[]?>(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取产物失败 {Key}", key);
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
        Task.FromResult(File.Exists(PathOf(key)));

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            var path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        // traces 前缀对应独立根目录本身；其它前缀落在截图根的子目录里
        var dir = prefix.StartsWith("traces", StringComparison.OrdinalIgnoreCase)
            ? _traceRoot
            : PathOf(prefix.TrimEnd('/'));
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixOlderThanAsync(string prefix, DateTime cutoffUtc, CancellationToken ct)
    {
        // traces 前缀对应独立根目录本身；其它前缀落在截图根的子目录里
        var dir = prefix.StartsWith("traces", StringComparison.OrdinalIgnoreCase)
            ? _traceRoot
            : PathOf(prefix.TrimEnd('/'));
        if (!Directory.Exists(dir)) return Task.FromResult(0);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoffUtc) continue;
                File.Delete(file);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Task.FromResult(removed);
    }

    public Task<int> DeleteRootOlderThanAsync(DateTime cutoffUtc, string[] excludePrefixes, CancellationToken ct)
    {
        var removed = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_shotsRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    // 统一成 / 分隔再匹配排除前缀（key 与 URL 同形）
                    var key = Path.GetRelativePath(_shotsRoot, file).Replace('\\', '/');
                    if (excludePrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.GetLastWriteTimeUtc(file) >= cutoffUtc) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "根级产物清理失败");
        }
        return Task.FromResult(removed);
    }
}

/// <summary>
/// MinIO / S3 对象存储实现。截图、基线、trace 全部进对象存储后，
/// 平台节点变成无状态——多实例部署 / 重建容器不再丢产物。
/// Bucket 不存在时自动创建（私有读写；对外仍经受权端点 / 回源中间件提供）。
/// </summary>
public class MinioArtifactStore : IArtifactStore
{
    // 注意用具体类型而非 IMinioClient：WithSSL 等流式配置方法只在 MinioClient 上
    private readonly MinioClient _client;
    private readonly string _bucket;
    private readonly ILogger<MinioArtifactStore> _logger;

    public MinioArtifactStore(IConfiguration configuration, ILogger<MinioArtifactStore> logger)
    {
        var endpoint = configuration["Storage:Minio:Endpoint"] ?? "localhost:9000";
        var accessKey = configuration["Storage:Minio:AccessKey"] ?? "minioadmin";
        var secretKey = configuration["Storage:Minio:SecretKey"] ?? "minioadmin";
        _bucket = configuration["Storage:Minio:Bucket"] ?? "aitest-artifacts";
        var secure = configuration["Storage:Minio:Secure"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        // 对象存储通常在内网/本机，绝不能走环境代理：部署机带 HTTP_PROXY 时，
        // SDK 默认 HttpClient 会把 localhost:9000 的请求发给代理，
        // 导致签名/连接阶段 NullReference（ConnectionException: Object reference not set）。
        var handler = new HttpClientHandler { UseProxy = false };
        _client = new MinioClient()
            .WithHttpClient(new HttpClient(handler), disposeHttpClient: true)
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey);
        if (secure) _client.WithSSL();
        _logger = logger;
    }

    /// <summary>确保 bucket 存在（启动时由 Program.cs 调用一次；失败不阻塞启动，写入时会再尝试）</summary>
    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        try
        {
            var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
            if (!exists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
                _logger.LogInformation("已创建产物 bucket {Bucket}", _bucket);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查/创建 bucket {Bucket} 失败（MinIO 不可用？）", _bucket);
        }
    }

    public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        try
        {
            await PutObjectAsync(key, content, contentType, ct);
        }
        catch (Minio.Exceptions.BucketNotFoundException)
        {
            // bucket 被外部删除等场景：重建后重试一次
            await EnsureBucketAsync(ct);
            await PutObjectAsync(key, content, contentType, ct);
        }
    }

    private async Task PutObjectAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        await using var stream = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket).WithObject(key)
            .WithStreamData(stream).WithObjectSize(content.Length)
            .WithContentType(contentType), ct);
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            byte[]? result = null;
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucket).WithObject(key)
                .WithCallbackStream(stream =>
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    result = ms.ToArray();
                }), ct);
            return result;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取产物失败 {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs().WithBucket(_bucket).WithObject(key), ct);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查产物存在性失败 {Key}", key);
            return false;
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        // SDK 的 GetObjectAsync 以回调流消费，不直接暴露可返回的 Stream；
        // 一次性读进内存再包一层（trace 单个几 MB，内存开销可接受）
        var bytes = await ReadAsync(key, ct);
        return bytes is null ? null : new MemoryStream(bytes);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(key), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除产物失败 {Key}", key);
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        try
        {
            foreach (var item in await ListItemsAsync(prefix, ct))
                await DeleteAsync(item.Key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "按前缀删除产物失败 {Prefix}", prefix);
        }
    }

    /// <summary>列出前缀下全部对象（ListObjects 是 IObservable 风格，收拢成 Task）</summary>
    private async Task<List<Minio.DataModel.Item>> ListItemsAsync(string prefix, CancellationToken ct)
    {
        var items = new List<Minio.DataModel.Item>();
        var tcs = new TaskCompletionSource();
        var subscription = _client.ListObjectsAsync(
                new ListObjectsArgs().WithBucket(_bucket).WithPrefix(prefix).WithRecursive(true), ct)
            .Subscribe(item => items.Add(item), _ => tcs.TrySetResult(), () => tcs.TrySetResult());
        try
        {
            await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            subscription.Dispose();
        }
        return items;
    }

    public async Task<int> DeletePrefixOlderThanAsync(string prefix, DateTime cutoffUtc, CancellationToken ct)
    {
        var removed = 0;
        try
        {
            foreach (var item in await ListItemsAsync(prefix, ct))
            {
                // Item.LastModified 是 ISO8601 字符串（S3 风格）
                if (!DateTime.TryParse(item.LastModified, out var modified) || modified >= cutoffUtc) continue;
                await DeleteAsync(item.Key, ct);
                removed++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "按前缀清理产物失败 {Prefix}", prefix);
        }
        return removed;
    }

    public async Task<int> DeleteRootOlderThanAsync(DateTime cutoffUtc, string[] excludePrefixes,
        CancellationToken ct)
    {
        var removed = 0;
        try
        {
            foreach (var item in await ListItemsAsync("", ct))
            {
                if (excludePrefixes.Any(p => item.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                if (!DateTime.TryParse(item.LastModified, out var modified) || modified >= cutoffUtc) continue;
                await DeleteAsync(item.Key, ct);
                removed++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "根级产物清理失败");
        }
        return removed;
    }
}
