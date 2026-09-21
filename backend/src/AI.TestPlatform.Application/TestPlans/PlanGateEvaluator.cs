using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.TestPlans;

/// <summary>一轮的原始统计（执行级计数 + 阻断用例明细）</summary>
public record PlanRoundRaw(
    int RoundNo,
    DateTime StartedAt,
    DateTime? CompletedAt,
    /// <summary>本轮执行总数（含 Skipped）</summary>
    int Total,
    int Passed, int Failed, int Error, int Skipped,
    /// <summary>其中属于「不稳定用例」的执行数（只统计 Failed / Error）</summary>
    int FlakyFailed, int FlakyError,
    /// <summary>失败与 Error 的用例（去重后，供人看；同一用例多条执行只列一条）</summary>
    IReadOnlyList<PlanCaseOutcome> BlockingCases,
    /// <summary>其中属于「Agent 自愈通过」的执行数（Passed 的子集）</summary>
    int PassedViaAgent = 0);

public record PlanCaseOutcome(
    Guid TestCaseId, string Name, string? Module,
    ExecutionStatus Status, string? ErrorMessage, bool IsFlaky);

/// <summary>
/// 计划达标判定（纯函数，可完整单测）。
///
/// 抽成纯函数而不是塞进服务里，是因为这里的口径最容易出错、也最需要说清楚：
/// - **分母是「总数 − Skipped」**：被 FailFast 跳过的执行不代表「没通过」，
///   算进分母会无端拉低通过率，把本来达标的计划判成不达标；
/// - **排除 flaky 时要连分母一起扣**：既然认定这条样本不可信（抖动），
///   就不该既不算它失败、又拿它当分母。否则「排除 flaky」只改变了分子，逻辑不自洽；
/// - **必须给出人话原因**：只说 passed:false，CI 日志里就只能人肉翻报告。
/// </summary>
public static class PlanGateEvaluator
{
    public static PlanGateResult Evaluate(
        string planName, string? releaseName,
        double targetPassRate, bool allowErrors, bool excludeFlaky, PlanGateMode gateMode,
        IReadOnlyList<PlanRoundRaw> rounds,
        // 缺陷验收门槛（P2）：开启后项目存在未闭环致命/严重缺陷则直接不达标，
        // 无论 GateMode 与通过率如何——统计变成拦截才是验收闭环
        bool requireNoOpenCriticalDefects = false, int openCriticalDefectCount = 0,
        // M8：Agent 自愈"通过"是否计入达标（项目级 TreatAgentHealedAsPass，默认 false = 不计入）
        bool treatAgentHealedAsPass = false)
    {
        if (rounds.Count == 0)
        {
            return new PlanGateResult(false, planName, releaseName, targetPassRate, null,
                new PlanStatsDto(0, 0, 0, 0, 0, 0, 0),
                ["计划还没有执行过任何轮次，无法判定达标"], []);
        }

        var ordered = rounds.OrderBy(r => r.RoundNo).ToList();

        // 缺陷门槛的原因单独成列，与轮次通过率的原因并列展示
        var defectReasons = BuildDefectReasons(requireNoOpenCriticalDefects, openCriticalDefectCount);

        if (gateMode == PlanGateMode.AnyRound)
        {
            // 持续回归场景：任意一轮达标即算通过——但缺陷门槛不豁免。
            // 有达标轮次时仍用那一轮出报告（Stats 才是真实通过的那轮），
            // 缺陷门槛的原因追加在后面并把结论压成不达标
            var passing = ordered.FirstOrDefault(r =>
                Judge(r, targetPassRate, allowErrors, excludeFlaky, treatAgentHealedAsPass).Passed);
            if (passing is not null)
                return Build(passing, defectReasons.Count == 0 ? true : null,
                    targetPassRate, allowErrors, excludeFlaky,
                    planName, releaseName, [], false, defectReasons, treatAgentHealedAsPass);

            // 都不达标时用最后一轮的原因（最近一次的结果最能指导下一步）
            return Build(ordered[^1], false, targetPassRate, allowErrors, excludeFlaky,
                planName, releaseName,
                [$"共 {ordered.Count} 轮均未达标，以下为最后一轮的情况"], false, defectReasons,
                treatAgentHealedAsPass);
        }

        return Build(ordered[^1], null, targetPassRate, allowErrors, excludeFlaky,
            planName, releaseName, [], false, defectReasons, treatAgentHealedAsPass);
    }

