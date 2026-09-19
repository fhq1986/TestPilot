using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.Suites;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Suites;

/// <summary>
/// 套件运行：一次「冒烟集 / 回归集 / 发版必跑集」的执行。
///
/// 每次运行生成一个新的 SuiteRunId，本次运行创建的所有执行记录都带上它，
/// 于是「按套件运行聚合报告 / 看历史通过率」只需要按 SuiteRunId 查一次。
/// </summary>
public class SuiteRunner
{
    private readonly TestDbContext _db;
    private readonly ExecutionPlanner _planner;
    private readonly ExecutionQueue _queue;
    private readonly ILogger<SuiteRunner> _logger;

    public SuiteRunner(TestDbContext db, ExecutionPlanner planner, ExecutionQueue queue,
        ILogger<SuiteRunner> logger)
    {
        _db = db;
        _planner = planner;
        _queue = queue;
        _logger = logger;
    }

    public async Task<SuiteRunResult> RunAsync(Guid suiteId, RunSuiteRequest request,
        TriggerType triggerType, string? triggerSource, Guid? triggeredById, CancellationToken ct)
    {
        var suite = await _db.TestSuites
            .Include(s => s.Cases)
            .FirstOrDefaultAsync(s => s.Id == suiteId, ct);
        if (suite is null)
            return new SuiteRunResult(suiteId, Guid.Empty, 0, new List<Guid>(), 0, "套件不存在");

        var caseIds = suite.Cases.OrderBy(c => c.Order).Select(c => c.TestCaseId).ToList();
        if (caseIds.Count == 0)
        {
            suite.LastError = "套件内没有用例";
            suite.LastRunAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new SuiteRunResult(suiteId, Guid.Empty, 0, new List<Guid>(), 0, "套件内没有用例");
        }

        var suiteRunId = Guid.NewGuid();
        var environmentId = request.EnvironmentId ?? suite.EnvironmentId;
        try
        {
            var plan = await _planner.PlanAsync(
                caseIds, environmentId, request.Browsers, request.ExpandDataSets, request.Variables,
                triggerType, triggerSource ?? $"测试套件：{suite.Name}", triggeredById,
                suiteId: suite.Id, suiteRunId: suiteRunId, ct: ct);

            if (plan.Executions.Count == 0)
            {
                suite.LastError = "套件内没有可执行的用例（移动端用例不支持执行）";
                suite.LastRunAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return new SuiteRunResult(suiteId, suiteRunId, 0, new List<Guid>(), 0, suite.LastError);
            }

            _db.Executions.AddRange(plan.Executions);
            suite.LastRunAt = DateTime.UtcNow;
            suite.LastSuiteRunId = suiteRunId;
            suite.LastCreatedCount = plan.Executions.Count;
            suite.LastError = null;
            suite.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            foreach (var execution in plan.Executions)
                await _queue.EnqueueAsync(execution.Id, ct);

            _logger.LogInformation("套件 {Name}（{SuiteId}）运行 {RunId}：创建 {Count} 条执行",
                suite.Name, suite.Id, suiteRunId, plan.Executions.Count);

            return new SuiteRunResult(suiteId, suiteRunId, plan.Executions.Count,
                plan.Executions.Select(e => e.Id).ToList(), plan.CasesWithoutData, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "套件 {SuiteId} 运行失败", suiteId);
            suite.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            suite.LastRunAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new SuiteRunResult(suiteId, suiteRunId, 0, new List<Guid>(), 0, ex.Message);
        }
    }

    /// <summary>套件的历史运行汇总（按 SuiteRunId 聚合）</summary>
    public async Task<List<SuiteRunSummaryDto>> HistoryAsync(Guid suiteId, int take, CancellationToken ct)
    {
        var runs = await _db.Executions.AsNoTracking()
            .Where(e => e.SuiteId == suiteId && e.SuiteRunId != null)
            .GroupBy(e => e.SuiteRunId!.Value)
            .Select(g => new
            {
                SuiteRunId = g.Key,
                StartedAt = g.Min(e => e.CreatedAt),
                Total = g.Count(),
                Passed = g.Count(e => e.Status == ExecutionStatus.Passed),
                Failed = g.Count(e => e.Status == ExecutionStatus.Failed),
                Error = g.Count(e => e.Status == ExecutionStatus.Error),
                Skipped = g.Count(e => e.Status == ExecutionStatus.Skipped),
                // 因编排（前置未通过 / 快停）跳过的条数：与「其它原因跳过」区分开，
                // 界面上要能回答「这次为什么少了这么多条执行」
                OrchestrationSkipped = g.Count(e => e.Status == ExecutionStatus.Skipped && e.SkipReason != null),
                Pending = g.Count(e => e.Status == ExecutionStatus.Pending || e.Status == ExecutionStatus.Running),
                DurationMs = g.Sum(e => e.DurationMs ?? 0),
                Source = g.Max(e => e.TriggerSource),
            })
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct);

        return runs.Select(r =>
        {
            var scored = r.Passed + r.Failed + r.Error;
            return new SuiteRunSummaryDto(
                r.SuiteRunId, suiteId, r.StartedAt,
                r.Total, r.Passed, r.Failed, r.Error, r.Skipped, r.Pending,
                scored == 0 ? 0 : Math.Round(r.Passed * 100.0 / scored, 1),
                r.DurationMs, r.Source, r.OrchestrationSkipped);
        }).ToList();
    }
}
