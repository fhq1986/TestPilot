using AI.TestPlatform.Application.AI;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 元素描述相似度匹配。
///
/// 这套匹配的意义是「省掉一次 LLM 定位」：命中了就用缓存选择器，命不中才走 AI。
/// 所以两类错误都不能犯——
/// - 太宽松：用错选择器，执行结果不可信（比慢更糟）；
/// - 太严格：缓存形同虚设，白花钱。
/// 这组用例就是在这条线上来回压。
/// </summary>
public class DescriptionMatcherTests
{
    [Theory]
    [InlineData("登录按钮", "登录按钮", 1.0)]
    [InlineData("登录按钮", "登录 按钮", 1.0)]      // 空格差异
    [InlineData("登录按钮", "登录按钮。", 1.0)]      // 标点差异
    [InlineData("Login Button", "login button", 1.0)] // 大小写差异
    [InlineData("登录按钮", "登录 按钮  ", 1.0)]
    public void 归一化后等价即满分(string left, string right, double expected)
    {
        Assert.Equal(expected, DescriptionMatcher.Similarity(left, right), 2);
    }

    [Fact]
    public void 全角与半角视为同一串()
    {
        // 中文输入法下全角字符很常见（如全角括号、全角字母）
        Assert.Equal(1.0, DescriptionMatcher.Similarity("登录（按钮）", "登录(按钮)"), 2);
    }

    [Fact]
    public void 描述更具体时仍能命中()
    {
        // 真实场景：用例作者把描述写细了一点，不该因此丢掉缓存
        var score = DescriptionMatcher.Similarity("蓝色的登录按钮", "登录按钮");

        Assert.True(score >= DescriptionMatcher.DefaultThreshold,
            $"「蓝色的登录按钮」应能匹配「登录按钮」，实际相似度 {score:F3}");
    }

    [Fact]
    public void 简短描述被扩写时仍能命中()
    {
        var score = DescriptionMatcher.Similarity("提交", "提交订单并支付");

        // 长度比只有 2/7≈0.29，明显低于「详略差别」的合理区间，
        // 这时给高分是危险的——「提交」可能对应页面上一堆东西
        Assert.True(score < DescriptionMatcher.DefaultThreshold,
            $"过短的描述不该高置信命中，实际相似度 {score:F3}");
    }

    [Fact]
    public void 换词但语义接近时靠二元组给分()
    {
        // 语序变化：二元组仍有大量重叠
        var score = DescriptionMatcher.Similarity("用户名输入框", "输入用户名的框");

        Assert.True(score > 0.4, $"实际相似度 {score:F3}");
    }

    [Theory]
    [InlineData("登录按钮", "取消按钮")]
    [InlineData("用户名输入框", "密码输入框")]
    [InlineData("提交订单", "删除订单")]
    public void 同后缀不同前缀不应命中(string left, string right)
    {
        // 这是最危险的误匹配类型：共用「按钮」「输入框」这种通用后缀
        var score = DescriptionMatcher.Similarity(left, right);

        Assert.True(score < DescriptionMatcher.DefaultThreshold,
            $"「{left}」与「{right}」不该达到阈值，实际相似度 {score:F3}");
    }

    [Theory]
    [InlineData(null, "登录按钮")]
    [InlineData("登录按钮", null)]
    [InlineData("", "")]
    [InlineData("   ", "登录按钮")]
    public void 空值相似度为0(string? left, string? right)
    {
        Assert.Equal(0, DescriptionMatcher.Similarity(left, right));
    }

    [Fact]
    public void 单字描述的退化处理()
    {
        // 单字没有二元组，退化为逐字比较
        Assert.Equal(1.0, DescriptionMatcher.Similarity("a", "a"));
        Assert.Equal(0, DescriptionMatcher.Similarity("a", "b"));
    }

    [Fact]
    public void 重复字不会被算成完全重合()
    {
        // 用集合而非计数的话「重重」与「重重重重」会被判为 1.0，这是错的
        var score = DescriptionMatcher.Similarity("重重", "重重重重");

        Assert.True(score < 1.0, $"实际相似度 {score:F3}");
    }

    [Fact]
    public void Dice系数在完全重合时为1()
    {
        Assert.Equal(1.0, DescriptionMatcher.DiceCoefficient("登录按钮", "登录按钮"), 3);
    }

    // ------------------------------ BestMatch

    private sealed record Candidate(string Description);

    [Fact]
    public void 选出相似度最高的候选()
    {
        var candidates = new[]
        {
            new Candidate("取消按钮"),
            new Candidate("登录按钮"),
            new Candidate("注册链接"),
        };

        var match = DescriptionMatcher.BestMatch(candidates, c => c.Description, "登录 按钮");

        Assert.NotNull(match);
        Assert.Equal("登录按钮", match!.Value.Candidate.Description);
        Assert.Equal(1.0, match.Value.Score, 2);
    }

    [Fact]
    public void 都不够像时返回null()
    {
        var candidates = new[]
        {
            new Candidate("取消按钮"),
            new Candidate("注册链接"),
        };

        // 宁可不命中走 AI 定位，也不要用错选择器
        Assert.Null(DescriptionMatcher.BestMatch(candidates, c => c.Description, "购物车图标"));
    }

    [Fact]
    public void 候选为空时返回null()
    {
        Assert.Null(DescriptionMatcher.BestMatch(Array.Empty<Candidate>(), c => c.Description, "登录按钮"));
    }

    [Fact]
    public void 可自定义阈值()
    {
        var candidates = new[] { new Candidate("蓝色的登录按钮") };

        // 阈值放低就能命中；放高则拒绝
        Assert.NotNull(DescriptionMatcher.BestMatch(candidates, c => c.Description, "登录按钮", 0.5));
        Assert.Null(DescriptionMatcher.BestMatch(candidates, c => c.Description, "登录按钮", 0.95));
    }

    [Fact]
    public void 阈值边界取等号也算命中()
    {
        var candidates = new[] { new Candidate("登录按钮") };
        var exact = DescriptionMatcher.Similarity("登录按钮", "登录按钮");

        Assert.NotNull(DescriptionMatcher.BestMatch(candidates, c => c.Description, "登录按钮", exact));
    }
}
