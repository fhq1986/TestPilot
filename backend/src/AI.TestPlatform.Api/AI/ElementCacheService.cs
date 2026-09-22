using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

public sealed record SelectorCacheEntry(string Type, string Value);

/// <summary>
/// 元素选择器缓存。
///
/// 查找分三步：
/// 1. 精确命中（描述与页面完全一致）—— 最常见，也最快；
/// 2. 向量近邻命中（pgvector 余弦距离）—— 同页面下嵌入向量最近且过阈值的条目；
/// 3. 字符模糊命中（<see cref="DescriptionMatcher"/>）—— 保底回退：
///    历史行尚未回填向量、或嵌入写入失败时仍然可用。
///
/// 第 2 步（2026-09-17）把镜像里闲置的 pgvector 真正用了起来。
/// 嵌入来自 <see cref="ElementEmbedder"/>（确定性哈希嵌入，不需要外部 embedding 服务）。
/// 与第 3 步的差异：字符相似度会被长度稀释（「登录」vs「请输入账号密码后点击登录」），
/// 词特征向量在换措辞场景下更稳；检索也下推到了数据库，不再全表拉回内存比对。
/// </summary>
public class ElementCacheService
{
    /// <summary>参与相似度评选的候选上限：同项目同页面的缓存条目通常是可枚举的量级</summary>
    private const int MaxCandidates = 500;

    /// <summary>
    /// 向量命中的相似度阈值（实测标定，见 ElementEmbedderTests.EmbedEvalProbe 数据）：
    /// 无关描述对稳定为 0.000，字面漂移对（错别字/加空格）0.33~1.0——0.30 是干净的分界。
    /// 刻意低于字符阈值（0.72）：哈希嵌入捕捉的是字面特征分布，不是真语义，
    /// 它的角色是"字符匹配漏掉的轻度漂移的第二道召回"，不是替代字符判定。
    /// </summary>
    private const double VectorThreshold = 0.30;

    /// <summary>语义（单位向量）近邻阈值：余弦相似度 >= 0.6 视为同一元素；哈希嵌入沿用 0.30。</summary>
    private const double SemanticVectorThreshold = 0.6;

    private readonly TestDbContext _db;
    private readonly IEmbeddingProvider _embedding;
    private readonly ILogger<ElementCacheService> _logger;

