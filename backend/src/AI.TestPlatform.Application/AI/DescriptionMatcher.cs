using System.Globalization;
using System.Text;

namespace AI.TestPlatform.Application.AI;

/// <summary>
/// 元素描述相似度匹配（迭代 D）。
///
/// 背景：元素缓存原先用**精确字符串相等**查找描述，于是
/// 「登录按钮」与「登录 按钮」、「蓝色的登录按钮」与「登录按钮」都命中不到缓存，
/// 白白触发一次 LLM 定位（慢 + 花钱）。这类「同一元素的描述措辞略有出入」在真实用例里是常态。
///
/// 为什么不用向量检索：平台当前没有可用的 embedding 服务（LLM 走的是 DeepSeek 对话接口），
/// 硬上向量要么引入额外依赖、要么自建模型，收益与成本不成比例。
/// 字符 n-gram 相似度对中文短文本的效果已经很接近：中文以字为语义单位，
/// 二元组重叠度天然刻画了「这段话在说什么」。
///
/// 纯函数，无外部依赖，因此可以被完整单测覆盖。
/// </summary>
public static class DescriptionMatcher
{
    /// <summary>默认判定阈值。低于它宁可走 AI 定位，也不要用错选择器——用错比慢更糟</summary>
    public const double DefaultThreshold = 0.72;

    /// <summary>
    /// 归一化：全角转半角、转小写、去掉空白与标点。
    /// 目的是让「登录 按钮」「登录按钮」「登录按钮。」视为同一串。
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (char.IsPunctuation(ch) || char.IsSymbol(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 相似度（0~1）。综合「字面包含」与「字符二元组重叠」两个信号：
    /// - 包含关系：描述被写得更具体或更简略（最常见），直接给高分；
    /// - 二元组 Dice 系数：处理换词、语序变化。
    /// 取两者较大值，避免任一路径的短板拉低整体。
    /// </summary>
    public static double Similarity(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        // 包含关系：短串完全出现在长串里。按长度比例给分，避免「按钮」命中一切带「按钮」的描述
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            var shorter = Math.Min(a.Length, b.Length);
            var longer = Math.Max(a.Length, b.Length);
            var ratio = (double)shorter / longer;
            // 长度比 0.5 以上才算「同一元素的详略差别」，否则只是碰巧共用了一个词
            return ratio >= 0.5 ? 0.80 + 0.15 * ratio : ratio * 0.6;
        }

        return DiceCoefficient(a, b);
    }

    /// <summary>
    /// 字符二元组 Dice 系数：2|A∩B| / (|A|+|B|)。
    /// 单字文本退化为逐字比较。
    /// </summary>
    public static double DiceCoefficient(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2)
            return a == b ? 1 : 0;

        var left = Bigrams(a);
        var right = Bigrams(b);
        if (left.Count == 0 || right.Count == 0) return 0;

        var intersection = 0;
        foreach (var (gram, count) in left)
        {
            if (right.TryGetValue(gram, out var other))
                intersection += Math.Min(count, other);
        }

        var total = left.Values.Sum() + right.Values.Sum();
        return total == 0 ? 0 : 2.0 * intersection / total;
    }

    /// <summary>二元组计数。用计数字典而不是集合，重复字（「重重重重」）才不会被算成完全重合</summary>
    private static Dictionary<string, int> Bigrams(string value)
    {
        var grams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < value.Length; i++)
        {
            var gram = value.Substring(i, 2);
            grams[gram] = grams.TryGetValue(gram, out var n) ? n + 1 : 1;
        }
        return grams;
    }

    /// <summary>
    /// 从候选里挑最相似的一个。返回 null 表示都不够像——
    /// 这种情况必须老实返回 null 并走 AI 定位，用错选择器比多花一次 LLM 调用代价大得多。
    /// </summary>
    public static (T Candidate, double Score)? BestMatch<T>(
        IEnumerable<T> candidates, Func<T, string?> describe,
        string target, double threshold = DefaultThreshold)
    {
        T? best = default;
        var bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            var score = Similarity(describe(candidate), target);
            if (score <= bestScore) continue;
            bestScore = score;
            best = candidate;
        }

        if (best is null || bestScore < threshold) return null;
        return (best, bestScore);
    }
}
