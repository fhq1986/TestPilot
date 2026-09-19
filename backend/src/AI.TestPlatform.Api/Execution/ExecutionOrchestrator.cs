using AI.TestPlatform.Api.Hubs;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 编排落地层：把 <see cref="SuiteOrchestration"/> 的决策写回数据库。
///
/// 为什么是「定期扫 + 条件更新」而不是「在内存里给每条执行挂前置任务」：
/// 执行任务真实存在于数据库、由多个实例抢占（FOR UPDATE SKIP LOCKED），
/// 进程内对象活不过一次重启。把「谁该跳过」落成一次幂等的 SQL 更新，重启/多实例都安全。
///
/// 幂等性靠 WHERE "Status" = Pending：并发两次扫描只会有一条生效，重复扫描不会覆盖已跑完的结果。
/// </summary>
public class ExecutionOrchestrator
{
    private readonly TestDbContext _db;
    private readonly IHubContext<ExecutionHub> _hub;
    private readonly ILogger<ExecutionOrchestrator> _logger;

    public ExecutionOrchestrator(TestDbContext db, IHubContext<ExecutionHub> hub,
        ILogger<ExecutionOrchestrator> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// 扫描一次套件运行，跳过那些「前置未通过」或「因快停被中止」的待执行记录。
    /// 返回本次跳过的条数（0 表示无需处理，属于常态）。
    /// </summary>
    public async Task<int> SweepSuiteRunAsync(Guid suiteRunId, CancellationToken ct)
    {
        var executions = await _db.Executions.AsNoTracking()
            .Where(e => e.SuiteRunId == suiteRunId)
            .Select(e => new OrchestrationExecution(e.Id, e.TestCaseId, e.Status, e.DependsOnTestCaseId))
            .ToListAsync(ct);
        if (executions.Count == 0)
            return 0;
        // 没有待执行记录时无事可做：这一步挡掉了绝大多数空扫（跑完的批次会被反复扫到）
        if (!executions.Any(e => e.Status == ExecutionStatus.Pending))
            return 0;

        var policy = await ResolvePolicyAsync(suiteRunId, ct);
        var nameOf = await BuildNameLookupAsync(executions, ct);

        var skips = SuiteOrchestration.PlanSkips(executions, policy, nameOf);
        if (skips.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        var applied = new List<Guid>();
        // 按原因分组批量更新：同一原因下几十条执行只发一条 SQL
        foreach (var group in skips.GroupBy(s => s.Reason))
        {
            var ids = group.Select(s => s.ExecutionId).ToList();
            try
            {
                var affected = await _db.Executions
                    .Where(e => ids.Contains(e.Id) && e.Status == ExecutionStatus.Pending)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(e => e.Status, ExecutionStatus.Skipped)
                        .SetProperty(e => e.SkipReason, group.Key)
                        .SetProperty(e => e.StartedAt, e => e.StartedAt ?? now)
                        .SetProperty(e => e.EndedAt, now)
                        .SetProperty(e => e.DurationMs, 0)
                        .SetProperty(e => e.HeartbeatAt, (DateTime?)null), ct);
                if (affected > 0)
                    applied.AddRange(ids);
            }
            catch (Exception ex)
            {
                // 编排是「优化与提示」而非「正确性依赖」：扫不动只影响体验，不能让执行器停摆
                _logger.LogWarning(ex, "编排跳过执行失败（套件运行 {SuiteRunId}）", suiteRunId);
                return applied.Count;
            }
        }

        if (applied.Count == 0)
            return 0;

        _logger.LogInformation("套件运行 {SuiteRunId}：编排跳过 {Count} 条待执行（策略 {Policy}）",
            suiteRunId, applied.Count, policy);

        foreach (var executionId in applied)
            await TryPushSkippedAsync(executionId);

        return applied.Count;
    }

    /// <summary>取本次运行所属套件的失败策略；套件已删除时按 Continue（保守：不额外跳过任何东西）</summary>
    private async Task<SuiteFailurePolicy> ResolvePolicyAsync(Guid suiteRunId, CancellationToken ct)
    {
        var suiteId = await _db.Executions.AsNoTracking()
            .Where(e => e.SuiteRunId == suiteRunId && e.SuiteId != null)
            .Select(e => e.SuiteId)
            .FirstOrDefaultAsync(ct);
        if (suiteId is null)
            return SuiteFailurePolicy.Continue;

        return await _db.TestSuites.AsNoTracking()
            .Where(s => s.Id == suiteId.Value)
            .Select(s => s.FailurePolicy)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// 前置用例的名称（用于跳过原因）。用 IgnoreQueryFilters 是刻意的：
    /// 用例被软删除后名字依然要能显示出来，否则原因会退化成「前置用例「7f3a1b2c」未通过」这种没法读的文案。
    /// </summary>
    private async Task<Func<Guid, string>?> BuildNameLookupAsync(
        List<OrchestrationExecution> executions, CancellationToken ct)
    {
        var ids = executions
            .Where(e => e.DependsOnTestCaseId is not null)
            .Select(e => e.DependsOnTestCaseId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return null;

        var names = await _db.TestCases.IgnoreQueryFilters().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return id => names.TryGetValue(id, out var name) ? name : id.ToString("N")[..8];
    }

    private async Task TryPushSkippedAsync(Guid executionId)
    {
        try
        {
            await _hub.Clients.Group(executionId.ToString("N")).SendAsync(
                "StatusChanged", (int)ExecutionStatus.Skipped);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "推送编排跳过状态失败（{ExecutionId}）", executionId);
        }
    }
}