    public ElementCacheService(TestDbContext db, IEmbeddingProvider embedding, ILogger<ElementCacheService> logger)
    {
        _db = db;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<SelectorCacheEntry?> GetSelectorAsync(
        Guid projectId, string pageUrl, string description, CancellationToken ct)
    {
        var normalized = DomExtractor.NormalizePageUrl(pageUrl);

        var caches = await _db.AIElementCaches.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.PageUrl == normalized)
            // 历史命中最多的优先：同样的描述下，常用的那条更可能是对的选择器
            .OrderByDescending(e => e.MatchCount)
            .Take(MaxCandidates)
            .ToListAsync(ct);
        if (caches.Count == 0) return null;

        var exact = caches.FirstOrDefault(e => e.ElementDescription == description);
        if (exact is not null)
            return Latest(exact);

        // ---- 向量近邻：数据库侧按余弦距离排序取最近几条（只算已回填向量的行）
        // D4：向量由 IEmbeddingProvider 产生（语义或哈希）。向量化失败时退回字符匹配，不阻断本次命中。
        float[]? queryVec = null;
        try
        {
            queryVec = await _embedding.EmbedAsync(description, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "向量化失败，跳过向量近邻，退回字符匹配");
        }

        if (queryVec is not null)
        {
            var queryVector = new Vector(queryVec);
            var threshold = _embedding.IsSemantic ? SemanticVectorThreshold : VectorThreshold;
            var vectorCandidates = await _db.AIElementCaches.AsNoTracking()
                .Where(e => e.ProjectId == projectId && e.PageUrl == normalized
                            && e.ElementDescription != description && e.Embedding != null)
                .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
                .Take(5)
                .Select(e => new { e.Id, Distance = (double?)e.Embedding!.CosineDistance(queryVector) })
                .ToListAsync(ct);
            var nearest = vectorCandidates
                .Where(c => (1 - (c.Distance ?? 1)) >= threshold)
                .Select(c => caches.FirstOrDefault(x => x.Id == c.Id))
                .FirstOrDefault(c => c is not null);
            if (nearest is not null)
            {
                var similarity = 1 - vectorCandidates.First(c => c.Id == nearest.Id).Distance!.Value;
                _logger.LogInformation(
                    "元素缓存向量命中（相似度 {Score:F2}，{Kind}）：「{Target}」↔「{Cached}」，页面 {Page}",
                    similarity, _embedding.IsSemantic ? "语义" : "哈希", description, nearest.ElementDescription, normalized);
                return Latest(nearest);
            }
        }

        var fuzzy = DescriptionMatcher.BestMatch(caches, e => e.ElementDescription, description);
        if (fuzzy is null) return null;

        var entry = Latest(fuzzy.Value.Candidate);
        if (entry is null) return null;

        // 自愈要可观测：把「用了哪条缓存、有多像」记下来，
        // 否则测试团队只能看到「这次没走 AI」，却不知道依据是什么
        _logger.LogInformation(
            "元素缓存字符命中（相似度 {Score:F2}）：「{Target}」↔「{Cached}」，页面 {Page}",
            fuzzy.Value.Score, description, fuzzy.Value.Candidate.ElementDescription, normalized);

        // 惰性回填：字符命中的历史行若还没有向量，顺手补上——
        // 不专门写回填迁移（嵌入逻辑是 C# 代码，SQL 里做不了），让流量自然补齐。
        // D4：回填同样走 IEmbeddingProvider；向量化失败时跳过本条（下次命中再补）。
        if (fuzzy.Value.Candidate.Embedding is null)
        {
            try
            {
                var backfillVec = new Vector(await _embedding.EmbedAsync(fuzzy.Value.Candidate.ElementDescription, ct));
                await _db.AIElementCaches.Where(e => e.Id == fuzzy.Value.Candidate.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Embedding, backfillVec), ct);
            }
            catch (Exception ex)
            {
                // 回填失败不影响本次命中：向量只是加速，字符匹配已经拿到了正确结果
                _logger.LogDebug(ex, "回填元素嵌入向量失败（无害）：{Id}", fuzzy.Value.Candidate.Id);
            }
        }
        return entry;
    }

    private static SelectorCacheEntry? Latest(AIElementCache cache)
    {
        // 取历史里置信度最高的一条而不是最后一条：
        // 自愈失败过的那次也会写进历史，按时间取末条有可能取到更差的
        var best = cache.SelectorHistory
            .OrderByDescending(h => h.Confidence)
            .ThenByDescending(h => h.CreatedAt)
            .FirstOrDefault();
        return best is null ? null : new SelectorCacheEntry(best.Type, best.Value);
    }

    public async Task SaveAsync(Guid projectId, string pageUrl, string description,
        string selectorType, string selectorValue, float confidence, CancellationToken ct)
    {
        var normalized = DomExtractor.NormalizePageUrl(pageUrl);
        var cache = await _db.AIElementCaches
            .FirstOrDefaultAsync(e => e.ProjectId == projectId
                && e.PageUrl == normalized
                && e.ElementDescription == description, ct);
        if (cache is null)
        {
            cache = new AIElementCache
            {
                ProjectId = projectId,
                PageUrl = normalized,
                ElementDescription = description,
            };
            _db.AIElementCaches.Add(cache);
        }
        // 新增与更新都刷新向量：描述不变则重算结果相同（确定性），成本可忽略；
        // 描述被编辑过的行会拿到新向量，避免"文本改了向量还是旧的"这种静默漂移。
        // D4：向量由 IEmbeddingProvider 产生；向量化失败时该条不写向量（后续惰性回填），不阻断保存。
        try
        {
            cache.Embedding = new Vector(await _embedding.EmbedAsync(description, ct));
        }
        catch (Exception ex)
        {
            cache.Embedding = null;
            _logger.LogDebug(ex, "保存时向量化失败，该条不写向量（后续惰性回填）");
        }
        cache.SelectorHistory.Add(new SelectorHistoryEntry
        {
            Type = selectorType,
            Value = selectorValue,
            Confidence = confidence,
            CreatedAt = DateTime.UtcNow,
        });
        cache.LastMatchedAt = DateTime.UtcNow;
        cache.MatchCount++;
        await _db.SaveChangesAsync(ct);
    }
}
