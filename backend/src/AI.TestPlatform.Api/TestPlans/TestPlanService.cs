using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.TestPlans;

/// <summary>轮次已在进行中（同时只允许一个）。端点会把它转成 409</summary>
public class PlanRoundConflictException(string message) : Exception(message);

/// <summary>
/// 单轮的执行级原始计数（未套用 flaky 排除口径）。
/// 折成「参与判定的样本」走 <see cref="TestPlanService.ToStats"/>。
/// </summary>
public record RoundCounts(
    int Total, int Passed, int Failed, int Error, int Skipped, int Pending,
    int FlakyFailed, int FlakyError)
{
    /// <summary>该轮还没有任何执行时的零值（字典取不到时用，避免散落的 null 判断）</summary>
    public static readonly RoundCounts Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public class TestPlanService
{
    private readonly TestDbContext _db;
    private readonly ExecutionPlanner _planner;
    private readonly ExecutionQueue _queue;
    private readonly ILogger<TestPlanService> _logger;

    public TestPlanService(TestDbContext db, ExecutionPlanner planner, ExecutionQueue queue,
        ILogger<TestPlanService> logger)
    {
        _db = db;
        _planner = planner;
        _queue = queue;
        _logger = logger;
    }

    // ================================================================== 范围体检

    /// <summary>
    /// 范围体检：把「排进计划但跑不了」的用例提前找出来。
    ///
    /// 计划常在提测前突击编排，范围里混进不可执行的用例如果等到验收当天才发现，
    /// 代价很大。做成一个随时可点的接口，也可以放进报告的前置检查。
    /// </summary>
    public async Task<List<PlanScopeIssueDto>> ValidateScopeAsync(Guid planId, CancellationToken ct)
    {
        var items = await _db.TestPlanItems.AsNoTracking()
            .Where(i => i.PlanId == planId)
            // 注意：已删除的用例会被全局查询过滤器挡掉，`i.TestCase` 变成 null，
            // 此时 `i.TestCase.Type` 在 SQL 里是 NULL —— 直接读非空枚举会抛异常。
            // 所以三个字段都写成可空形式；下面第一步就会把这类行判为 Deleted 并 continue。
            .Select(i => new
            {
                i.TestCaseId,
                Name = i.TestCase != null ? i.TestCase.Name : "(用例已删除)",
                Type = i.TestCase != null ? i.TestCase.Type : (TestType?)null,
                Status = i.TestCase != null ? i.TestCase.Status : (TestCaseStatus?)null,
                StepCount = i.TestCase != null ? i.TestCase.Steps.Count : 0,
            })
            .ToListAsync(ct);

        var issues = new List<PlanScopeIssueDto>();

        // 先拿"真实存在"的用例 ID，用来区分「软删除」与「已不存在」
        var aliveIds = await _db.TestCases.AsNoTracking()
            .Where(t => items.Select(i => i.TestCaseId).Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);
        var alive = aliveIds.ToHashSet();

        foreach (var item in items)
        {
            if (!alive.Contains(item.TestCaseId))
            {
                issues.Add(new PlanScopeIssueDto("Error", "Deleted", item.TestCaseId, item.Name,
                    "用例已被删除，执行时会被跳过，建议从范围中移除"));
                continue;
            }
            if (item.Type == TestType.Mobile)
            {
                issues.Add(new PlanScopeIssueDto("Error", "Unsupported", item.TestCaseId, item.Name,
                    "移动端用例的执行链路尚未实现，会被跳过"));
                continue;
            }
            if (item.StepCount == 0)
            {
                issues.Add(new PlanScopeIssueDto("Warning", "NoSteps", item.TestCaseId, item.Name,
                    "用例没有任何步骤，执行必然无意义"));
                continue;
            }
            // 这里原来还有一条 "Deprecated" 警告，但它和上面那条 "Deleted" 检查是**同一件事**——
            // 都是"这条用例已经没了"（软删除只是标记，用例行还在）。
            // 两套说法报同一件事只会让人以为是两个问题，所以合并成上面那一处。
        }

        return issues;
    }

    // ================================================================== 轮次统计

    /// <summary>
    /// 加载各轮的原始统计。
    ///
    /// **不落冗余状态字段**：轮次的通过/失败一律由本轮的执行记录实时聚合。
    /// 冗余字段必须在每条执行完成时回写，既写放大，异常中断时还会留下不一致的值。
    /// </summary>
    public async Task<List<PlanRoundRaw>> LoadRoundsRawAsync(Guid planId, CancellationToken ct)
    {
        var rounds = await _db.TestPlanRounds.AsNoTracking()
            .Where(r => r.PlanId == planId)
            .OrderBy(r => r.RoundNo)
            .ToListAsync(ct);
        if (rounds.Count == 0) return [];

        var roundIds = rounds.Select(r => r.Id).ToList();
        var executions = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
            .Select(e => new
            {
                RoundId = e.PlanRoundId!.Value,
                e.Status,
                e.TestCaseId,
                CaseName = e.TestCase != null ? e.TestCase.Name : "(用例已删除)",
                Module = e.TestCase != null ? e.TestCase.Module : null,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
                // 错误信息在步骤结果上（Execution 本身只有 AI 诊断），取第一条带错误的步骤
                StepError = e.Results
                    .Where(r => r.ErrorMessage != null)
                    .OrderBy(r => r.StepOrder)
                    .Select(r => r.ErrorMessage)
                    .FirstOrDefault(),
                e.AIDiagnosis,
            })
            .ToListAsync(ct);

        var byRound = executions.GroupBy(e => e.RoundId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PlanRoundRaw>();
        foreach (var round in rounds)
        {
            var rows = byRound.TryGetValue(round.Id, out var list) ? list : [];

            var passed = rows.Count(r => r.Status == ExecutionStatus.Passed);
            var failed = rows.Count(r => r.Status == ExecutionStatus.Failed);
            var error = rows.Count(r => r.Status == ExecutionStatus.Error);
            var skipped = rows.Count(r => r.Status == ExecutionStatus.Skipped);

            var flakyFailed = rows.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Failed);
            var flakyError = rows.Count(r => r.IsFlaky && r.Status == ExecutionStatus.Error);

            // 阻断用例：失败与 Error 的用例去重（同一用例多浏览器/多数据行只列一条）
            var blocking = rows
                .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                .GroupBy(r => r.TestCaseId)
                .Select(g =>
                {
                    var first = g.First();
                    // 同一用例多条执行时取「最严重」的状态：Error 比 Failed 更需要人处理
                    var status = g.Any(x => x.Status == ExecutionStatus.Error)
                        ? ExecutionStatus.Error
                        : ExecutionStatus.Failed;
                    // 用例被删除后 Execution.TestCaseId 会被置空（SetNull），
                    // 这些执行仍要出现在阻断清单里（否则报告会少条），用 Empty 占位
                    return new PlanCaseOutcome(
                        g.Key ?? Guid.Empty, first.CaseName, first.Module, status,
                        first.StepError ?? first.AIDiagnosis, first.IsFlaky);
                })
                .OrderByDescending(c => c.Status == ExecutionStatus.Error)
                .ToList();

            result.Add(new PlanRoundRaw(round.RoundNo, round.StartedAt, round.CompletedAt,
                rows.Count, passed, failed, error, skipped, flakyFailed, flakyError, blocking));
        }

        return result;
    }

    /// <summary>
    /// 单轮的执行级计数（轮次列表页用，不必把全部明细拉回来）。
    /// 额外带上「不稳定用例的失败数」——轮次列表必须用与达标判定**同一套口径**，
    /// 否则会出现「轮次页显示 60%、达标判定说 75%」这种两个说法的情况，用户就不敢信数字了。
    /// </summary>
    public async Task<Dictionary<Guid, RoundCounts>> LoadRoundCountsAsync(
        IReadOnlyList<Guid> roundIds, CancellationToken ct)
    {
        if (roundIds.Count == 0) return new();

        var rows = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
            .Select(e => new
            {
                RoundId = e.PlanRoundId!.Value,
                e.Status,
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
            })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.RoundId).ToDictionary(
            g => g.Key,
            g => new RoundCounts(
                Total: g.Count(),
                Passed: g.Count(x => x.Status == ExecutionStatus.Passed),
                Failed: g.Count(x => x.Status == ExecutionStatus.Failed),
                Error: g.Count(x => x.Status == ExecutionStatus.Error),
                Skipped: g.Count(x => x.Status == ExecutionStatus.Skipped),
                Pending: g.Count(x => x.Status is ExecutionStatus.Pending or ExecutionStatus.Running),
                FlakyFailed: g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Failed),
                FlakyError: g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Error)));
    }

    /// <summary>
    /// 按计划口径把原始计数折成「参与判定的样本」。与 <see cref="PlanGateEvaluator.Judge"/> 同一算法，
    /// 保证轮次页与达标判定的数字一致。
    /// </summary>
    public static PlanStatsDto ToStats(RoundCounts c, bool excludeFlaky)
    {
        var excluded = excludeFlaky ? c.FlakyFailed + c.FlakyError : 0;
        var failed = c.Failed - (excludeFlaky ? c.FlakyFailed : 0);
        var error = c.Error - (excludeFlaky ? c.FlakyError : 0);
        var denominator = Math.Max(0, c.Total - c.Skipped - excluded);
        var passRate = denominator > 0 ? (double)c.Passed / denominator : 0;
        return new PlanStatsDto(denominator, c.Passed, failed, error, c.Skipped, c.Pending, passRate);
    }

    /// <summary>取某轮的阻塞用例明细（轮次详情展开时按需加载）</summary>
    public async Task<List<PlanRoundCaseResultDto>> LoadRoundDetailAsync(Guid roundId, CancellationToken ct)
    {
        var snapshot = await _db.PlanRoundCases.AsNoTracking()
            .Where(c => c.RoundId == roundId)
            .OrderBy(c => c.Order)
            .ToListAsync(ct);

        var executions = await _db.Executions.AsNoTracking()
            .Where(e => e.PlanRoundId == roundId)
            .Select(e => new
            {
                e.Id, e.TestCaseId, e.BrowserName, e.DataSetRowLabel, e.Status,
                e.DurationMs, e.AIDiagnosis,
                // 错误信息与截图都在步骤结果上，取第一条有内容的（与轮次统计同一口径）
                StepError = e.Results
                    .Where(r => r.ErrorMessage != null)
                    .OrderBy(r => r.StepOrder)
                    .Select(r => r.ErrorMessage)
                    .FirstOrDefault(),
                ScreenshotUrl = e.Results
                    .Where(r => r.ScreenshotUrl != null)
                    .OrderBy(r => r.StepOrder)
                    .Select(r => r.ScreenshotUrl)
                    .FirstOrDefault(),
                // 对外统一改发受权 trace 端点 URL（安全审查 S1），旧格式落库值一并翻译
                TraceUrl = e.TraceUrl == null ? null : $"/api/executions/{e.Id:N}/trace",
                IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
            })
            .ToListAsync(ct);

        // 用例被删除后执行记录的 TestCaseId 会是 null（SetNull），
        // 这些执行归不到任何快照条目上，直接排除；用 Guid.Empty 占位反而会和真实用例混淆
        var byCase = executions
            .Where(e => e.TestCaseId is not null)
            .GroupBy(e => e.TestCaseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return snapshot.Select(c =>
        {
            var rows = byCase.TryGetValue(c.TestCaseId, out var list) ? list : [];
            return new PlanRoundCaseResultDto(c.TestCaseId, c.TestCaseName, c.Module, c.Order,
                rows.Select(e => new PlanRoundExecutionDto(
                    e.Id, e.BrowserName, e.DataSetRowLabel, e.Status, e.DurationMs,
                    e.StepError ?? e.AIDiagnosis, e.IsFlaky, e.ScreenshotUrl, e.TraceUrl))
                    .ToList());
        }).ToList();
    }

    // ================================================================== 开轮次

    /// <summary>
    /// 开一轮。同一计划同时只允许一个进行中的轮次——由数据库的部分唯一索引兜底，
    /// 这里捕获唯一冲突并转成业务异常，避免「先查后写」在并发下双双通过。
    /// </summary>
    public async Task<(TestPlanRound? Round, string? Error)> StartRoundAsync(
        Guid planId, StartRoundRequest request, TriggerType triggerType, string? triggerSource,
        Guid? triggeredById, CancellationToken ct)
    {
        var plan = await _db.TestPlans
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return (null, "测试计划不存在");
        if (plan.Status == TestPlanStatus.Archived)
            return (null, "计划已归档，不能开新一轮");

        var caseIds = plan.Items.OrderBy(i => i.Order).Select(i => i.TestCaseId).ToList();
        if (caseIds.Count == 0) return (null, "计划范围内没有用例");

        var running = await _db.TestPlanRounds
            .AnyAsync(r => r.PlanId == planId && r.Status == PlanRoundStatus.Running, ct);
        if (running) return (null, "该计划已有进行中的轮次，请等它结束或先中止");

        var roundNo = (await _db.TestPlanRounds.Where(r => r.PlanId == planId)
            .MaxAsync(r => (int?)r.RoundNo, ct) ?? 0) + 1;

        var environmentId = request.EnvironmentId ?? plan.EnvironmentId;
        var browsers = request.Browsers ?? plan.Browsers;
        var expandDataSets = request.ExpandDataSets ?? plan.ExpandDataSets;

        var round = new TestPlanRound
        {
            PlanId = planId,
            RoundNo = roundNo,
            Status = PlanRoundStatus.Running,
            TriggerType = triggerType,
            TriggerSource = triggerSource ?? $"测试计划：{plan.Name}",
            TriggeredById = triggeredById,
            EnvironmentId = environmentId,
            Browsers = browsers,
            ExpandDataSets = expandDataSets,
            StartedAt = DateTime.UtcNow,
        };

        try
        {
            // 范围快照：把「这一轮跑了哪些用例」钉死，后续编辑/删除用例不影响历史轮次报告
            var cases = await _db.TestCases.AsNoTracking()
                .Where(t => caseIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name, t.Module })
                .ToListAsync(ct);
            var byId = cases.ToDictionary(c => c.Id);

            var order = 0;
            foreach (var caseId in caseIds)
            {
                if (!byId.TryGetValue(caseId, out var c)) continue;
                _db.PlanRoundCases.Add(new PlanRoundCase
                {
                    RoundId = round.Id,
                    TestCaseId = caseId,
                    TestCaseName = c.Name,
                    Module = c.Module,
                    Order = order++,
                });
            }

            _db.TestPlanRounds.Add(round);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return (null, "该计划已有进行中的轮次（并发触发被数据库唯一约束拦下）");
        }

        try
        {
            var plan1 = await _planner.PlanAsync(caseIds, environmentId, browsers, expandDataSets,
                request.Variables, triggerType, round.TriggerSource, triggeredById,
                planId: planId, planRoundId: round.Id, ct: ct);

            if (plan1.Executions.Count == 0)
            {
                round.Status = PlanRoundStatus.Aborted;
                round.Error = "范围内没有可执行的用例（移动端用例不支持执行）";
                round.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return (round, round.Error);
            }

            _db.Executions.AddRange(plan1.Executions);
            round.CreatedCount = plan1.Executions.Count;

            // 计划从 Draft 自动进入 Active：开跑即视为已开始
            if (plan.Status == TestPlanStatus.Draft) plan.Status = TestPlanStatus.Active;
            plan.LastRoundAt = round.StartedAt;
            plan.LastCreatedCount = round.CreatedCount;
            plan.LastError = null;
            plan.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            foreach (var execution in plan1.Executions)
                await _queue.EnqueueAsync(execution.Id, ct);

            _logger.LogInformation("计划 {Name}（{PlanId}）第 {RoundNo} 轮：创建 {Count} 条执行",
                plan.Name, planId, roundNo, round.CreatedCount);

            return (round, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计划 {PlanId} 第 {RoundNo} 轮创建执行失败", planId, roundNo);
            round.Status = PlanRoundStatus.Aborted;
            round.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            round.CompletedAt = DateTime.UtcNow;
            plan.LastError = round.Error;
            await _db.SaveChangesAsync(ct);
            return (round, round.Error);
        }
    }

    /// <summary>
    /// 中止本轮：只把**尚未开始**的执行置为 Skipped，不打断正在跑的。
    /// 打断执行会杀掉浏览器并留下半个状态，得不偿失。
    /// </summary>
    public async Task<(bool Ok, int Skipped, string? Error)> AbortRoundAsync(Guid roundId, CancellationToken ct)
    {
        var round = await _db.TestPlanRounds.FirstOrDefaultAsync(r => r.Id == roundId, ct);
        if (round is null) return (false, 0, "轮次不存在");
        if (round.Status != PlanRoundStatus.Running)
            return (false, 0, "轮次已结束，无需中止");

        var pending = await _db.Executions
            .Where(e => e.PlanRoundId == roundId && e.Status == ExecutionStatus.Pending)
            .ToListAsync(ct);
        foreach (var execution in pending)
        {
            execution.Status = ExecutionStatus.Skipped;
            execution.EndedAt = DateTime.UtcNow;
            execution.DurationMs = 0;
            execution.AIDiagnosis = "轮次被手动中止，未执行";
        }

        round.Status = PlanRoundStatus.Aborted;
        round.CompletedAt = DateTime.UtcNow;
        round.Error = $"轮次被手动中止（跳过 {pending.Count} 条未开始的执行）";
        await _db.SaveChangesAsync(ct);

        // 🔁 同 TryCompleteRoundAsync：中止后若已无任何 Running 轮次，自动把计划转 Completed
        var plan = await _db.TestPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == round.PlanId, ct);
        if (plan is not null && plan.Status == TestPlanStatus.Active)
        {
            var anyRunning = await _db.TestPlanRounds
                .AnyAsync(r => r.PlanId == round.PlanId && r.Status == PlanRoundStatus.Running, ct);
            if (!anyRunning)
            {
                var planEntity = await _db.TestPlans.FirstOrDefaultAsync(p => p.Id == round.PlanId, ct);
                if (planEntity is not null && planEntity.Status == TestPlanStatus.Active)
                {
                    planEntity.Status = TestPlanStatus.Completed;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("计划 {PlanId}（{PlanName}）因最后一轮被中止，自动转态为 Completed",
                        round.PlanId, planEntity.Name);
                }
            }
        }

        _logger.LogInformation("轮次 {RoundId} 已中止，跳过 {Count} 条执行", roundId, pending.Count);
        return (true, pending.Count, null);
    }

    /// <summary>
    /// 轮次完成回填：该轮已无 Pending/Running 时把轮次标为 Completed。
    /// 由执行完成回调触发（不引入独立轮询——执行完成就是最准确的时点）。
    /// </summary>
    public async Task<Guid?> TryCompleteRoundAsync(Guid roundId, CancellationToken ct)
    {
        var round = await _db.TestPlanRounds.FirstOrDefaultAsync(r => r.Id == roundId, ct);
        if (round is null || round.Status != PlanRoundStatus.Running) return null;

        var unfinished = await _db.Executions
            .AnyAsync(e => e.PlanRoundId == roundId &&
                           (e.Status == ExecutionStatus.Pending || e.Status == ExecutionStatus.Running), ct);
        if (unfinished) return null;

        round.Status = PlanRoundStatus.Completed;
        round.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("轮次 {RoundId}（第 {RoundNo} 轮）已完成", roundId, round.RoundNo);

        // 🔁 自动转态：如果计划下已无任何 Running 轮次，且计划状态还是 Active，→ 转 Completed
        // （有 "标记完成" 手动按钮兜底，这里只处理"全部轮次跑完"的自然收敛）
        var plan = await _db.TestPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == round.PlanId, ct);
        if (plan is not null && plan.Status == TestPlanStatus.Active)
        {
            var anyRunning = await _db.TestPlanRounds
                .AnyAsync(r => r.PlanId == round.PlanId && r.Status == PlanRoundStatus.Running, ct);
            if (!anyRunning)
            {
                var planEntity = await _db.TestPlans.FirstOrDefaultAsync(p => p.Id == round.PlanId, ct);
                if (planEntity is not null && planEntity.Status == TestPlanStatus.Active)
                {
                    planEntity.Status = TestPlanStatus.Completed;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("计划 {PlanId}（{PlanName}）所有轮次已结束，自动转态为 Completed",
                        round.PlanId, planEntity.Name);
                }
            }
        }

        return round.PlanId;
    }

    // ================================================================== 达标判定

    public async Task<PlanGateResult?> BuildGateAsync(Guid planId, CancellationToken ct)
    {
        var plan = await _db.TestPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return null;

        var rounds = await LoadRoundsRawAsync(planId, ct);

        // 缺陷验收门槛（P2）：项目级未闭环致命/严重缺陷计数，与缺陷模块统计同一口径
        var openCritical = 0;
        if (plan.DefectGateEnabled)
        {
            openCritical = await _db.Defects.AsNoTracking()
                .CountAsync(d => d.ProjectId == plan.ProjectId
                                 && (d.Severity == DefectSeverity.Critical || d.Severity == DefectSeverity.Major)
                                 && (d.Status == DefectStatus.New || d.Status == DefectStatus.Assigned || d.Status == DefectStatus.Fixed), ct);
        }

        return PlanGateEvaluator.Evaluate(plan.Name, plan.ReleaseName,
            plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure,
            plan.GateMode, rounds, plan.DefectGateEnabled, openCritical);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
