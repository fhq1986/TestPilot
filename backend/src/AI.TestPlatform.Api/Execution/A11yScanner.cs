using System.Text.Json;
using Microsoft.Playwright;

namespace AI.TestPlatform.Api.Execution;

/// <summary>单条无障碍违规</summary>
public record A11yViolation(
    string Id, string Impact, string Help, string HelpUrl, int NodeCount, List<string> Targets);

/// <summary>无障碍扫描结果</summary>
public record A11yReport(int Total, int Minor, int Moderate, int Serious, int Critical, List<A11yViolation> Violations);

/// <summary>
/// 无障碍（WCAG）扫描（迭代 D）。
///
/// 用 axe-core 在页面里跑一遍规则集，把结果映射成平台可用的断言：
/// 这是一项「成本低、覆盖面广」的能力——不需要新的测试模型，
/// 却能把「按钮没有可访问名称」「表单没有 label」「图片缺 alt」这类问题挡住。
///
/// axe-core 以**内置资源**方式随程序发布（wwwroot 之外，走 Resources 目录复制到输出），
/// 不走 CDN：被测站点常常在内网、CI 也可能无外网，依赖 CDN 等于这个功能随时会失效。
/// </summary>
public class A11yScanner
{
    /// <summary>影响级别由轻到重。用于「只拦截某个级别以上」的阈值比较</summary>
    private static readonly string[] ImpactOrder = { "minor", "moderate", "serious", "critical" };

    private readonly ILogger<A11yScanner> _logger;
    private readonly string _axePath;

    public A11yScanner(ILogger<A11yScanner> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _axePath = Path.Combine(AppContext.BaseDirectory, "Resources", "axe.min.js");
    }

    /// <summary>axe-core 资源是否可用（缺失时给出明确提示，而不是抛一个难懂的 JS 异常）</summary>
    public bool IsAvailable => File.Exists(_axePath);

    /// <summary>
    /// 在页面上执行扫描。
    /// </summary>
    /// <param name="threshold">
    /// 最低拦截级别（config.value）：默认 serious。低于该级别的违规只记录不失败——
    /// 按 minor 拦截会让绝大多数真实站点直接不通过，反而没人看这个断言。
    /// 传 "none" 表示只报告不判定失败。
    /// </param>
    public async Task<(A11yReport Report, string? FailureMessage)> ScanAsync(
        IPage page, string? threshold, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new StepExecutionException(
                "axe-core 资源缺失（Resources/axe.min.js），无法执行无障碍扫描");

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = _axePath });

        // 只取需要断言的部分，避免把 axe 的完整结果（含 passes/incomplete）整个搬回来——
        // 那个 JSON 通常几百 KB，序列化开销比扫描本身还大
        var raw = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const result = await window.axe.run(document, {
                resultTypes: ['violations'],
                reporter: 'v2'
              });
              return {
                total: result.violations.length,
                violations: result.violations.map(v => ({
                  id: v.id,
                  impact: v.impact || 'minor',
                  help: v.help,
                  helpUrl: v.helpUrl,
                  nodeCount: v.nodes.length,
                  targets: v.nodes.slice(0, 3).map(n => (n.target || []).join(' '))
                }))
              };
            }
            """);

        var violations = new List<A11yViolation>();
        if (raw.TryGetProperty("violations", out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                violations.Add(new A11yViolation(
                    item.GetProperty("id").GetString() ?? "unknown",
                    item.GetProperty("impact").GetString() ?? "minor",
                    item.GetProperty("help").GetString() ?? string.Empty,
                    item.TryGetProperty("helpUrl", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                    item.GetProperty("nodeCount").GetInt32(),
                    item.TryGetProperty("targets", out var t)
                        ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                        : []));
            }
        }

        var report = new A11yReport(
            violations.Count,
            Count(violations, "minor"),
            Count(violations, "moderate"),
            Count(violations, "serious"),
            Count(violations, "critical"),
            violations);

        var failure = Judge(report, threshold);
        _logger.LogInformation(
            "无障碍扫描完成：违规 {Total} 条（严重 {Serious}、致命 {Critical}），判定阈值 {Threshold}",
            report.Total, report.Serious, report.Critical, NormalizeThreshold(threshold));

        return (report, failure);
    }

    /// <summary>
    /// 按阈值判定是否失败。返回 null 表示通过。
    /// 抽成公开静态方法是为了能单测——判定规则错了会让整个断言形同虚设或遍地误报。
    /// </summary>
    public static string? Judge(A11yReport report, string? threshold)
    {
        var level = NormalizeThreshold(threshold);
        if (level == "none") return null;

        var minIndex = Array.IndexOf(ImpactOrder, level);
        if (minIndex < 0) minIndex = Array.IndexOf(ImpactOrder, "serious");

        var blocking = report.Violations
            .Where(v => Array.IndexOf(ImpactOrder, v.Impact) >= minIndex)
            .OrderByDescending(v => Array.IndexOf(ImpactOrder, v.Impact))
            .ToList();
        if (blocking.Count == 0) return null;

        var details = string.Join("；", blocking.Take(5).Select(v =>
            $"[{LevelLabel(v.Impact)}] {v.Id}：{v.Help}（{v.NodeCount} 处，如 {string.Join(", ", v.Targets.Take(2))}）"));
        var more = blocking.Count > 5 ? $"；另有 {blocking.Count - 5} 条同类问题" : string.Empty;
        return $"无障碍扫描发现 {blocking.Count} 条「{LevelLabel(level)}」及以上违规：{details}{more}";
    }

    /// <summary>归一化阈值；非法值回落到 serious 而不是静默变成「不判定」</summary>
    public static string NormalizeThreshold(string? threshold)
    {
        if (string.IsNullOrWhiteSpace(threshold)) return "serious";
        var value = threshold.Trim().ToLowerInvariant();
        if (value is "none" or "off") return "none";
        return ImpactOrder.Contains(value) ? value : "serious";
    }

    private static string LevelLabel(string impact) => impact switch
    {
        "critical" => "致命",
        "serious" => "严重",
        "moderate" => "中等",
        "minor" => "轻微",
        _ => impact,
    };

    private static int Count(List<A11yViolation> violations, string impact) =>
        violations.Count(v => string.Equals(v.Impact, impact, StringComparison.OrdinalIgnoreCase));
}
