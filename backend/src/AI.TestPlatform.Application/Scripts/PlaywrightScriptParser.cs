using System.Text;
using System.Text.RegularExpressions;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Scripts;

/// <summary>
/// Playwright 脚本解析：把 codegen / 手写的 Playwright 脚本转成平台的步骤模型。
///
/// 解析采用「结构化切分」而不是正则硬匹配链式调用：
/// 表达式结尾必是 <c>…)</c>，从右往左找到与之配对的 <c>(</c>，
/// 前面紧跟的标识符就是方法名，再往前必须是 <c>.</c>，剩下的就是调用目标——
/// 这样 <c>page.locator('x').click()</c> 能被正确切成目标 <c>page.locator('x')</c> + 方法 <c>click</c>。
///
/// 定位策略（与平台的 AI 自愈能力配合）：
/// - <c>locator('#id' | '.cls' | 'css=…' | 'xpath=…')</c> → 直接作为 css/xpath 选择器（最精确）；
/// - <c>getByRole/getByText/getByLabel/getByPlaceholder</c> 等语义定位 → 转成 <c>type = ai</c> + 中文描述，
///   运行时由 AI 定位与自愈兜底（这些 API 无法反推出稳定的 CSS）；
/// - <c>getByTestId</c> → <c>[data-testid="…"]</c>；
/// - iframe（frameLocator）等暂不支持，记入告警并跳过。
/// </summary>
public static class PlaywrightScriptParser
{
    private static readonly Regex LiteralRegex = new(
        // 注意：.NET 中「命名组的编号排在无名组之后」，因此这里一律用命名反向引用 \k<q>，
        // 避免 \1 / \2 指到错误的组（曾因此导致 locator(...) 全部匹配失败）
        """^(?<q>['"`])(?<value>(?:\\.|(?!\k<q>).)*)\k<q>$""", RegexOptions.Compiled);

    private static readonly Regex RoleNameRegex = new(
        @"name\s*:\s*(?<name>'(?:\\.|[^'])*'|""(?:\\.|[^""])*"")", RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"(?:describe|test|it)\s*\(\s*(?<q>['""])(?<name>(?:\\.|(?!\k<q>).)*)\k<q>", RegexOptions.Compiled);

    private static readonly Regex RequestRegex = new(
        @"\brequest\s*\.\s*(fetch|get|post|put|patch|delete)\s*\(", RegexOptions.IgnoreCase);

