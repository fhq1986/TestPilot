namespace AI.TestPlatform.Application.Executions;

/// <summary>
/// 浏览器目录：统一浏览器名称的归一化、展示与合法性判断。
///
/// 历史数据里用例的 Browser 可能是 chrome / Chrome / chrome-headless 等写法，
/// 统一映射到 Playwright 的三个引擎名（chromium / firefox / webkit）。
/// </summary>
public static class BrowserCatalog
{
    public const string Chromium = "chromium";
    public const string Firefox = "firefox";
    public const string Webkit = "webkit";

    /// <summary>默认浏览器</summary>
    public const string Default = Chromium;

    /// <summary>可选浏览器（顺序即前端下拉顺序）</summary>
    public static readonly IReadOnlyList<BrowserOption> All = new List<BrowserOption>
    {
        new(Chromium, "Chromium", "Blink 内核，对应 Chrome / Edge（默认）"),
        new(Firefox, "Firefox", "Gecko 内核（Mozilla）"),
        new(Webkit, "WebKit", "WebKit 内核，对应 Safari"),
    };

    /// <summary>别名表：归一化后的写法 → 引擎名</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["chromium"] = Chromium,
        ["chrome"] = Chromium,
        ["googlechrome"] = Chromium,
        ["chromeheadless"] = Chromium,
        ["headlesschrome"] = Chromium,
        ["edge"] = Chromium,
        ["msedge"] = Chromium,
        ["blink"] = Chromium,
        ["firefox"] = Firefox,
        ["ff"] = Firefox,
        ["gecko"] = Firefox,
        ["mozillafirefox"] = Firefox,
        ["webkit"] = Webkit,
        ["safari"] = Webkit,
        ["safaritechnologypreview"] = Webkit,
    };

    private static string Sanitize(string name) => name.Trim().ToLowerInvariant()
        .Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);

    /// <summary>
    /// 尝试归一化：空值视为默认浏览器且返回 true；无法识别的写法返回 false。
    /// </summary>
    public static bool TryNormalize(string? name, out string normalized)
    {
        normalized = Default;
        if (string.IsNullOrWhiteSpace(name)) return true;
        return Aliases.TryGetValue(Sanitize(name), out normalized!);
    }

    /// <summary>归一化为引擎名；无法识别或为空时回落到默认浏览器</summary>
    public static string Normalize(string? name) => TryNormalize(name, out var value) ? value : Default;

    /// <summary>判断是否是可识别的浏览器名（用于请求校验；空值表示不指定）</summary>
    public static bool IsRecognized(string? name) => TryNormalize(name, out _);

    /// <summary>展示名（用于界面与报告）</summary>
    public static string DisplayName(string? name) =>
        All.FirstOrDefault(b => b.Id == Normalize(name))?.Name ?? "Chromium";
}

public record BrowserOption(string Id, string Name, string Note);
