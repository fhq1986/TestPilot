using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Stats;

public record DashboardOverview(
    int TotalProjects, int TotalCases, int TotalExecutions,
    int Executions7d, double PassRate7d, int RunningCount,
    /// <summary>被标记为不稳定的用例数（flaky），点击可跳转到隔离视图</summary>
    int FlakyCount,
    // ---------------------------------------------------------------- 2026-09-12 新增
    /// <summary>测试计划总数</summary>
    int TotalPlans,
    /// <summary>进行中的计划数（验收期内）</summary>
    int ActivePlans,
    /// <summary>近 7 天执行时长 P95（毫秒）。用 P95 而不是只看均值：偶发的一次卡死会被均值吃掉，
    /// 而"越来越慢"这类性能回归恰恰最先体现在长尾上</summary>
    int DurationP95Ms,
    /// <summary>近 7 天执行时长均值（毫秒），与 P95 并排展示便于看出长尾有多长</summary>
    int DurationAvgMs,
    // ---------------------------------------------------------------- 2026-09-13 新增（缺陷）
    /// <summary>未闭环缺陷数（New/Assigned/Fixed）。测试的最终目的是定位并解决缺陷，
    /// 这个数是"还欠着多少债"的直接度量</summary>
    int OpenDefects,
    /// <summary>未闭环中致命/严重的数量——达标门槛盯的就是它</summary>
    int OpenCriticalDefects,
    /// <summary>近 7 天新增缺陷数</summary>
    int NewDefects7d,
    /// <summary>近 7 天闭环缺陷数（时点 = VerifiedAt，与缺陷模块口径一致）</summary>
    int ClosedDefects7d);

public record TrendPoint(string Date, int Passed, int Failed, int Total,
    /// <summary>当日新增缺陷数（质量趋势：执行结果只是症状，缺陷才是产出）</summary>
    int DefectsCreated = 0,
    /// <summary>当日闭环缺陷数（时点 = VerifiedAt，与缺陷模块口径一致）</summary>
    int DefectsClosed = 0);

/// <summary>
/// 稳定性榜条目。**替代了原来的「失败次数 Top5」**。
///
/// 为什么必须改：按**绝对失败次数**排，跑的次数多的用例必然霸榜，
/// 而「跑 100 次失败 20 次」（80% 通过）和「跑 5 次失败 5 次」（0% 通过）
/// 是性质完全不同的两件事——前者可能是抖动，后者说明用例本身就是坏的。
/// 现在按失败率排并带出样本量，让人自己判断可信度。
/// </summary>
public record UnstableCaseItem(
    Guid TestCaseId, string TestCaseName, string? Module,
    int TotalRuns, int FailCount, double FailRate,
    DateTime LastFailedAt,
    /// <summary>标注 flaky，便于区分"用例坏了"还是"环境抖动"</summary>
    bool IsFlaky);

/// <summary>进行中计划的达标态势。判定由 <see cref="PlanGateEvaluator"/> 给出，前端不重算</summary>
public record PlanGatingItem(
    Guid PlanId, Guid ProjectId, string PlanName, string? ReleaseName, string ProjectName,
    double TargetPassRate, int CaseCount,
    /// <summary>为 null 表示该计划还没有跑过轮次，此时**不该显示"未达标"**</summary>
    int? EvaluatedRoundNo,
    bool GatePassed, double EvaluatedPassRate,
    IReadOnlyList<string> GateReasons,
    DateTime? EndsAt, int? DaysToDeadline);

/// <summary>
/// 定时任务健康度。这是最容易「悄悄坏掉」的一环——调度器停了或被回收了，
/// 页面照常打开、没有任何报错，只是再也不跑测试了（本项目的 WSL 回收事故就是同类）。
/// </summary>
public record ScheduleHealth(
    int Total, int Enabled, int WithError,
    DateTime? LastRunAt, DateTime? NextRunAt, string? LastErrorScheduleName);

