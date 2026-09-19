using AI.TestPlatform.Application.Reports;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ExecutionEntity = AI.TestPlatform.Domain.Entities.Execution;

namespace AI.TestPlatform.Api.Reports;

/// <summary>
/// 在线报告聚合：把执行记录整理成一份可直接渲染的报告负载。
///
/// 三种粒度：
/// - Execution：单次执行（含逐步明细与视觉差异 + 该用例最近 10 次历史）
/// - SuiteRun：一次套件运行（含本次运行的全部用例 + 最近 10 次运行的通过率趋势）
/// - Project：项目汇总（区间内每个用例取最新一次执行 + 每日趋势 + flake 排行）
/// </summary>
public class ReportAggregator
{
    /// <summary>项目汇总最多扫描的执行记录数（避免大项目把内存打满）</summary>
    private const int MaxExecutions = 2000;

    private readonly TestDbContext _db;

    public ReportAggregator(TestDbContext db) => _db = db;

    public async Task<PublicReportDto> ForExecutionAsync(Guid executionId, DateTime? expiresAt, CancellationToken ct)
    {
        var execution = await _db.Executions.AsNoTracking()
            .Include(e => e.Results).ThenInclude(r => r.TestStep)
            .Include(e => e.TestCase)
            .Include(e => e.Environment)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution is null)
            return Missing("单次执行报告", "Execution", null, null, null);

        var testCase = execution.TestCase;
        var history = await CaseHistoryAsync(execution.TestCaseId, ct);
        var cases = new List<ReportCaseDto>
        {
            ToCaseDto(execution, testCase, includeSteps: true, history),
        };

        var title = testCase is null ? "执行报告" : $"{testCase.Name} · 执行报告";
        var subtitle = $"执行于 {Local(execution.StartedAt ?? execution.CreatedAt):yyyy-MM-dd HH:mm:ss}" +
                       (string.IsNullOrWhiteSpace(execution.BrowserName)
                           ? string.Empty : $"｜{BrowserText(execution.BrowserName)}");

