using AI.TestPlatform.Application.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.AI;

/// <summary>语义向量化选项（appsettings 的 Embedding 节；默认关闭，零回归）。</summary>
public class EmbeddingOptions
{
    /// <summary>是否启用真语义 embedding（替代 ElementEmbedder 的确定性哈希）。默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>OpenAI 兼容 embeddings 端点（含 /v1）。留空则由 AIWorker 的 EMBEDDING_* 环境变量兜底。</summary>
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>模型名（如 text-embedding-3-small / text-embedding-ada-002 / bge 等）。</summary>
    public string Model { get; set; } = "text-embedding-3-small";

    /// <summary>向量维度，须与所选模型一致；启用时会据此调整 pgvector 列维度（必要时清空历史向量）。</summary>
    public int Dimensions { get; set; } = 1536;
}

public interface IEmbeddingProvider
{
    /// <summary>true = 走 AIWorker 真语义 embedding；false = 走 ElementEmbedder 确定性哈希。</summary>
    bool IsSemantic { get; }

    /// <summary>当前生效的向量维度（语义=配置值，哈希=512）。用于启动时的列维度对齐。</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}

/// <summary>
/// 元素描述向量化：默认走 <see cref="ElementEmbedder"/>（确定性哈希，512 维，零依赖、纯函数）；
/// 启用 <see cref="EmbeddingOptions.Enabled"/> 后改走 AIWorker 的 /api/embed（真语义，维度=EmbeddingOptions.Dimensions）。
///
/// 语义路径失败（Worker 不可达 / 配置缺失 / 熔断打开 / 维度不符）时**抛出异常**交由调用方降级——
/// 调用方据此跳过向量步骤（退回字符匹配 + 惰性回填），不会把错维哈希写进库造成列维度错配。
/// </summary>
public sealed class EmbeddingProvider : IEmbeddingProvider
{
    private readonly AIClient _ai;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<EmbeddingProvider> _logger;

    public EmbeddingProvider(AIClient ai, IOptions<EmbeddingOptions> options, ILogger<EmbeddingProvider> logger)
    {
        _ai = ai;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsSemantic => _options.Enabled;

    public int Dimensions => _options.Enabled ? _options.Dimensions : ElementEmbedder.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        if (!_options.Enabled)
            return ElementEmbedder.Embed(text);

        try
        {
            var cfg = new EmbeddingConfigDto(_options.BaseUrl, _options.ApiKey, _options.Model);
            var vector = await _ai.EmbedAsync(text, cfg, ct);
            if (vector.Length != _options.Dimensions)
                _logger.LogWarning(
                    "语义向量维度({Actual})与配置({Expected})不一致，向量检索可能失真，请核对 Embedding:Model/Dimensions",
                    vector.Length, _options.Dimensions);
            return vector;
        }
        catch (AIWorkerException ex)
        {
            _logger.LogWarning(ex, "语义向量化失败，调用方应降级为字符匹配：{Message}", ex.Message);
            throw;
        }
    }
}