public record DashboardResponse(
    DashboardOverview Overview,
    List<TrendPoint> Trend,
    List<UnstableCaseItem> UnstableTop,
    List<PlanGatingItem> ActivePlans,
    ScheduleHealth ScheduleHealth);

// 仪表盘统计：概览 + 近 14 天趋势 + 近 30 天稳定性榜 + 进行中计划达标态势 + 定时任务健康
public static class StatsApiExtensions
{
    /// <summary>稳定性榜的最小样本量：只跑过一两次的用例算出来的失败率没有参考价值</summary>
    private const int MinRunsForStability = 5;

    /// <summary>稳定性榜与达标态势各取前几条，避免仪表盘被撑长</summary>
    private const int TopCount = 5;

    public static RouteGroupBuilder MapStatsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", async (
            TestDbContext db, TestPlanService plans, int? trendDays, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            // 趋势窗口可切换 14/30 天（默认 14）：30 天视图用于看周级别的质量走向
            var trendSpan = trendDays is >= 14 and <= 60 ? trendDays.Value : 14;
            var trendSince = now.Date.AddDays(-(trendSpan - 1));
            var weekSince = now.AddDays(-7);
            var monthSince = now.AddDays(-30);

            var totalProjects = await db.Projects.CountAsync(ct);
            var totalCases = await db.TestCases.CountAsync(ct);
            var totalExecutions = await db.Executions.CountAsync(ct);
            var runningCount = await db.Executions.AsNoTracking()
                .CountAsync(e => e.Status == ExecutionStatus.Pending || e.Status == ExecutionStatus.Running, ct);
            var flakyCount = await db.TestCases.AsNoTracking().CountAsync(t => t.IsFlaky, ct);
            var totalPlans = await db.TestPlans.CountAsync(ct);
            var activePlans = await db.TestPlans.AsNoTracking()
                .CountAsync(p => p.Status == TestPlanStatus.Active, ct);

            // 近 7 天通过率（仅统计已出结果的执行）
            var weekExecs = await db.Executions.AsNoTracking()
                .Where(e => e.CreatedAt >= weekSince)
                .Where(e => e.Status == ExecutionStatus.Passed ||
                            e.Status == ExecutionStatus.Failed ||
                            e.Status == ExecutionStatus.Error)
                .Select(e => e.Status)
                .ToListAsync(ct);
            var passRate7d = weekExecs.Count == 0 ? 0 : Math.Round(weekExecs.Count(s => s == ExecutionStatus.Passed) * 100.0 / weekExecs.Count, 1);

            // 近 7 天时长只取 DurationMs 一列，不必把整行拉回来
            var durations = await db.Executions.AsNoTracking()
                .Where(e => e.CreatedAt >= weekSince && e.DurationMs != null)
                .Select(e => e.DurationMs!.Value)
                .ToListAsync(ct);
            var (durationP95, durationAvg) = DurationStats(durations);

            // 缺陷指标：与缺陷管理页同一口径（未闭环 = New/Assigned/Fixed，闭环时点 = VerifiedAt）。
            // 全表量级有限，一次把状态/严重度/时间拉回内存聚合，避免四条独立 COUNT 来回跑
            var defectRows = await db.Defects.AsNoTracking()
                .Select(d => new { d.Severity, d.Status, d.CreatedAt, d.VerifiedAt })
                .ToListAsync(ct);
            var openDefects = defectRows.Count(d =>
                d.Status is DefectStatus.New or DefectStatus.Assigned or DefectStatus.Fixed);
            var openCriticalDefects = defectRows.Count(d =>
                d.Status is DefectStatus.New or DefectStatus.Assigned or DefectStatus.Fixed
                && d.Severity is DefectSeverity.Critical or DefectSeverity.Major);
            var newDefects7d = defectRows.Count(d => d.CreatedAt >= weekSince);
            var closedDefects7d = defectRows.Count(d => d.VerifiedAt >= weekSince);

