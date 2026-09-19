using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 测试计划达标判定。
///
/// 这是整个计划模块里口径最容易出错的地方，也是最需要说清楚的：
/// - 分母是「总数 − Skipped」：FailFast 跳过的执行不代表没通过，算进分母会无端拉低通过率；
/// - 排除 flaky 时**分子分母一起扣**：既然认定样本不可信，就不该既不算它失败又拿它当分母；
/// - 判定必须能解释：只说 passed:false，CI 日志里就只能人肉翻报告。
/// 这组用例就是在这几条线上来回压。
/// </summary>
public class PlanGateEvaluatorTests
{
    private static PlanCaseOutcome Failure(string name, bool flaky = false,
        ExecutionStatus status = ExecutionStatus.Failed) =>
        new(Guid.NewGuid(), name, "订单", status, $"{name} 失败", flaky);

    private static PlanRoundRaw Round(
        int roundNo, int passed, int failed = 0, int error = 0, int skipped = 0,
        int flakyFailed = 0, int flakyError = 0,
        IReadOnlyList<PlanCaseOutcome>? blocking = null) =>
        new(roundNo, new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc), null,
            passed + failed + error + skipped, passed, failed, error, skipped,
            flakyFailed, flakyError, blocking ?? []);

    private static PlanGateResult Evaluate(PlanRoundRaw[] rounds,
        double target = 0.95, bool allowErrors = false, bool excludeFlaky = true,
        PlanGateMode mode = PlanGateMode.LastRound) =>
        PlanGateEvaluator.Evaluate("发版验收", "v2.3.0", target, allowErrors, excludeFlaky, mode, rounds);

    // ------------------------------ 基本判定

    [Fact]
    public void 没有轮次时未达标并给出原因()
    {
        var result = Evaluate([]);

        Assert.False(result.Passed);
        Assert.Null(result.EvaluatedRoundNo);
        Assert.Contains("还没有执行过任何轮次", result.Reasons[0]);
    }

    [Fact]
    public void 全部通过则达标()
    {
        var result = Evaluate([Round(1, passed: 48)]);

        Assert.True(result.Passed);
        Assert.Empty(result.Reasons);
        Assert.Equal(1.0, result.Stats.PassRate, 3);
    }

    [Fact]
    public void 恰好等于目标通过率算达标()
    {
        // 95/100 = 0.95，与目标相等。边界上取等号是达标，否则「目标 95%」永远达不到
        var result = Evaluate([Round(1, passed: 95, failed: 5)], target: 0.95);

        Assert.True(result.Passed);
    }

    [Fact]
    public void 低于目标时给出换算成用例数的差距()
    {
        // 90/100 = 0.90，目标 0.95 → 还差 5 条
        var result = Evaluate([Round(1, passed: 90, failed: 10, blocking: [Failure("A")])], target: 0.95);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, r => r.Contains("还差 5 条用例"));
    }

    // ------------------------------ 分母口径

    [Fact]
    public void 被跳过的执行不计入分母()
    {
        // 4 通过 / 1 跳过：分母应是 4 而不是 5，通过率 100% 而非 80%
        var result = Evaluate([Round(1, passed: 4, skipped: 1)]);

        Assert.True(result.Passed);
        Assert.Equal(4, result.Stats.Total);
        Assert.Equal(1, result.Stats.Skipped);
        Assert.Equal(1.0, result.Stats.PassRate, 3);
    }

    [Fact]
    public void 全部被跳过时未达标并说明()
    {
        var result = Evaluate([Round(1, passed: 0, skipped: 6)]);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, r => r.Contains("没有可判定的执行样本"));
        // 分母为 0 时通过率取 0，不能变成 NaN 或 1
        Assert.Equal(0, result.Stats.PassRate);
    }

    [Fact]
    public void 计数满足总数等于通过加失败加错误()
    {
        var result = Evaluate([Round(1, passed: 10, failed: 3, error: 2, skipped: 5)]);

        Assert.Equal(result.Stats.Total,
            result.Stats.Passed + result.Stats.Failed + result.Stats.Error);
    }

    // ------------------------------ flaky 排除

    [Fact]
    public void 排除flaky时分子分母一起扣()
    {
        // 9 通过 / 1 失败(flaky) / 10 总数：排除后分母 9、通过 9 → 100%
        var result = Evaluate([Round(1, passed: 9, failed: 1, flakyFailed: 1,
            blocking: [Failure("抖动的用例", flaky: true)])]);

        Assert.True(result.Passed);
        Assert.Equal(9, result.Stats.Total);
        Assert.Equal(0, result.Stats.Failed);
        Assert.Equal(1.0, result.Stats.PassRate, 3);
    }

    [Fact]
    public void 排除flaky时要在原因里告知而不是静默丢弃()
    {
        // 看报告的人会疑惑「明明有失败，为什么通过率 100%」，必须说明
        var result = Evaluate([Round(1, passed: 9, failed: 1, flakyFailed: 1)]);

        Assert.Contains(result.Reasons, r => r.Contains("已排除 1 条不稳定用例"));
    }

    [Fact]
    public void 关闭flaky排除时失败照常计入()
    {
        var result = Evaluate([Round(1, passed: 9, failed: 1, flakyFailed: 1,
            blocking: [Failure("抖动的用例", flaky: true)])], excludeFlaky: false);

        Assert.False(result.Passed);
        Assert.Equal(10, result.Stats.Total);
        Assert.Equal(1, result.Stats.Failed);
    }

    [Fact]
    public void flaky的失败不出现在阻断清单里()
    {
        var result = Evaluate([Round(1, passed: 9, failed: 1, flakyFailed: 1,
            blocking: [Failure("抖动的用例", flaky: true)])]);

        Assert.Empty(result.BlockingCases);
    }

    // ------------------------------ Error 口径

    [Fact]
    public void 默认不允许Error即使通过率达标也不通过()
    {
        // 48/49 = 97.9% 超过目标，但有 1 条 Error——环境坏了不该算通过
        var result = Evaluate([Round(1, passed: 48, error: 1,
            blocking: [Failure("E", status: ExecutionStatus.Error)])]);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, r => r.Contains("未允许 Error"));
    }

    [Fact]
    public void 允许Error时通过率达标即通过()
    {
        var result = Evaluate([Round(1, passed: 48, error: 1,
            blocking: [Failure("E", status: ExecutionStatus.Error)])], allowErrors: true);

        Assert.True(result.Passed);
    }

    [Fact]
    public void 阻断清单把Error排在Failed之后()
    {
        // Error 通常是环境问题，先看代码问题更有价值
        var result = Evaluate([Round(1, passed: 0, failed: 1, error: 1,
            blocking: [Failure("环境挂的", status: ExecutionStatus.Error), Failure("代码错的")])]);

        Assert.Equal(2, result.BlockingCases.Count);
        Assert.Equal(ExecutionStatus.Failed, result.BlockingCases[0].Status);
        Assert.Equal(ExecutionStatus.Error, result.BlockingCases[1].Status);
    }

    // ------------------------------ 轮次选择

    [Fact]
    public void 最后一轮模式取轮次号最大的那轮()
    {
        var result = Evaluate(
        [
            Round(1, passed: 100),                    // 第 1 轮达标
            Round(2, passed: 50, failed: 50, blocking: [Failure("退化")]),  // 第 2 轮退化
        ], mode: PlanGateMode.LastRound);

        Assert.False(result.Passed);
        Assert.Equal(2, result.EvaluatedRoundNo);
    }

    [Fact]
    public void 任意轮模式只要有达标轮就通过()
    {
        var result = Evaluate(
        [
            Round(1, passed: 100),
            Round(2, passed: 50, failed: 50, blocking: [Failure("退化")]),
        ], mode: PlanGateMode.AnyRound);

        Assert.True(result.Passed);
        Assert.Equal(1, result.EvaluatedRoundNo);
    }

    [Fact]
    public void 任意轮模式全不达标时报告最后一轮并说明()
    {
        var result = Evaluate(
        [
            Round(1, passed: 50, failed: 50, blocking: [Failure("A")]),
            Round(2, passed: 60, failed: 40, blocking: [Failure("B")]),
        ], mode: PlanGateMode.AnyRound);

        Assert.False(result.Passed);
        Assert.Equal(2, result.EvaluatedRoundNo);
        Assert.Contains(result.Reasons, r => r.Contains("共 2 轮均未达标"));
    }

    [Fact]
    public void 轮次乱序传入也能正确取最后一轮()
    {
        var result = Evaluate(
        [
            Round(3, passed: 50, failed: 50, blocking: [Failure("退化")]),
            Round(1, passed: 100),
            Round(2, passed: 100),
        ], mode: PlanGateMode.LastRound);

        Assert.Equal(3, result.EvaluatedRoundNo);
        Assert.False(result.Passed);
    }

    // ------------------------------ 通过率计算

    [Fact]
    public void 通过率按参与判定的样本计算()
    {
        // 18 通过 / 2 失败 / 5 跳过 → 分母 20，通过率 90%
        var result = Evaluate([Round(1, passed: 18, failed: 2, skipped: 5,
            blocking: [Failure("A")])], target: 0.95);

        Assert.Equal(0.90, result.Stats.PassRate, 4);
        Assert.Equal(20, result.Stats.Total);
        Assert.Equal(5, result.Stats.Skipped);
    }
}
