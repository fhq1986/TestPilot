using AI.TestPlatform.Api.Execution;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 无障碍扫描的阈值判定。
///
/// 判定规则是这个断言的成败关键：
/// - 太严（比如按 minor 拦截）→ 几乎所有真实站点都不通过，团队很快就不看这个断言了；
/// - 太松 → 断言形同虚设。
/// 因此这里既压「该拦的拦住」，也压「不该拦的放过」。
/// </summary>
public class A11yScannerJudgeTests
{
    private static A11yViolation Violation(string id, string impact, int nodes = 1) =>
        new(id, impact, $"{id} 的说明", $"https://example.com/{id}", nodes, ["#target"]);

    private static A11yReport Report(params A11yViolation[] violations) => new(
        violations.Length,
        violations.Count(v => v.Impact == "minor"),
        violations.Count(v => v.Impact == "moderate"),
        violations.Count(v => v.Impact == "serious"),
        violations.Count(v => v.Impact == "critical"),
        violations.ToList());

    [Fact]
    public void 没有违规时通过()
    {
        Assert.Null(A11yScanner.Judge(Report(), null));
    }

    [Fact]
    public void 默认阈值是serious()
    {
        Assert.Equal("serious", A11yScanner.NormalizeThreshold(null));
        Assert.Equal("serious", A11yScanner.NormalizeThreshold("  "));
    }

    [Fact]
    public void 默认只拦严重及以上()
    {
        // 只有轻微与中等 → 默认阈值下不该失败
        var minorOnly = Report(Violation("color-contrast", "minor"), Violation("region", "moderate"));
        Assert.Null(A11yScanner.Judge(minorOnly, null));

        // 出现严重 → 拦
        var withSerious = Report(Violation("label", "serious"));
        Assert.NotNull(A11yScanner.Judge(withSerious, null));
    }

    [Fact]
    public void 致命级别一定被拦()
    {
        Assert.NotNull(A11yScanner.Judge(Report(Violation("aria-hidden-focus", "critical")), null));
        // 即便阈值放到 critical，也仍然要拦
        Assert.NotNull(A11yScanner.Judge(
            Report(Violation("aria-hidden-focus", "critical")), "critical"));
    }

    [Fact]
    public void 阈值放宽到minor时轻微问题也会拦()
    {
        var report = Report(Violation("color-contrast", "minor"));

        Assert.Null(A11yScanner.Judge(report, "serious"));
        Assert.NotNull(A11yScanner.Judge(report, "minor"));
    }

    [Fact]
    public void 阈值收紧到critical时严重问题不再拦()
    {
        var report = Report(Violation("label", "serious"));

        Assert.NotNull(A11yScanner.Judge(report, "serious"));
        Assert.Null(A11yScanner.Judge(report, "critical"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("off")]
    [InlineData("NONE")]
    [InlineData(" Off ")]
    public void none表示只报告不判定(string threshold)
    {
        var report = Report(Violation("aria-hidden-focus", "critical"));

        Assert.Null(A11yScanner.Judge(report, threshold));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("严重")]
    [InlineData("7")]
    public void 非法阈值回落到serious而不是静默不判定(string threshold)
    {
        // 回落成 none 会让写错配置的人以为「全都通过了」，这是最坏的失败方式
        Assert.Equal("serious", A11yScanner.NormalizeThreshold(threshold));
        Assert.NotNull(A11yScanner.Judge(Report(Violation("label", "serious")), threshold));
    }

    [Fact]
    public void 失败信息里包含规则名与影响范围()
    {
        var report = Report(
            Violation("label", "serious", nodes: 3),
            Violation("color-contrast", "moderate", nodes: 12));

        var message = A11yScanner.Judge(report, "serious");

        Assert.NotNull(message);
        Assert.Contains("label", message);
        Assert.Contains("3 处", message);
        // 中等及以下的 color-contrast 不该出现在拦截信息里
        Assert.DoesNotContain("color-contrast", message);
    }

    [Fact]
    public void 违规很多时只列前几条并给出总数()
    {
        var report = Report(Enumerable.Range(0, 9)
            .Select(i => Violation($"rule-{i}", "serious"))
            .ToArray());

        var message = A11yScanner.Judge(report, "serious");

        Assert.NotNull(message);
        Assert.Contains("9 条", message);
        Assert.Contains("另有 4 条", message);
    }

    [Fact]
    public void 报告统计按级别分组()
    {
        var report = Report(
            Violation("a", "minor"),
            Violation("b", "moderate"),
            Violation("c", "serious"),
            Violation("d", "serious"),
            Violation("e", "critical"));

        Assert.Equal(5, report.Total);
        Assert.Equal(1, report.Minor);
        Assert.Equal(1, report.Moderate);
        Assert.Equal(2, report.Serious);
        Assert.Equal(1, report.Critical);
    }
}