    /// <summary>单轮判定</summary>
    public static (bool Passed, double PassRate, int EffectiveFailed, int EffectiveError,
        int Denominator, int ExcludedCount, List<string> Reasons) Judge(
        PlanRoundRaw round, double targetPassRate, bool allowErrors, bool excludeFlaky,
        bool treatAgentHealedAsPass = false)
    {
        var excluded = excludeFlaky ? round.FlakyFailed + round.FlakyError : 0;
        // Agent 自愈"通过"默认不算通过：它只是被 Agent 改过并重跑通了，**不是原生通过**。
        // 处理：从有效通过里扣掉、并等量计入有效失败（既不悄悄算通过，也不从分母抹掉）。
        var viaAgent = treatAgentHealedAsPass ? 0 : round.PassedViaAgent;
        var effectivePassed = round.Passed - viaAgent;
        var effectiveFailed = round.Failed + viaAgent - (excludeFlaky ? round.FlakyFailed : 0);
        // Error 可能是负数？不会——FlakyError 只统计 Status==Error 的执行，是 Failed/Error 的子集
        var effectiveError = round.Error - (excludeFlaky ? round.FlakyError : 0);

        // 分母：总数 − Skipped − 被排除的 flaky 样本
        var denominator = round.Total - round.Skipped - excluded;
        if (denominator < 0) denominator = 0;

        var passRate = denominator > 0 ? (double)effectivePassed / denominator : 0;

        // ⚠ 只有这三条是**判定性**原因；下面的自愈说明是**告知**，不得把结论压成不达标
        var blockingReasons = new List<string>();
        if (passRate < targetPassRate)
        {
            var gap = targetPassRate - passRate;
            // 换算成「还差几条用例」比只给百分比有用得多
            var casesShort = denominator > 0 ? (int)Math.Ceiling(gap * denominator) : 0;
            blockingReasons.Add(casesShort > 0
                ? $"通过率 {passRate:P1} 低于目标 {targetPassRate:P1}（还差 {casesShort} 条用例）"
                : $"通过率 {passRate:P1} 低于目标 {targetPassRate:P1}");
        }
        if (!allowErrors && effectiveError > 0)
            blockingReasons.Add($"存在 {effectiveError} 条 Error 执行（该计划未允许 Error）");
        if (denominator == 0)
            blockingReasons.Add("本轮没有可判定的执行样本（全部被跳过）");

        var reasons = new List<string>(blockingReasons);
        if (viaAgent > 0)
            reasons.Add($"{viaAgent} 条为 Agent 自愈通过，按项目设置不计入达标");

        return (blockingReasons.Count == 0, passRate, effectiveFailed, effectiveError, denominator, excluded, reasons);
    }

    private static PlanGateResult Build(
        PlanRoundRaw round, bool? forcePassed,
        double targetPassRate, bool allowErrors, bool excludeFlaky,
        string planName, string? releaseName, List<string> prefixReasons,
        bool ignoreDefectGate = false, List<string>? defectReasons = null,
        bool treatAgentHealedAsPass = false)
    {
        var (passed, passRate, effectiveFailed, effectiveError, denominator, excluded, reasons) =
            Judge(round, targetPassRate, allowErrors, excludeFlaky, treatAgentHealedAsPass);

        if (forcePassed is not null) passed = forcePassed.Value;

        var allReasons = new List<string>(prefixReasons);
        allReasons.AddRange(reasons);
        // 缺陷门槛不豁免：即使轮次全绿，致命/严重缺陷未闭环也不达标
        if (!ignoreDefectGate && defectReasons is { Count: > 0 })
        {
            passed = false;
            allReasons.AddRange(defectReasons);
        }
        // 被排除的样本要**告知**而不是静默丢弃：否则看报告的人会疑惑
        // 「明明有 3 条失败，为什么通过率还是 100%」
        if (excluded > 0)
            allReasons.Add($"已排除 {excluded} 条不稳定用例（flaky）的失败样本，不计入通过率");

        var blocking = round.BlockingCases
            .Where(c => !(excludeFlaky && c.IsFlaky))
            // Error 排在 Failed 之后：Error 通常是环境问题，先看代码问题更有价值
            .OrderBy(c => c.Status == ExecutionStatus.Error ? 1 : 0)
            .Select(c => new PlanBlockingCaseDto(c.TestCaseId, c.Name, c.Module, c.Status,
                c.ErrorMessage, c.IsFlaky))
            .ToList();

        return new PlanGateResult(passed, planName, releaseName, targetPassRate,
            round.RoundNo,
            // Total 只算「参与判定的样本」，保证 Passed + Failed + Error == Total 可自洽校验；
            // 被跳过的与排除掉的样本另列，不混进总数
            // 计入达标的通过数：默认扣掉 Agent 自愈通过；细分（原生/自愈）始终上报，便于界面区分
            new PlanStatsDto(denominator,
                round.Passed - (treatAgentHealedAsPass ? 0 : round.PassedViaAgent),
                effectiveFailed, effectiveError,
                round.Skipped, 0, passRate,
                PassedNative: round.Passed - round.PassedViaAgent,
                PassedViaAgent: round.PassedViaAgent),
            allReasons, blocking);
    }

    /// <summary>缺陷门槛的原因列表；未开启或无未闭环致命/严重缺陷时为空（空 = 不拦截）</summary>
    public static List<string> BuildDefectReasons(bool requireNoOpenCriticalDefects, int openCriticalDefectCount) =>
        requireNoOpenCriticalDefects && openCriticalDefectCount > 0
            ? [$"缺陷验收门槛未通过：项目仍有 {openCriticalDefectCount} 个未闭环的致命/严重缺陷"]
            : [];
}