            // 近 14 天趋势（按日期分组，内存聚合——数据量有限，避免复杂 SQL 翻译）
            var trendExecs = await db.Executions.AsNoTracking()
                .Where(e => e.CreatedAt >= trendSince)
                .Where(e => e.Status == ExecutionStatus.Passed ||
                            e.Status == ExecutionStatus.Failed ||
                            e.Status == ExecutionStatus.Error)
                .Select(e => new { e.CreatedAt, e.Status })
                .ToListAsync(ct);
            var trend = Enumerable.Range(0, trendSpan)
                .Select(offset =>
                {
                    var date = trendSince.AddDays(offset);
                    var dayExecs = trendExecs.Where(e => e.CreatedAt.Date == date).ToList();
                    var passed = dayExecs.Count(e => e.Status == ExecutionStatus.Passed);
                    // 缺陷进出与执行共用同一日期轴，质量因果一眼可见（失败多→新增缺陷多）
                    return new TrendPoint(date.ToString("MM-dd"), passed,
                        dayExecs.Count - passed, dayExecs.Count,
                        defectRows.Count(d => d.CreatedAt.Date == date),
                        defectRows.Count(d => d.VerifiedAt.HasValue && d.VerifiedAt.Value.Date == date));
                })
                .ToList();

            var unstableTop = await BuildUnstableTopAsync(db, monthSince, ct);
            var activePlanGating = await BuildPlanGatingAsync(db, plans, ct);
            var scheduleHealth = await BuildScheduleHealthAsync(db, ct);

