using AI.TestPlatform.Application.Executions;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 用例级网络规则：解析、校验、规范化。
///
/// 这块值得单独锁住的理由是**失败发生在哪儿**：规则最终由 Playwright 的 route 消费，
/// 一个空 pattern 或越界状态码会在**执行期**才炸，用户拿到的是底层异常，
/// 根本看不出是哪条规则写错了。所以校验必须在这里挡住，并说清"第几条、哪个字段"。
/// </summary>
public class NetworkRuleSetTests
{
    // ------------------------------ 空值语义

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void 空值或空数组都表示不拦截(string? json)
        => Assert.Null(NetworkRuleSet.Parse(json));

    [Fact]
    public void 序列化空集合得到null()
    {
        Assert.Null(NetworkRuleSet.Serialize(null));
        Assert.Null(NetworkRuleSet.Serialize(Array.Empty<NetworkRule>()));
    }

    // ------------------------------ 正常解析

    [Fact]
    public void 解析返回构造响应规则()
    {
        var rules = NetworkRuleSet.Parse("""
            [{"pattern":"**/api/pay**","action":"fulfill","status":502,"contentType":"application/json","body":"{\"ok\":false}"}]
            """);

        var rule = Assert.Single(rules!);
        Assert.Equal("**/api/pay**", rule.Pattern);
        Assert.Equal(NetworkRuleAction.Fulfill, rule.Action);
        Assert.Equal(502, rule.Status);
        Assert.Equal("""{"ok":false}""", rule.Body);
    }

    /// <summary>
    /// 动作既接受可读字符串（用例作者手写），也接受数字（程序化写入 / 老数据）
    /// </summary>
    [Theory]
    [InlineData("""[{"pattern":"a","action":"abort"}]""", NetworkRuleAction.Abort)]
    [InlineData("""[{"pattern":"a","action":"delay","delayMs":800}]""", NetworkRuleAction.Delay)]
    [InlineData("""[{"pattern":"a","action":1}]""", NetworkRuleAction.Abort)]
    [InlineData("""[{"pattern":"a","action":2,"delayMs":100}]""", NetworkRuleAction.Delay)]
    public void 动作支持字符串与数字两种写法(string json, NetworkRuleAction expected)
        => Assert.Equal(expected, Assert.Single(NetworkRuleSet.Parse(json)!).Action);

    [Fact]
    public void 大小写不敏感的属性名也能解析()
        // Web 默认的 PropertyNameCaseInsensitive：库里若存过 PascalCase 也不至于读不出来
        => Assert.Single(NetworkRuleSet.Parse("""[{"Pattern":"a","Action":"abort"}]""")!);

    // ------------------------------ 非法值必须报错，且指明位置

    [Fact]
    public void 缺少pattern被拒()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse("""[{"pattern":"  ","action":"abort"}]"""));
        Assert.Contains("第 1 条", ex.Message);
        Assert.Contains("URL 匹配", ex.Message);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void 越界状态码被拒(int status)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse($$"""[{"pattern":"a","action":"fulfill","status":{{status}}}]"""));
        Assert.Contains("100~599", ex.Message);
    }

    /// <summary>
    /// abort 不产生响应：带了状态码说明作者理解错了，要提示而不是静默忽略
    /// </summary>
    [Fact]
    public void abort带状态码被拒()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse("""[{"pattern":"a","action":"abort","status":500}]"""));
        Assert.Contains("abort", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(600000)]
    public void 越界延迟被拒(int delayMs)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse($$"""[{"pattern":"a","action":"delay","delayMs":{{delayMs}}}]"""));
        Assert.Contains("延迟毫秒数", ex.Message);
    }

    /// <summary>
    /// 未定义的枚举数值必须被拦：JSON 里写 99 反序列化不会报错，
    /// 不校验的话到执行期就是一个"什么都不做"的规则——静默失效最难查
    /// </summary>
    [Fact]
    public void 未定义的动作数值被拒()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse("""[{"pattern":"a","action":99}]"""));
        Assert.Contains("动作无效", ex.Message);
    }

    [Fact]
    public void 非JSON被拒()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NetworkRuleSet.Parse("not json"));
        Assert.Contains("合法 JSON", ex.Message);
    }

    [Fact]
    public void 超过条数上限被拒()
    {
        var json = "[" + string.Join(",",
            Enumerable.Range(0, NetworkRuleSet.MaxRules + 1)
                .Select(i => $$"""{"pattern":"/p{{i}}","action":"abort"}""")) + "]";

        var ex = Assert.Throws<InvalidOperationException>(() => NetworkRuleSet.Parse(json));
        Assert.Contains("最多", ex.Message);
    }

    [Fact]
    public void 响应体过大被拒()
    {
        var huge = new string('x', NetworkRuleSet.MaxBodyChars + 1);
        var ex = Assert.Throws<InvalidOperationException>(
            () => NetworkRuleSet.Parse($$"""[{"pattern":"a","action":"fulfill","body":"{{huge}}"}]"""));
        Assert.Contains("响应体过长", ex.Message);
    }

    /// <summary>错误信息要说清是第几条——规则一多，"有一条写错了"等于没说</summary>
    [Fact]
    public void 错误信息定位到具体第几条()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NetworkRuleSet.Parse("""
            [{"pattern":"a","action":"abort"},{"pattern":"","action":"abort"}]
            """));
        Assert.Contains("第 2 条", ex.Message);
    }

    // ------------------------------ 规范化

    [Fact]
    public void 规范化是幂等的()
    {
        var normalized = NetworkRuleSet.Normalize("""
            [{"pattern":"**/api/a**","action":"fulfill","status":200,"body":"{}"}]
            """)!;

        Assert.Equal(normalized, NetworkRuleSet.Normalize(normalized));
    }

    [Fact]
    public void 规范化后是紧凑JSON且保留中文()
    {
        var normalized = NetworkRuleSet.Normalize("""
            [ { "pattern" : "**/api/a**" , "action" : "fulfill" , "body" : "{\"msg\":\"余额不足\"}" } ]
            """)!;

        // 不转义中文：查库排障时要能直接看懂响应体
        Assert.Contains("余额不足", normalized);
        Assert.DoesNotContain("\\u", normalized);
    }

    [Fact]
    public void 解析不了时规范化也会抛而不是静默返回空()
        // 端点靠这个异常回 400；若吞掉，非法规则会静默变成"没有规则"
        => Assert.Throws<InvalidOperationException>(() => NetworkRuleSet.Normalize("{bad json"));
}
