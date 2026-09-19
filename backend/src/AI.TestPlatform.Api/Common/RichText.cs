using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace AI.TestPlatform.Api.Common;

/// <summary>
/// 富文本（需求「说明」、缺陷「描述」）的统一入口：**净化 + 纯文本转 HTML**。
///
/// **为什么必须净化**：这两处内容由用户以 HTML 提交、又在**别人的**浏览器里渲染
/// （编辑器与 v-html）。不净化就是典型的**存储型 XSS**——能写的人（哪怕只是账号被盗）
/// 可以在所有能看到这个项目的人（包括只读访客）的浏览器里执行脚本。
///
/// **为什么放在服务端**：接口是公开可调的，前端净化拦不住直接调 API 的写入；
/// 而且净化只该有一处实现（项目规矩：同一判据不留第二份）。
///
/// **为什么写侧和读侧都要过一遍 Normalize**：
/// - 写侧：新数据一律以 HTML 落库，库里只有一种格式，渲染与导出的分支就只有一个；
/// - 读侧：库里还有**改版之前的历史纯文本**（`\n` 分隔、且从未被净化过）。
///   读侧过一遍，历史数据不必做数据迁移就能正确渲染（换行变 &lt;br&gt;），
///   同时也把那批「从未净化过、却马上要进 v-html」的老数据补上净化。
///   Normalize 对 HTML 输入是幂等的（只净化不再包装），所以读侧重复调用无副作用。
/// </summary>
public static class RichText
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    /// <summary>
    /// 判断"这是编辑器产出的 HTML 而不是纯文本"。
    ///
    /// 这个判据必须**同时**满足两点，少一个都会出事：
    ///
    /// 1. **只认已知标签名**。不能用"含 &lt; 就是 HTML"这种判据——纯文本里
    ///    `a &lt; b` 极常见，那样会被误判，该编码的没编码。
    ///
    /// 2. **必须是一个结构完整的标签**：属性部分不许出现 `&lt;` / `&gt;`，且最终要以 `&gt;` 收尾。
    ///
    ///    第 2 点是踩出来的：最初的写法是 `[^&gt;]*&gt;`，而它可以跨空白、跨换行一路吃下去。
    ///    于是 `第一行：条件 a &lt; b 且 a & b\n第二行：见 &lt;custom&gt; 标签` 里，
    ///    `&lt; b` 被认成开标签 `b`，属性一直吃到 `<custom>` 的 `&gt;` ——
    ///    整段被当成"一个叫 b、属性乱七八糟的标签"解析：正文被吞掉一部分，剩下的被二次转义。
    ///    表现是"用户输入的正文莫名少了一段"，而且只在同时出现尖括号和别的尖括号时才复现。
    /// </summary>
    private static readonly Regex HtmlTagPattern = new(
        @"<\s*/?\s*(a|b|br|blockquote|code|div|em|h[1-6]|hr|i|img|li|ol|p|pre|s|span|strong|u|ul)(\s[^<>]*)?\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // 白名单刻意收窄到编辑器真会产出的标签：script/style/iframe/on* 事件属性默认就被排除，
        // 这里再显式限定一遍，改动时也一眼看得出允许了什么。
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "strong", "b", "em", "i", "u", "s", "span", "a", "img",
                     "ul", "ol", "li", "h1", "h2", "h3", "h4", "blockquote", "code", "pre", "hr",
                 })
            sanitizer.AllowedTags.Add(tag);

        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "title", "target", "rel", "src", "alt", "width", "height" })
            sanitizer.AllowedAttributes.Add(attr);

        // 允许的协议。**相对地址（/screenshots/... 这种）不受这里影响**，会被保留——
        // 富文本里插入的图片正是相对地址，一旦被剥掉，正文里的图就全没了。
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
            sanitizer.AllowedSchemes.Add(scheme);

        return sanitizer;
    }

    /// <summary>
    /// 统一入口：HTML 走净化，纯文本编码后转 HTML 段落。空值原样返回（不把 null 变成空串）。
    ///
    /// **纯文本为什么必须编码**：转成 HTML 之后它就进了 v-html 的渲染路径，
    /// 文本里的 `&lt;`、`&amp;` 不编码会被当成标签/实体解析，轻则显示错乱，重则又开出一个 XSS 口子。
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return LooksLikeHtml(value) ? Sanitizer.Sanitize(value) : ToHtml(value);
    }

    /// <summary>
    /// 反向：把富文本还原成纯文本，给**只接受纯文本的消费方**用
    /// （例如 Jira api/2 的 description 字段）。段落还原为换行。
    /// </summary>
    public static string ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
        if (!LooksLikeHtml(value)) return value;

        var text = Regex.Replace(value, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|li|h[1-6]|blockquote|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);   // 丢掉剩余标签
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    private static bool LooksLikeHtml(string value) => HtmlTagPattern.IsMatch(value);

    /// <summary>纯文本 → HTML：空行分段、段内单个换行变 &lt;br&gt;</summary>
    private static string ToHtml(string plain)
    {
        var encoded = WebUtility.HtmlEncode(plain)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var sb = new StringBuilder();
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br>", paragraph)).Append("</p>");
            paragraph.Clear();
        }

        foreach (var line in encoded.Split('\n'))
        {
            if (line.Trim().Length == 0) Flush();
            else paragraph.Add(line);
        }
        Flush();

        return sb.ToString();
    }
}