            return Results.Ok(new DashboardResponse(
                new DashboardOverview(totalProjects, totalCases, totalExecutions, weekExecs.Count, passRate7d,
                    runningCount, flakyCount, totalPlans, activePlans, durationP95, durationAvg,
                    openDefects, openCriticalDefects, newDefects7d, closedDefects7d),
                trend, unstableTop, activePlanGating, scheduleHealth));
        }).WithPermission(Permission.ViewProjects).Produces<DashboardResponse>();

        return group;
    }

    /// <summary>
    /// P95 与均值。样本为 1 时 P95 就取那一个值——若按"下标 = 数量×0.95"机械取整，
    /// 单样本会算出 0，界面上显示"P95 = 0ms"比不显示更误导。
    /// </summary>
    private static (int P95, int Avg) DurationStats(List<int> values)
    {
        if (values.Count == 0) return (0, 0);

        var ordered = values.OrderBy(v => v).ToList();
        var index = Math.Clamp((int)Math.Ceiling(ordered.Count * 0.95) - 1, 0, ordered.Count - 1);
        return (ordered[index], (int)Math.Round(ordered.Average()));
    }

    /// <summary>近 30 天稳定性榜：按失败率排，样本量不足的不入选</summary>
    private static async Task<List<UnstableCaseItem>> BuildUnstableTopAsync(
        TestDbContext db, DateTime since, CancellationToken ct)
    {
        var rows = await db.Executions.AsNoTracking()
            .Where(e => e.CreatedAt >= since && e.TestCaseId != null)
            .Where(e => e.Status == ExecutionStatus.Passed ||
                        e.Status == ExecutionStatus.Failed ||
                        e.Status == ExecutionStatus.Error)
            .Select(e => new { e.TestCaseId, e.Status, e.CreatedAt })
            .ToListAsync(ct);

        var ranked = rows
            .GroupBy(x => x.TestCaseId!.Value)
            .Select(g =>
            {
                var fail = g.Count(x => x.Status != ExecutionStatus.Passed);
                return new
                {
                    TestCaseId = g.Key,
                    Total = g.Count(),
                    Fail = fail,
                    LastFailedAt = g.Where(x => x.Status != ExecutionStatus.Passed)
                                    .Select(x => x.CreatedAt).DefaultIfEmpty().Max(),
                };
            })
            .Where(x => x.Total >= MinRunsForStability && x.Fail > 0)
            .OrderByDescending(x => (double)x.Fail / x.Total)
            .ThenByDescending(x => x.Fail)
            .Take(TopCount)
            .ToList();

        if (ranked.Count == 0) return [];

        // 用例名/模块/flaky 一次性取回，避免逐条查询
        var ids = ranked.Select(x => x.TestCaseId).ToList();
        var metas = await db.TestCases.IgnoreQueryFilters().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Module, t.IsFlaky })
            .ToDictionaryAsync(t => t.Id, ct);

        return ranked.Select(x =>
        {
            var meta = metas.GetValueOrDefault(x.TestCaseId);
            return new UnstableCaseItem(
                x.TestCaseId,
                meta?.Name ?? "(用例已删除)",
                meta?.Module,
                x.Total, x.Fail,
                Math.Round((double)x.Fail / x.Total, 4),
                x.LastFailedAt,
                meta?.IsFlaky ?? false);
        }).ToList();
    }

    /// <summary>
    /// 进行中计划的达标态势。达标一律走 <see cref="TestPlanService.BuildGateAsync"/>——
    /// 前端拿 lastPassRate 和 targetPassRate 自己比会漏掉 allowErrors / excludeFlaky / GateMode，
    /// 直接造出第二套口径。
    /// </summary>
    private static async Task<List<PlanGatingItem>> BuildPlanGatingAsync(
        TestDbContext db, TestPlanService plans, CancellationToken ct)
    {
        var active = await db.TestPlans.AsNoTracking()
            .Where(p => p.Status == TestPlanStatus.Active)
            .OrderBy(p => p.EndsAt == null)      // 有截止日期的排前面
            .ThenBy(p => p.EndsAt)
            .Select(p => new
            {
                p.Id, p.ProjectId, p.Name, p.ReleaseName, p.TargetPassRate, p.EndsAt,
                ProjectName = p.Project.Name,
            })
            .Take(TopCount)
            .ToListAsync(ct);

        if (active.Count == 0) return [];

        var planIds = active.Select(p => p.Id).ToList();
        var caseCounts = await db.TestPlanItems.AsNoTracking()
            .Where(i => planIds.Contains(i.PlanId))
            .GroupBy(i => i.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, ct);

        var today = DateTime.UtcNow.Date;
        var result = new List<PlanGatingItem>(active.Count);
        foreach (var p in active)
        {
            var gate = await plans.BuildGateAsync(p.Id, ct);
            result.Add(new PlanGatingItem(
                p.Id, p.ProjectId, p.Name, p.ReleaseName, p.ProjectName,
                p.TargetPassRate,
                caseCounts.GetValueOrDefault(p.Id),
                gate?.EvaluatedRoundNo,
                gate?.Passed ?? false,
                gate?.Stats.PassRate ?? 0,
                gate?.Reasons ?? [],
                p.EndsAt,
                p.EndsAt is null ? null : (int)Math.Ceiling((p.EndsAt.Value.Date - today).TotalDays)));
        }
        return result;
    }

    private static async Task<ScheduleHealth> BuildScheduleHealthAsync(TestDbContext db, CancellationToken ct)
    {
        var schedules = await db.Schedules.AsNoTracking()
            .Select(s => new { s.Name, s.Enabled, s.LastRunAt, s.NextRunAt, s.LastError })
            .ToListAsync(ct);

        var withError = schedules.Where(s => !string.IsNullOrWhiteSpace(s.LastError)).ToList();

        return new ScheduleHealth(
            schedules.Count,
            schedules.Count(s => s.Enabled),
            withError.Count,
            schedules.Where(s => s.LastRunAt.HasValue).Select(s => s.LastRunAt).DefaultIfEmpty().Max(),
            schedules.Where(s => s.Enabled && s.NextRunAt.HasValue)
                     .Select(s => s.NextRunAt).DefaultIfEmpty().Min(),
            withError.FirstOrDefault()?.Name);
    }
}
