using AI.TestPlatform.Application.AI;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 确定性哈希嵌入的性质验证。
/// 嵌入是缓存检索正确性的根基：这里钉住「确定性」「相似性」「无关性」三条性质，
/// 任何一条坏了，元素缓存就会开始用错选择器或彻底失效。
/// </summary>
public class ElementEmbedderTests
{
    [Fact]
    public void Embed_确定性_同一描述两次结果完全一致()
    {
        var a = ElementEmbedder.Embed("登录按钮");
        var b = ElementEmbedder.Embed("登录按钮");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Embed_维度正确且已L2归一化()
    {
        var v = ElementEmbedder.Embed("请输入用户名与密码后点击登录");
        Assert.Equal(ElementEmbedder.Dimensions, v.Length);

        var norm = MathF.Sqrt(v.Sum(x => x * x));
        // 空描述全零向量（模为 0，不能除），非空描述必须归一到 1
        Assert.Equal(1f, norm, 4f);
    }

    [Fact]
    public void Embed_空描述_返回零向量()
    {
        var v = ElementEmbedder.Embed("   ");
        Assert.All(v, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Embed_相似描述_向量距离近()
    {
        // 措辞漂移的三种典型：空白差异 / 错别字 / 更详细的长描述
        var pairs = new (string A, string B)[]
        {
            ("登录按钮", "登录 按钮"),
            ("登录按钮", "登陆按钮"),
            ("登录", "请输入账号密码后点击登录"),
        };

        // 阈值 0.30 = ElementCacheService.VectorThreshold 的实测标定值
        foreach (var (a, b) in pairs)
        {
            var similarity = CosineSimilarity(ElementEmbedder.Embed(a), ElementEmbedder.Embed(b));
            Assert.True(similarity >= 0.30f,
                $"「{a}」vs「{b}」相似度 {similarity:F2} 低于召回线 0.30");
        }
    }

    [Fact]
    public void Embed_无关描述_向量距离远()
    {
        var login = ElementEmbedder.Embed("登录按钮");
        var unrelated = new[] { "商品价格列表", "退出系统并清空购物车", "导出月度报表为Excel" };

        foreach (var other in unrelated)
        {
            var similarity = CosineSimilarity(login, ElementEmbedder.Embed(other));
            // 实测无关对 ≈ 0.000：必须与召回线 0.30 之间留出足够的安全边际
            Assert.True(similarity < 0.10f,
                $"「登录按钮」vs「{other}」相似度 {similarity:F2} 意外地高（>0.10）");
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
    }
}
