using System.Security.Cryptography;
using System.Text;

namespace AI.TestPlatform.Application.AI;

/// <summary>
/// 元素描述的确定性哈希嵌入（256 维，L2 归一化）。
///
/// 为什么不用外部 embedding 服务：平台当前可用的 LLM（DeepSeek 对话接口）没有
/// embeddings 端点；自建本地向量模型又超出单机部署的负担。哈希嵌入是当下务实的选择：
/// - 特征 = 字符二元组 + 整词（复用 DescriptionMatcher 的归一化）；
/// - 特征哈希（feature hashing）投到 256 维，符号位决定正负，避免哈希碰撞单边偏置；
/// - **确定性**：同一段描述永远得到同一向量——嵌入不变性是缓存检索正确性的前提；
/// - **纯函数**：不落库、不调网，单测可以直接钉住「相似描述向量近、无关描述向量远」。
///
/// 它捕捉的仍是字面特征（n-gram），不是真语义。实测分布（EmbedEvalProbe）：
/// 无关描述对余弦 ≈ 0.000（区分度极好）；字面漂移对 0.33~1.0；
/// 但「短描述 ↔ 长描述」会被特征稀释（「登录」vs「请输入账号密码后点击登录」仅 0.33）。
/// 因此向量检索只以 0.30 为召回线，判定真语义（换词表达）仍需未来的真 embedding 服务——
/// 接入时只需要替换这一个类的实现，检索链路不动。
/// </summary>
public static class ElementEmbedder
{
    /// <summary>向量维度。512：把哈希碰撞概率压到 <2%（256 维时碰撞会把
    /// 「只差一个字」的短描述相似度从 ~0.75 砸到 ~0.56），pgvector 精确检索无需索引</summary>
    public const int Dimensions = 512;

    /// <summary>归一化沿用 DescriptionMatcher 的口径：全角转半角、去空白与标点、转小写</summary>
    public static float[] Embed(string? description)
    {
        var normalized = DescriptionMatcher.Normalize(description);
        var vector = new float[Dimensions];
        if (normalized.Length == 0) return vector;

        // ---- 特征集 1：单字。短描述（中文 UI 文案常见 2~6 字）只有 1~5 个 bigram，
        // 一个字不同就掉一半重叠；单字特征把"只差一个字"的描述拉回高相似区。
        // 权重 1 < bigram 的 2：单字承载的语义弱，只做缓冲不做主导。
        for (var i = 0; i < normalized.Length; i++)
            AddFeature(vector, normalized[i].ToString(), weight: 1f);

        // ---- 特征集 2：字符二元组（跨语种安全，中文按字成对）
        for (var i = 0; i + 1 < normalized.Length; i++)
            AddFeature(vector, normalized.AsSpan(i, 2).ToString(), weight: 2f);

        // ---- 特征集 3：整词。ASCII 连续段视作一个词——英文 UI 文案按词比对
        foreach (var word in SplitAsciiWords(normalized))
            if (word.Length >= 2)
                AddFeature(vector, "#" + word, weight: 2f);

        // L2 归一化：让余弦距离只反映方向（特征构成），不反映长度
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
            for (var i = 0; i < Dimensions; i++)
                vector[i] /= norm;
        return vector;
    }

    private static void AddFeature(float[] vector, string feature, float weight)
    {
        var (bucket, sign) = HashFeature(feature);
        vector[bucket] += weight * sign;
    }

    /// <summary>同一特征必须永远落到同一桶、同一符号（跨进程、跨平台稳定）</summary>
    private static (int Bucket, float Sign) HashFeature(string feature)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(feature));
        var bucket = BitConverter.ToUInt32(bytes, 0) % Dimensions;
        // 符号位来自另一个字节，与桶号解耦：碰撞时正负对冲，比单边累加的偏差小
        var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
        return ((int)bucket, sign);
    }

    private static IEnumerable<string> SplitAsciiWords(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWord = i < text.Length && text[i] is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (isWord && start < 0) start = i;
            else if (!isWord && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }
}
