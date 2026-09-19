using AI.TestPlatform.Api.Common;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 富文本（需求「说明」/ 缺陷「描述」）净化的单测。
///
/// 这类值得单独锁死，是因为它的失效**不会报错、也不会被日常操作发现**：
/// 净化规则一松，页面照常显示，只是某个访客的浏览器里多跑了一段脚本。
///
/// 尤其是「纯文本分支」和「HTML 分支」的判定：判据一旦放宽成"含 &lt; 就是 HTML"，
/// 纯文本里极常见的 `a &lt; b` 会被误送进 HTML 分支——轻则显示错乱，
/// 重则给注入口子。所以下面两个方向都各有测试。
/// </summary>
public class RichTextTests
{
    // ------------------------------ HTML 分支：净化

    [Fact]
    public void 脚本标签被剥掉()
    {
        var result = RichText.Normalize("<p>正常内容</p><script>alert(1)</script>");

        Assert.Contains("正常内容", result);
        Assert.DoesNotContain("script", result!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 事件处理属性被剥掉而图片本身保留()
    {
        var result = RichText.Normalize("<img src=\"/screenshots/uploads/a.png\" onerror=\"alert(1)\">");

        Assert.Contains("/screenshots/uploads/a.png", result!);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void 危险协议的链接被剥掉(string href)
    {
        var result = RichText.Normalize($"<p><a href=\"{href}\">点我</a></p>");

        Assert.DoesNotContain("javascript:", result!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void iframe_与_style_属性被剥掉()
    {
        var result = RichText.Normalize(
            "<p style=\"position:fixed;top:0\">x</p><iframe src=\"http://evil\"></iframe>");

        Assert.DoesNotContain("iframe", result!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 编辑器真会产出的标签与属性被保留()
    {
        var result = RichText.Normalize(
            "<h2>标题</h2><p><strong>粗</strong><em>斜</em></p>" +
            "<ul><li>一</li></ul><blockquote>引</blockquote><pre><code>x=1</code></pre>" +
            "<a href=\"https://example.com\" title=\"t\">链接</a>");

        Assert.Contains("<h2>标题</h2>", result!);
        Assert.Contains("<strong>粗</strong>", result);
        Assert.Contains("<li>一</li>", result);
        Assert.Contains("<blockquote>引</blockquote>", result);
        Assert.Contains("<pre><code>x=1</code></pre>", result);
        Assert.Contains("href=\"https://example.com\"", result);
    }

    /// <summary>
    /// **相对地址必须保住**。正文里插入的图片存的正是 `/screenshots/uploads/...`，
    /// 一旦被当成"无协议"剥掉，用户贴的图会全部消失——而且这个回归在服务端毫无痕迹。
    /// </summary>
    [Fact]
    public void 相对地址的图片不被剥掉()
    {
        var result = RichText.Normalize("<p><img src=\"/screenshots/uploads/2026-09/abc.png\" alt=\"图\"></p>");

        Assert.Contains("/screenshots/uploads/2026-09/abc.png", result!);
    }

    // ------------------------------ 纯文本分支：编码 + 转段落

    [Fact]
    public void 纯文本的换行转成段落与br()
    {
        Assert.Equal("<p>第一行<br>第二行</p>", RichText.Normalize("第一行\n第二行"));
        Assert.Equal("<p>段一</p><p>段二</p>", RichText.Normalize("段一\n\n段二"));
    }

    [Fact]
    public void 纯文本的尖括号与和号被编码而不是当标签解析()
    {
        var result = RichText.Normalize("条件 a < b 且 a & b");

        Assert.Equal("<p>条件 a &lt; b 且 a &amp; b</p>", result);
    }

    /// <summary>判据只认**已知标签名**。`<custom>` 不在其中，所以整段仍走纯文本分支并被编码——
    /// 这正是我们要的：用户就是在正文里写尖括号，不该被当成 HTML。
    /// </summary>
    [Fact]
    public void 未知标签名按纯文本处理()
    {
        Assert.Equal("<p>见 &lt;custom&gt; 标签</p>", RichText.Normalize("见 <custom> 标签"));
    }

    /// <summary>
    /// 回归：`a &lt; b` 这种**极常见的纯文本**曾被误判成 HTML，并因此丢内容、被二次编码。
    ///
    /// 原因是判据里的 `[^&gt;]*` 可以跨空白与换行一路吃下去：
    /// `条件 a &lt; b 且 a & b\n第二行：见 &lt;custom&gt;` 里，
    /// `&lt; b` 被当成开标签 `b`，属性部分一直吃到 `<custom>` 的 `&gt;` —— 于是整段进了 HTML 分支，
    /// 被当成"一个名为 b、属性乱七八糟的标签"解析掉，正文被吞、实体被二次转义。
    /// </summary>
    [Fact]
    public void 含小于号的纯文本不该被当成HTML()
    {
        const string input = "第一行：条件 a < b 且 a & b\n第二行：见 <custom> 标签\n\n第二段";

        Assert.Equal(
            "<p>第一行：条件 a &lt; b 且 a &amp; b<br>第二行：见 &lt;custom&gt; 标签</p><p>第二段</p>",
            RichText.Normalize(input));
    }

    /// <summary>同一段正文里既没有真标签、又只有一处 `&lt; x` 时也必须走纯文本分支</summary>
    [Theory]
    [InlineData("a < b")]
    [InlineData("a < i 表示斜体")]
    [InlineData("条件 a < s 且 b < u")]
    [InlineData("a <\nb")]
    public void 单独的尖括号不算标签(string input)
        => Assert.StartsWith("<p>", RichText.Normalize(input));

    /// <summary>已含实体的 HTML 再净化一次不应被二次编码（读侧会重复调用）</summary>
    [Fact]
    public void 含实体的HTML再净化不变()
        => Assert.Equal("<p>a &lt; b &amp; c</p>", RichText.Normalize("<p>a &lt; b &amp; c</p>"));

    /// <summary>
    /// 反向的关键性质：**只有 &lt;script&gt; 的纯文本不会走净化分支**（script 不在已知标签表里），
    /// 于是它被编码成可见文本。所以两个分支都不会漏掉这个 XSS 载荷。
    /// </summary>
    [Fact]
    public void 只含script的纯文本被编码而不是丢弃()
    {
        var result = RichText.Normalize("<script>alert(1)</script>");

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", result);
    }

    // ------------------------------ 边界与幂等

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空值原样返回而不是变成空串(string? value)
        => Assert.Equal(value, RichText.Normalize(value));

    /// <summary>
    /// 幂等是**读侧再净化一遍**的前提（见 RequirementService/DefectService 的 DTO 映射）。
    /// 不幂等的话，每次读都会给正文再包一层 &lt;p&gt;，正文会越来越长。
    /// </summary>
    [Theory]
    [InlineData("<p>a</p><ul><li>b</li></ul>")]
    [InlineData("纯文本\n换行")]
    [InlineData("a < b & c")]
    [InlineData("<script>alert(1)</script>")]
    public void 净化是幂等的(string input)
    {
        var once = RichText.Normalize(input);
        Assert.Equal(once, RichText.Normalize(once));
    }

    // ------------------------------ 反向：ToPlainText（给只吃纯文本的外部系统）

    [Fact]
    public void 富文本还原为纯文本时段落变回换行()
    {
        Assert.Equal("第一行\n第二行", RichText.ToPlainText("<p>第一行</p><p>第二行</p>"));
        Assert.Equal("a\nb", RichText.ToPlainText("<p>a<br>b</p>"));
        Assert.Equal("待办\n一\n二", RichText.ToPlainText("<p>待办</p><ul><li>一</li><li>二</li></ul>"));
    }

    [Fact]
    public void 还原纯文本时会解码实体()
        => Assert.Equal("a < b & c", RichText.ToPlainText("<p>a &lt; b &amp; c</p>"));

    [Fact]
    public void 还原对纯文本输入是原样返回()
        => Assert.Equal("就是纯文本", RichText.ToPlainText("就是纯文本"));

    [Fact]
    public void 还原空值是空串而不是null()
        => Assert.Equal(string.Empty, RichText.ToPlainText(null));
}