        return Build(title, subtitle, "Execution", expiresAt, cases,
            DailyTrend(cases.Select(c => (c.StartedAt, c.Status)).ToList()),
            await FlakeRankAsync(testCase?.ProjectId, ct),
            suiteId: execution.SuiteId, suiteName: null, suiteRunId: null);
    }

    public async Task<PublicReportDto> ForSuiteRunAsync(Guid suiteRunId, DateTime? expiresAt, CancellationToken ct)
    {
        var executions = await _db.Executions.AsNoTracking()
            .Include(e => e.TestCase)
            .Include(e => e.Environment)
            .Where(e => e.SuiteRunId == suiteRunId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        if (executions.Count == 0)
            return Missing("套件运行报告", "SuiteRun", null, null, null);

        var suiteId = executions[0].SuiteId;
        var suite = suiteId is null
            ? null
            : await _db.TestSuites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == suiteId.Value, ct);

        var cases = executions
            .Select(e => ToCaseDto(e, e.TestCase, includeSteps: false, history: null))
            .ToList();

        var title = suite is null ? "套件运行报告" : $"{suite.Name} · 运行报告";
        var subtitle = $"运行于 {Local(executions.Min(e => e.CreatedAt)):yyyy-MM-dd HH:mm:ss}｜共 {executions.Count} 条执行";

        return Build(title, subtitle, "SuiteRun", expiresAt, cases,
            await SuiteRunTrendAsync(suiteId, ct),
            await FlakeRankAsync(suite?.ProjectId, ct),
            suiteId, suite?.Name, suiteRunId);
    }

    /// <summary>项目汇总：区间内每个用例取最近一次执行</summary>
    public async Task<PublicReportDto> ForProjectAsync(Guid projectId, DateTime? from, DateTime? to,
        DateTime? expiresAt, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return Missing("项目测试报告", "Project", null, null, null);

        var query = _db.Executions.AsNoTracking()
            .Include(e => e.TestCase)
            .Include(e => e.Environment)
            .Where(e => e.TestCase != null && e.TestCase.ProjectId == projectId);
        if (from.HasValue) query = query.Where(e => e.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(e => e.CreatedAt <= to.Value);

        var executions = await query.OrderByDescending(e => e.CreatedAt).Take(MaxExecutions).ToListAsync(ct);
        // 已按时间倒序：每个用例取首次出现的（即最新一次）
        var latest = executions
            .GroupBy(e => e.TestCaseId)
            .Select(g => g.First())
            .OrderBy(e => e.TestCase?.Module)
            .ThenBy(e => e.TestCase?.Name)
            .ToList();

        var cases = latest.Select(e => ToCaseDto(e, e.TestCase, includeSteps: false, history: null)).ToList();
        var range = from.HasValue || to.HasValue
            ? $"（{(from.HasValue ? Local(from.Value).ToString("yyyy-MM-dd") : "起始")} ~ " +
              $"{(to.HasValue ? Local(to.Value).ToString("yyyy-MM-dd") : "至今")}）"
            : string.Empty;
        var subtitle = $"共 {latest.Count} 条用例{range}｜生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        return Build($"{project.Name} · 测试报告", subtitle, "Project", expiresAt, cases,
            DailyTrend(executions.Select(e => ((DateTime?)e.CreatedAt, (int)e.Status)).ToList()),
            await FlakeRankAsync(projectId, ct),
            null, null, null);
    }

    // ---------------------------------------------------------------- 组装

    /// <summary>
    /// 测试计划报告（迭代 E）。
    ///
    /// 与其它三种粒度的关键差别：**数字不能从用例列表重算**。
    /// 计划的通过率口径是「总数 − 跳过 − 被排除的 flaky」，只认执行级计数；
    /// 硬套 <see cref="Build"/> 会把被排除的 flaky 失败又算回去，分享出去的报告
    /// 就和平台内看到的判定结论不一致了。因此这里直接取门禁判定的统计量。
    /// </summary>
    public async Task<PublicReportDto> ForTestPlanAsync(Guid planId, DateTime? expiresAt, CancellationToken ct)
    {
        var plan = await _db.TestPlans.AsNoTracking()
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
            return new PublicReportDto("测试计划已删除", string.Empty, "TestPlan",
                DateTime.Now, expiresAt, true, EmptyOverview(), [], [], [], [], null, null, null);

        var rounds = await LoadPlanRoundsAsync(planId, ct);
        var gate = PlanGateEvaluator.Evaluate(plan.Name, plan.ReleaseName,
            plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure,
            plan.GateMode, rounds);

        var trends = rounds.Select(r =>
        {
            var (_, passRate, _, _, denominator, _, _) = PlanGateEvaluator.Judge(
                r, plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure);
            // 趋势图按「日期」聚合，这里一轮一个点，用完成时间（未完成则用开始时间）
            var at = r.CompletedAt ?? r.StartedAt;
            return new TrendPointDto(at.ToString("MM-dd HH:mm"), r.Passed, r.Failed + r.Error,
                denominator);
        }).ToList();

        // 阻断用例映射成报告里的「用例」条目，分享页无需为计划单独做一套渲染
        var cases = gate.BlockingCases.Select(b => new ReportCaseDto(
            Guid.Empty, b.TestCaseId == Guid.Empty ? null : b.TestCaseId, b.Name, null, b.Module, null,
            (int)b.Status, null, null, null, null, null,
            b.ErrorMessage, null, null, null, null, null, null, null, null)).ToList();

        var owner = plan.Owner is null ? null : plan.Owner.DisplayName ?? plan.Owner.Username;
        var subtitle = $"{(gate.Passed ? "✅ 已达标" : "❌ 未达标")}｜目标通过率 {plan.TargetPassRate:P0}"
                       + $"｜实际 {gate.Stats.PassRate:P1}"
                       + (gate.EvaluatedRoundNo is null ? "｜尚无轮次" : $"｜判定依据：第 {gate.EvaluatedRoundNo} 轮")
                       + (plan.ReleaseName is null ? string.Empty : $"｜版本 {plan.ReleaseName}")
                       + (owner is null ? string.Empty : $"｜负责人 {owner}");

        // 注意单位：ReportOverviewDto.PassRate 是**百分数**（×100），而 PlanStatsDto.PassRate 是 0~1
        var overview = new ReportOverviewDto(
            gate.Stats.Total, gate.Stats.Passed, gate.Stats.Failed, gate.Stats.Error,
            gate.Stats.Skipped, 0,
            Math.Round(gate.Stats.PassRate * 100, 1),
            rounds.Sum(r => 0L),
            rounds.Count > 0 ? rounds.Min(r => r.StartedAt) : null,
            rounds.Count > 0 ? rounds.Max(r => r.CompletedAt) : null,
            []);

        // 模块通过率同样走计划口径（见 TestPlanService.ToStats 的注释），保证与平台内一致
        var moduleRows = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanId == planId)
            .Select(e => new
            {
                Module = e.TestCase != null && e.TestCase.Module != null ? e.TestCase.Module : "未分类",
                e.Status,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
            })
            .ToListAsync(ct);

        var modules = moduleRows.GroupBy(m => m.Module)
            .Select(g =>
            {
                var excluded = plan.ExcludeFlakyFromFailure
                    ? g.Count(x => x.IsFlaky && x.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                    : 0;
                var skipped = g.Count(x => x.Status == ExecutionStatus.Skipped);
                var passed = g.Count(x => x.Status == ExecutionStatus.Passed);
                var failed = g.Count(x => x.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                             - (plan.ExcludeFlakyFromFailure
                                 ? g.Count(x => x.IsFlaky &&
                                                x.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                                 : 0);
                var denominator = Math.Max(0, g.Count() - skipped - excluded);
                return new ReportModuleDto(g.Key, denominator, passed, failed,
                    denominator > 0 ? Math.Round(passed * 100.0 / denominator, 1) : 0);
            })
            .OrderByDescending(m => m.Failed)
            .ToList();

        var title = plan.ReleaseName is null ? plan.Name : $"{plan.Name}（{plan.ReleaseName}）";
        return new PublicReportDto(title, subtitle, "TestPlan", DateTime.Now, expiresAt, false,
            overview, modules, cases, trends, [], null, null, null);
    }

    /// <summary>加载计划各轮的原始统计（与 <see cref="TestPlanService.LoadRoundsRawAsync"/> 同一口径）</summary>
    private async Task<List<PlanRoundRaw>> LoadPlanRoundsAsync(Guid planId, CancellationToken ct)
    {
        var rounds = await _db.TestPlanRounds.AsNoTracking()
            .Where(r => r.PlanId == planId).OrderBy(r => r.RoundNo).ToListAsync(ct);
        if (rounds.Count == 0) return [];

        var roundIds = rounds.Select(r => r.Id).ToList();
        var rows = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
            .Select(e => new
            {
                RoundId = e.PlanRoundId!.Value,
                e.Status, e.TestCaseId,
                CaseName = e.TestCase != null ? e.TestCase.Name : "(用例已删除)",
                Module = e.TestCase != null ? e.TestCase.Module : null,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
                StepError = e.Results.Where(r => r.ErrorMessage != null)
                    .OrderBy(r => r.StepOrder).Select(r => r.ErrorMessage).FirstOrDefault(),
                e.AIDiagnosis,
            })
            .ToListAsync(ct);

        var byRound = rows.GroupBy(r => r.RoundId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<PlanRoundRaw>();
        foreach (var round in rounds)
        {
            var list = byRound.TryGetValue(round.Id, out var l) ? l : [];
            var blocking = list
                .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                .GroupBy(r => r.TestCaseId)
                .Select(g =>
                {
                    var first = g.First();
                    var status = g.Any(x => x.Status == ExecutionStatus.Error)
                        ? ExecutionStatus.Error : ExecutionStatus.Failed;
                    return new PlanCaseOutcome(g.Key ?? Guid.Empty, first.CaseName, first.Module,
                        status, first.StepError ?? first.AIDiagnosis, first.IsFlaky);
                })
                .ToList();

            result.Add(new PlanRoundRaw(round.RoundNo, round.StartedAt, round.CompletedAt,
                list.Count,
                list.Count(r => r.Status == ExecutionStatus.Passed),
                list.Count(r => r.Status == ExecutionStatus.Failed),
                list.Count(r => r.Status == ExecutionStatus.Error),
                list.Count(r => r.Status == ExecutionStatus.Skipped),
                list.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Failed),
                list.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Error),
                blocking));
        }
        return result;
    }

    private static ReportOverviewDto EmptyOverview() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, null, null, []);

    private static PublicReportDto Build(string title, string subtitle, string kind, DateTime? expiresAt,
        List<ReportCaseDto> cases, List<TrendPointDto> trend, List<FlakeRankItemDto> flakeRank,
        Guid? suiteId, string? suiteName, Guid? suiteRunId)
    {
        var scored = cases.Where(c => c.Status is (int)ExecutionStatus.Passed
            or (int)ExecutionStatus.Failed or (int)ExecutionStatus.Error).ToList();
        var started = cases.Where(c => c.StartedAt.HasValue).Select(c => c.StartedAt!.Value).ToList();

        var overview = new ReportOverviewDto(
            Total: cases.Count,
            Passed: cases.Count(c => c.Status == (int)ExecutionStatus.Passed),
            Failed: cases.Count(c => c.Status == (int)ExecutionStatus.Failed),
            Error: cases.Count(c => c.Status == (int)ExecutionStatus.Error),
            Skipped: cases.Count(c => c.Status == (int)ExecutionStatus.Skipped),
            Pending: cases.Count(c => c.Status is (int)ExecutionStatus.Pending or (int)ExecutionStatus.Running),
            PassRate: scored.Count == 0
                ? 0
                : Math.Round(scored.Count(c => c.Status == (int)ExecutionStatus.Passed) * 100.0 / scored.Count, 1),
            DurationMs: cases.Sum(c => (long)(c.DurationMs ?? 0)),
            StartedAt: started.Count > 0 ? started.Min() : null,
            EndedAt: started.Count > 0 ? started.Max() : null,
            Browsers: cases.Select(c => c.BrowserName).Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => BrowserText(b)).Distinct().OrderBy(b => b).ToList());

        var modules = cases
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Module) ? "未分类" : c.Module!)
            .Select(g =>
            {
                var moduleScored = g.Count(c => c.Status is (int)ExecutionStatus.Passed
                    or (int)ExecutionStatus.Failed or (int)ExecutionStatus.Error);
                return new ReportModuleDto(
                    g.Key, g.Count(),
                    g.Count(c => c.Status == (int)ExecutionStatus.Passed),
                    g.Count(c => c.Status is (int)ExecutionStatus.Failed or (int)ExecutionStatus.Error),
                    moduleScored == 0
                        ? 0
                        : Math.Round(g.Count(c => c.Status == (int)ExecutionStatus.Passed) * 100.0 / moduleScored, 1));
            })
            .OrderBy(m => m.Module)
            .ToList();

        return new PublicReportDto(title, subtitle, kind, DateTime.Now, expiresAt, false,
            overview, modules, cases, trend, flakeRank, suiteId, suiteName, suiteRunId);
    }

    private static ReportCaseDto ToCaseDto(ExecutionEntity execution, TestCase? testCase, bool includeSteps,
        List<CaseHistoryPointDto>? history)
    {
        var steps = includeSteps
            ? execution.Results.OrderBy(r => r.StepOrder).Select(r => new ReportStepDto(
                r.StepOrder,
                r.TestStep?.ActionType.ToString() ?? "步骤",
                (int)r.Status, r.DurationMs, r.ErrorMessage, r.ScreenshotUrl,
                r.VisualStatus, r.VisualDiffRatio, r.BaselineImageUrl, r.DiffImageUrl, r.VisualNote)).ToList()
            : null;

        return new ReportCaseDto(
            execution.Id, execution.TestCaseId,
            testCase?.Name ?? "(用例已删除)",
            testCase?.CaseCode, testCase?.Module, testCase?.Priority,
            (int)execution.Status, execution.BrowserName, execution.BrowserVersion, execution.DataSetRowLabel,
            execution.DurationMs, execution.StartedAt, FirstError(execution), execution.AIDiagnosis,
            execution.AISuggestedFix,
            execution.EnvironmentSnapshot?.Name ?? execution.Environment?.Name,
            execution.TriggerSource,
            testCase?.SourceSteps, testCase?.ExpectedResult,
            steps, history);
    }

    private static string? FirstError(ExecutionEntity execution)
    {
        if (execution.Status is not (ExecutionStatus.Failed or ExecutionStatus.Error)) return null;
        return execution.Results
                   .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                   .OrderBy(r => r.StepOrder)
                   .Select(r => r.ErrorMessage)
                   .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
               ?? (execution.Status == ExecutionStatus.Error ? execution.AIDiagnosis : null);
    }

    /// <summary>用例最近 10 次已结束的执行（按时间升序，便于画趋势）</summary>
    private async Task<List<CaseHistoryPointDto>?> CaseHistoryAsync(Guid? testCaseId, CancellationToken ct)
    {
        if (testCaseId is null) return null;
        var rows = await _db.Executions.AsNoTracking()
            .Where(e => e.TestCaseId == testCaseId &&
                        e.Status != ExecutionStatus.Pending && e.Status != ExecutionStatus.Running)
            .OrderByDescending(e => e.CreatedAt)
            .Take(10)
            .Select(e => new { e.CreatedAt, e.Status, e.DurationMs, e.BrowserName })
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        return rows.OrderBy(r => r.CreatedAt)
            .Select(r => new CaseHistoryPointDto(r.CreatedAt, (int)r.Status, r.DurationMs, r.BrowserName))
            .ToList();
    }

    /// <summary>按天聚合趋势（最多 14 天）</summary>
    private static List<TrendPointDto> DailyTrend(List<(DateTime? At, int Status)> items)
    {
        return items
            .Where(i => i.At.HasValue)
            .GroupBy(i => Local(i.At!.Value).Date)
            .OrderBy(g => g.Key)
            .TakeLast(14)
            .Select(g => new TrendPointDto(
                g.Key.ToString("MM-dd"),
                g.Count(i => i.Status == (int)ExecutionStatus.Passed),
                g.Count(i => i.Status is (int)ExecutionStatus.Failed or (int)ExecutionStatus.Error),
                g.Count()))
            .ToList();
    }

    /// <summary>套件最近 10 次运行的通过率（一次运行内的执行聚合成一个点）</summary>
    private async Task<List<TrendPointDto>> SuiteRunTrendAsync(Guid? suiteId, CancellationToken ct)
    {
        if (suiteId is null) return new List<TrendPointDto>();
        var rows = await _db.Executions.AsNoTracking()
            .Where(e => e.SuiteId == suiteId && e.SuiteRunId != null &&
                        e.Status != ExecutionStatus.Pending && e.Status != ExecutionStatus.Running)
            .Select(e => new { e.SuiteRunId, e.CreatedAt, e.Status })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.SuiteRunId!.Value)
            .Select(g => new { RunId = g.Key, At = g.Min(r => r.CreatedAt), Items = g.ToList() })
            .OrderByDescending(g => g.At)
            .Take(10)
            .OrderBy(g => g.At)
            .Select(g => new TrendPointDto(
                Local(g.At).ToString("MM-dd HH:mm"),
                g.Items.Count(r => r.Status == ExecutionStatus.Passed),
                g.Items.Count(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error),
                g.Items.Count))
            .ToList();
    }

    /// <summary>项目内 flake 排行（不稳定度降序，最多 5 条）</summary>
    private async Task<List<FlakeRankItemDto>> FlakeRankAsync(Guid? projectId, CancellationToken ct)
    {
        if (projectId is null) return new List<FlakeRankItemDto>();
        var cases = await _db.TestCases.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.IsFlaky)
            .OrderByDescending(t => t.FlakeRate)
            .Take(5)
            .Select(t => new { t.Id, t.Name, t.FlakeRate })
            .ToListAsync(ct);
        if (cases.Count == 0) return new List<FlakeRankItemDto>();

        var ids = cases.Select(c => c.Id).ToList();
        var statuses = await _db.Executions.AsNoTracking()
            .Where(e => e.TestCaseId != null && ids.Contains(e.TestCaseId.Value) &&
                        e.Status != ExecutionStatus.Pending && e.Status != ExecutionStatus.Running)
            .Select(e => new { e.TestCaseId, e.CreatedAt, e.Status })
            .ToListAsync(ct);

        return cases.Select(c =>
        {
            var window = statuses.Where(w => w.TestCaseId == c.Id)
                .OrderByDescending(w => w.CreatedAt).Take(10).ToList();
            // 不稳定度直接采用用例上的统计值（口径与 flake 识别保持一致）
            return new FlakeRankItemDto(c.Id, c.Name, Math.Round(c.FlakeRate, 2), window.Count);
        }).ToList();
    }

    private static PublicReportDto Missing(string title, string kind,
        Guid? suiteId, string? suiteName, Guid? suiteRunId) => new(
        title, "报告数据不存在或已被删除", kind, DateTime.Now, null, true,
        new ReportOverviewDto(0, 0, 0, 0, 0, 0, 0, 0, null, null, new List<string>()),
        new List<ReportModuleDto>(), new List<ReportCaseDto>(),
        new List<TrendPointDto>(), new List<FlakeRankItemDto>(), suiteId, suiteName, suiteRunId);

    private static DateTime Local(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    private static string BrowserText(string? name) => name switch
    {
        "firefox" => "Firefox",
        "webkit" => "WebKit",
        "chromium" => "Chromium",
        _ => string.IsNullOrWhiteSpace(name) ? "Chromium" : name,
    };
}