    /// <summary>角色 → 中文描述（用于 AI 定位描述）</summary>
    private static readonly Dictionary<string, string> RoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "按钮",
        ["link"] = "链接",
        ["textbox"] = "输入框",
        ["searchbox"] = "搜索框",
        ["checkbox"] = "复选框",
        ["radio"] = "单选框",
        ["combobox"] = "下拉框",
        ["listbox"] = "列表",
        ["option"] = "选项",
        ["heading"] = "标题",
        ["img"] = "图片",
        ["menuitem"] = "菜单项",
        ["menu"] = "菜单",
        ["tab"] = "标签页",
        ["switch"] = "开关",
        ["dialog"] = "对话框",
        ["alert"] = "提示框",
        ["table"] = "表格",
        ["row"] = "表格行",
        ["cell"] = "单元格",
        ["navigation"] = "导航",
        ["form"] = "表单",
        ["paragraph"] = "段落",
        ["listitem"] = "列表项",
    };

    public static ScriptParseResult Parse(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return Fail("脚本内容为空");

        var steps = new List<ParsedStepDto>();
        var warnings = new List<string>();
        string? suggestedName = null;
        string? suggestedBaseUrl = null;
        var isApi = false;

        var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // > 0 表示当前正处于某个多行配置块内部，块内所有行直接跳过
        var blockDepth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (blockDepth > 0)
            {
                blockDepth = Math.Max(0, blockDepth + BracketDelta(line));
                continue;
            }

            // describe / test 标题 → 用例名建议
            if (suggestedName is null)
            {
                var titleMatch = TitleRegex.Match(line);
                if (titleMatch.Success) suggestedName = Unescape(titleMatch.Groups["name"].Value);
            }

            // API 用例判定：request.fetch / request.post 等
            if (RequestRegex.IsMatch(line) && !line.Contains("page.", StringComparison.OrdinalIgnoreCase))
                isApi = true;

            if (IsIgnorableLine(line))
            {
                // 配置块起始行：记住未闭合深度，块内后续行整块跳过
                if (IsBlockPrefix(line))
                {
                    var delta = BracketDelta(line);
                    if (delta > 0) blockDepth = delta;
                }
                continue;
            }

            if (TryParseLine(line, out var actionType, out var config, out var note, out var skipReason))
            {
                var description = config!.Selector?.Description ?? config.Selector?.Value ?? config.Url;
                steps.Add(new ParsedStepDto(steps.Count, actionType, config, null, description, raw.Trim(), note));
                if (config.Url is { Length: > 0 } url &&
                    Uri.TryCreate(url, UriKind.Absolute, out var uri) && suggestedBaseUrl is null)
                    suggestedBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else if (skipReason is not null)
            {
                warnings.Add($"第 {i + 1} 行未映射：{skipReason}｜{Shorten(raw.Trim())}");
            }
        }

        if (steps.Count == 0)
            return Fail("没有解析出任何可执行的步骤（请确认粘贴的是 Playwright 脚本）",
                warnings, suggestedName, isApi);

        return new ScriptParseResult(true, null, steps, warnings, suggestedName,
            isApi ? TestType.Api : TestType.Web, suggestedBaseUrl);
    }

    private static ScriptParseResult Fail(string error, List<string>? warnings = null,
        string? name = null, bool isApi = false) =>
        new(false, error, new List<ParsedStepDto>(), warnings ?? new List<string>(), name,
            isApi ? TestType.Api : TestType.Web, null);

    // ---------------------------------------------------------------- 单行解析

    private static bool TryParseLine(string line, out ActionType actionType, out StepConfig? config,
        out string? note, out string? skipReason)
    {
        actionType = ActionType.Click;
        config = null;
        note = null;
        skipReason = null;

        var expression = StripAwait(line).TrimEnd(';').Trim();
        if (expression.Length == 0) return false;

        if (!TrySplitCall(expression, out var target, out var method, out var args))
        {
            skipReason = "无法识别的表达式";
            return false;
        }

        // expect(...).matcher(...)
        if (target.StartsWith("expect", StringComparison.Ordinal) && target.Length > 7 && target[6] == '(')
        {
            // 只去掉与 expect( 配对的那一个右括号：用 TrimEnd(')') 会连 locator('x') 的括号一起吃掉
            var inner = target[7..].TrimEnd();
            var subject = inner.EndsWith(')') ? inner[..^1].Trim() : inner;
            return TryParseAssertion(method, subject, args, out actionType, out config, out note, out skipReason);
        }

        // page.xxx(...)
        if (string.Equals(target, "page", StringComparison.Ordinal))
            return TryParsePageMethod(method, args, out actionType, out config, out note, out skipReason);

        // <locator>.method(...)
        return TryParseLocatorMethod(target, method, args, out actionType, out config, out note, out skipReason);
    }

    private static bool TryParseAssertion(string matcher, string subject, string args,
        out ActionType actionType, out StepConfig? config, out string? note, out string? skipReason)
    {
        actionType = ActionType.AssertVisible;
        config = null;
        note = null;
        skipReason = null;

        switch (matcher)
        {
            case "toHaveURL":
            {
                var expected = ExtractTextArgument(args);
                if (expected is null) { skipReason = "toHaveURL 需要字符串字面量"; return false; }
                actionType = ActionType.AssertUrl;
                config = new StepConfig { Value = expected };
                return true;
            }
            case "toHaveTitle":
            {
                var expected = ExtractTextArgument(args);
                if (expected is null) { skipReason = "toHaveTitle 需要字符串字面量"; return false; }
                actionType = ActionType.AssertTitle;
                config = new StepConfig { Value = expected };
                return true;
            }
            case "toBeVisible" or "toBeHidden" or "toBeEnabled" or "toBeDisabled" or "toBeChecked":
            {
                var selector = ParseLocator(subject);
                if (selector is null) { skipReason = $"{matcher} 的定位器不受支持"; return false; }
                if (matcher != "toBeVisible")
                    note = $"{matcher} 近似映射为 AssertVisible（平台暂只支持可见性断言）";
                actionType = ActionType.AssertVisible;
                config = new StepConfig { Selector = selector };
                return true;
            }
            case "toHaveText" or "toContainText" or "toHaveValue":
            {
                var expected = ExtractTextArgument(args);
                if (expected is null) { skipReason = $"{matcher} 需要字符串字面量"; return false; }
                var selector = ParseLocator(subject);
                if (selector is null) { skipReason = $"{matcher} 的定位器不受支持"; return false; }
                if (matcher != "toHaveText")
                    note = $"{matcher} 近似映射为 AssertText（文本包含断言）";
                actionType = ActionType.AssertText;
                config = new StepConfig { Selector = selector, Value = expected };
                return true;
            }
            default:
                skipReason = $"暂不支持断言 {matcher}";
                return false;
        }
    }

    private static bool TryParsePageMethod(string method, string args,
        out ActionType actionType, out StepConfig? config, out string? note, out string? skipReason)
    {
        actionType = ActionType.Wait;
        config = null;
        note = null;
        skipReason = null;

        switch (method)
        {
            case "goto":
                var url = FirstLiteral(args);
                if (url is null) { skipReason = "goto 需要字符串字面量 URL"; return false; }
                actionType = ActionType.Navigate;
                config = new StepConfig { Url = url };
                return true;

            case "waitForTimeout":
                actionType = ActionType.Wait;
                config = new StepConfig { Value = FirstLiteral(args) ?? FirstNumber(args) ?? "1000" };
                return true;

            case "waitForLoadState":
            case "waitForNavigation":
            case "waitForURL":
                actionType = ActionType.Wait;
                config = new StepConfig { Value = "1000" };
                note = $"{method} 近似映射为等待 1 秒";
                return true;

            case "screenshot":
                actionType = ActionType.Screenshot;
                config = new StepConfig();
                note = "平台每步自动截图，该步骤仅作标记";
                return true;

            case "click" or "dblclick" or "check" or "uncheck" or "hover" or "tap":
            {
                var selector = ParseLocatorFromArgs(args);
                if (selector is null) { skipReason = $"{method} 需要选择器字面量"; return false; }
                if (method is not ("click" or "dblclick")) note = $"{method} 近似映射为 Click";
                actionType = ActionType.Click;
                config = new StepConfig { Selector = selector };
                return true;
            }

            case "fill" or "type" or "pressSequentially":
            {
                var parts = SplitArguments(args);
                if (parts.Count < 2) { skipReason = $"{method} 需要选择器与输入值"; return false; }
                var selector = ParseLocatorFromArgs(parts[0]);
                if (selector is null) { skipReason = $"{method} 的选择器不受支持"; return false; }
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = FirstLiteral(parts[1]) ?? string.Empty };
                return true;
            }

            case "press":
            {
                var parts = SplitArguments(args);
                if (parts.Count < 2) { skipReason = "press 需要选择器与按键"; return false; }
                var selector = ParseLocatorFromArgs(parts[0]);
                if (selector is null) { skipReason = "press 的选择器不受支持"; return false; }
                var key = FirstLiteral(parts[1]) ?? string.Empty;
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = key };
                note = $"press('{key}') 近似映射为 Fill（需要按键语义时请手工调整）";
                return true;
            }

            case "selectOption":
            {
                var parts = SplitArguments(args);
                if (parts.Count < 2) { skipReason = "selectOption 需要选择器与选项值"; return false; }
                var selector = ParseLocatorFromArgs(parts[0]);
                if (selector is null) { skipReason = "selectOption 的选择器不受支持"; return false; }
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = FirstLiteral(parts[1]) ?? string.Empty };
                note = "selectOption 近似映射为 Fill（下拉选择暂按输入处理）";
                return true;
            }

            case "setViewportSize":
            case "setExtraHTTPHeaders":
            case "addInitScript":
            case "evaluate":
            case "addCookies":
            case "route":
            case "unroute":
            case "waitForEvent":
            case "on":
            case "close":
            case "reload":
            case "goBack":
            case "goForward":
                skipReason = $"{method} 属于环境/导航控制，平台暂不支持";
                return false;

            case "locator":
            case "$":
                skipReason = "单独的 locator 声明不产生步骤（其后的链式调用会被识别）";
                return false;

            default:
                skipReason = $"暂不支持的 page.{method} 调用";
                return false;
        }
    }

    private static bool TryParseLocatorMethod(string target, string method, string args,
        out ActionType actionType, out StepConfig? config, out string? note, out string? skipReason)
    {
        actionType = ActionType.Click;
        config = null;
        note = null;
        skipReason = null;

        var selector = ParseLocator(target);
        if (selector is null)
        {
            skipReason = $"不受支持的调用目标：{Shorten(target)}";
            return false;
        }

        switch (method)
        {
            case "click" or "dblclick" or "check" or "uncheck" or "hover" or "tap" or "focus":
                if (method is not ("click" or "dblclick")) note = $"{method} 近似映射为 Click";
                actionType = ActionType.Click;
                config = new StepConfig { Selector = selector };
                return true;

            case "fill" or "type" or "pressSequentially":
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = FirstLiteral(args) ?? string.Empty };
                return true;

            case "clear":
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = string.Empty };
                note = "clear 映射为 Fill 空值";
                return true;

            case "press":
            {
                var key = FirstLiteral(args) ?? string.Empty;
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = key };
                note = $"press('{key}') 近似映射为 Fill";
                return true;
            }

            case "selectOption":
                actionType = ActionType.Fill;
                config = new StepConfig { Selector = selector, Value = FirstLiteral(args) ?? string.Empty };
                note = "selectOption 近似映射为 Fill";
                return true;

            case "scrollIntoViewIfNeeded":
                actionType = ActionType.Scroll;
                config = new StepConfig { Selector = selector };
                return true;

            case "waitFor":
                actionType = ActionType.Wait;
                config = new StepConfig { Selector = selector };
                note = "waitFor 映射为等待元素可见";
                return true;

            case "screenshot":
            case "first":
            case "last":
            case "nth":
            case "filter":
            case "locator":
            case "getByRole":
            case "getByText":
                skipReason = $"{method} 不产生独立步骤（可忽略）";
                return false;

            default:
                skipReason = $"暂不支持的方法 {method}";
                return false;
        }
    }

    // ---------------------------------------------------------------- 表达式工具

    /// <summary>去掉开头的 await（保留 async 关键字用于跳过判断）</summary>
    private static string StripAwait(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("await", StringComparison.Ordinal)) return line;
        var rest = trimmed[5..];
        return rest.Length == 0 || char.IsWhiteSpace(rest[0]) ? rest.TrimStart() : line;
    }

    /// <summary>
    /// 结构化切分链式调用：返回目标、方法名与实参。
    /// 例：<c>page.locator('x').click()</c> → 目标 <c>page.locator('x')</c>、方法 <c>click</c>。
    /// </summary>
    public static bool TrySplitCall(string expression, out string target, out string method, out string args)
    {
        target = method = args = string.Empty;
        var text = expression.Trim();
        if (text.Length < 3 || !text.EndsWith(')')) return false;

        // 从右往左找与结尾 ) 配对的 (
        var depth = 0;
        var quote = '\0';
        var openIndex = -1;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (quote != '\0')
            {
                if (ch == quote && (i == 0 || text[i - 1] != '\\')) quote = '\0';
                continue;
            }
            if (ch is '\'' or '"' or '`')
            {
                quote = ch;
                continue;
            }
            if (ch == ')') depth++;
            else if (ch == '(')
            {
                depth--;
                if (depth == 0)
                {
                    openIndex = i;
                    break;
                }
            }
        }
        if (openIndex <= 0) return false;

        // 方法名紧贴在 ( 之前
        var end = openIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] is '_' or '$')) start--;
        if (start == end) return false;
        method = text[(start + 1)..(end + 1)];

        // 方法名之前必须是 "."，否则不是成员调用（如 test('x')）
        var dot = start;
        while (dot >= 0 && char.IsWhiteSpace(text[dot])) dot--;
        if (dot < 0 || text[dot] != '.') return false;
        target = text[..dot].Trim();
        args = text[(openIndex + 1)..^1];
        return target.Length > 0;
    }

    /// <summary>去掉行尾注释</summary>
    private static string StripComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        if (index < 0) return line;
        var prefix = line[..index];
        var single = prefix.Count(c => c == '\'');
        var dual = prefix.Count(c => c == '"');
        if (single % 2 == 1 || dual % 2 == 1) return line; // // 出现在字符串里
        return prefix;
    }

    /// <summary>与具体动作无关的脚手架行（import / 测试框架声明 / 大括号等）</summary>
    private static bool IsIgnorableLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return true;
        if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) return true;

        string[] prefixes =
        {
            "import ", "const ", "let ", "var ", "export ", "} from", "}from",
            "test(", "describe(", "it(", "async ", "test.use(", "test.beforeEach(",
            "test.afterEach(", "test.beforeAll(", "test.afterAll(", "test.describe(",
            "page.setDefaultTimeout", "use(",
        };
        if (prefixes.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal))) return true;

        // 纯括号/分号行
        return trimmed is "}" or "{" or "});" or "})" or ");" or ");";
    }

    /// <summary>
    /// 跨多行的**配置块**起始行（codegen 生成脚本时必插的样板）。
    /// 例：<c>test.use({ viewport: { width: 1280, height: 720 } })</c>。
    /// 这类块不是步骤，必须整块跳过——否则块内每一行（<c>height: 720,</c> 之类）都会
    /// 被当成失败解析，用户每次录制都会看到一串无意义的"未映射"告警。
    /// </summary>
    private static readonly string[] BlockPrefixes =
    {
        "test.use(", "use(", "test.describe.configure(", "test.slow(",
    };

    private static bool IsBlockPrefix(string line) =>
        BlockPrefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// 行内括号净值（开括号为正）。字符串字面量与转义字符内的括号不计入——
    /// 否则 <c>locator("a)b")</c> 这种会把深度算错。
    /// </summary>
    private static int BracketDelta(string line)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"' or '`') { quote = c; continue; }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
        }
        return depth;
    }

    private static string? FirstLiteral(string args)
    {
        foreach (var part in SplitArguments(args))
        {
            var literal = MatchLiteral(part.Trim());
            if (literal is not null) return literal;
        }
        return MatchLiteral(args.Trim());
    }

    /// <summary>匹配 /pattern/flags 形式的正则字面量</summary>
    private static readonly Regex RegexLiteral = new(
        @"^/(?<body>(?:\\.|[^/\\])*)/(?<flags>[a-z]*)$", RegexOptions.Compiled);

    /// <summary>
    /// 取第一个「文本型」实参：字符串字面量，或正则字面量（取内部内容并去掉 ^ $ 锚点）。
    /// Playwright 的断言常用正则，例如 expect(page).toHaveURL(/dashboard/)，直接按字面量处理会解析失败。
    /// </summary>
    private static string? ExtractTextArgument(string args)
    {
        foreach (var part in SplitArguments(args))
        {
            var token = part.Trim();
            var literal = MatchLiteral(token);
            if (literal is not null) return literal;

            var regex = RegexLiteral.Match(token);
            if (regex.Success)
            {
                var body = regex.Groups["body"].Value
                    .Replace("\\/", "/").Replace("\\", "\\");
                return body.TrimStart('^').TrimEnd('$');
            }
        }
        return null;
    }

    private static string? FirstNumber(string args)
    {
        var match = Regex.Match(args, @"\d+");
        return match.Success ? match.Value : null;
    }

    private static string? MatchLiteral(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var trimmed = token.Trim();
        // 带 .first()/.last() 等后缀的定位器不是字面量
        if (trimmed.Contains('(') && !trimmed.StartsWith('(')) return null;
        var match = LiteralRegex.Match(trimmed);
        return match.Success ? Unescape(match.Groups["value"].Value) : null;
    }

    private static string Unescape(string value) => value
        .Replace("\\n", "\n").Replace("\\t", "\t")
        .Replace("\\\"", "\"").Replace("\\'", "'")
        .Replace("\\\\", "\\");

    /// <summary>按顶层逗号切分实参（忽略字符串与括号内的逗号）</summary>
    public static List<string> SplitArguments(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args)) return result;

        var current = new StringBuilder();
        var depth = 0;
        char quote = '\0';
        foreach (var ch in args)
        {
            if (quote != '\0')
            {
                current.Append(ch);
                if (ch == quote) quote = '\0';
                continue;
            }
            switch (ch)
            {
                case '\'':
                case '"':
                case '`':
                    quote = ch;
                    current.Append(ch);
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    current.Append(ch);
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    current.Append(ch);
                    break;
                case ',' when depth == 0:
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }
        if (current.Length > 0) result.Add(current.ToString().Trim());
        return result;
    }

    /// <summary>把实参里的第一个定位器表达式解析成平台选择器</summary>
    private static SelectorConfig? ParseLocatorFromArgs(string args)
    {
        foreach (var part in SplitArguments(args))
        {
            var selector = ParseLocator(part);
            if (selector is not null) return selector;
        }
        return null;
    }

    /// <summary>
    /// 解析定位器表达式：locator / getByXxx / 裸字符串。
    /// 语义定位统一转成 type=ai + 中文描述，交给运行时的 AI 定位与自愈。
    /// </summary>
    public static SelectorConfig? ParseLocator(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var text = expression.Trim();
        if (text.Contains("frameLocator")) return null; // iframe 暂不支持

        var testId = Regex.Match(text, @"getByTestId\s*\(\s*(?<q>['""])(?<v>(?:\\.|(?!\k<q>).)*)\k<q>");
        if (testId.Success)
            return new SelectorConfig { Type = "css", Value = $"[data-testid=\"{Unescape(testId.Groups["v"].Value)}\"]" };

        var role = Regex.Match(text, @"getByRole\s*\(\s*(?<q>['""])(?<role>[^'""]*)\k<q>(?<rest>[^)]*)\)");
        if (role.Success)
        {
            var roleName = role.Groups["role"].Value;
            var nameMatch = RoleNameRegex.Match(role.Groups["rest"].Value);
            var name = nameMatch.Success ? Unescape(StripQuotes(nameMatch.Groups["name"].Value)) : null;
            var cn = RoleNames.TryGetValue(roleName, out var mapped) ? mapped : roleName;
            var description = string.IsNullOrWhiteSpace(name) ? cn : $"{cn}「{name}」";
            return new SelectorConfig { Type = "ai", Description = description };
        }

        var semantic = Regex.Match(text,
            @"getBy(?<kind>Text|Label|Placeholder|AltText|Title)\s*\(\s*(?<q>['""])(?<v>(?:\\.|(?!\k<q>).)*)\k<q>");
        if (semantic.Success)
        {
            var value = Unescape(semantic.Groups["v"].Value);
            var prefix = semantic.Groups["kind"].Value switch
            {
                "Text" => "文本",
                "Label" => "表单标签",
                "Placeholder" => "占位符",
                "AltText" => "图片替代文本",
                _ => "标题属性",
            };
            return new SelectorConfig { Type = "ai", Description = $"{prefix}「{value}」" };
        }

        var locator = Regex.Match(text, @"locator\s*\(\s*(?<arg>(?<q>['""`])(?:\\.|(?!\k<q>).)*\k<q>)\s*\)");
        if (locator.Success) return FromRawSelector(MatchLiteral(locator.Groups["arg"].Value));

        var literal = MatchLiteral(text);
        if (literal is not null) return FromRawSelector(literal);

        return null;
    }

    private static string StripQuotes(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '\'' || trimmed[0] == '"'))
            return trimmed[1..^1];
        return trimmed;
    }

    /// <summary>把 Playwright 的原始选择器字符串翻译成 css/xpath/ai</summary>
    public static SelectorConfig FromRawSelector(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new SelectorConfig { Type = "ai", Description = "未提供选择器" };
        var value = raw.Trim();

        if (value.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
            return new SelectorConfig { Type = "xpath", Value = value[6..] };
        if (value.StartsWith("//") || value.StartsWith("(/"))
            return new SelectorConfig { Type = "xpath", Value = value };
        if (value.StartsWith("css=", StringComparison.OrdinalIgnoreCase))
            return new SelectorConfig { Type = "css", Value = value[4..] };
        if (value.StartsWith("text=", StringComparison.OrdinalIgnoreCase))
            return new SelectorConfig { Type = "ai", Description = $"文本「{value[5..]}」" };
        if (value.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
            return new SelectorConfig { Type = "css", Value = $"#{value[3..]}" };
        if (value.StartsWith("data-testid=", StringComparison.OrdinalIgnoreCase))
            return new SelectorConfig { Type = "css", Value = $"[data-testid=\"{value[13..]}\"]" };

        return new SelectorConfig { Type = "css", Value = value };
    }

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
