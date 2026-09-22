using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// M8 自愈度量（迭代 D2）：把 <see cref="AgentAttempt"/> 的落库轨迹聚合成可观测指标。
///
/// 设计取舍：
/// - 只做一次聚合查询 + 一次误判相关性查询（嵌套 EXISTS），避免 N+1；
/// - 时间窗口（from/to）作用于"自愈尝试的发生时间"，误判检测中的"后续执行"只要求 CreatedAt 更晚，不二次加窗口；
/// - 这是管理/看板用的低频端点，正确性优先于极致性能。
/// </summary>
public static class AgentHealMetrics
{
    public static async Task<AgentHealMetricsDto> ComputeAsync(
        TestDbContext db, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = db.AgentAttempts.AsNoTracking().AsQueryable();
        if (from is not null) q = q.Where(a => a.CreatedAt >= from.Value);
        if (to is not null) q = q.Where(a => a.CreatedAt <= to.Value);

        var byResult = await q.GroupBy(a => a.Result)
            .Select(g => new { Result = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int total = byResult.Sum(c => c.Count);
        int Fixed = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.Fixed)?.Count ?? 0;
        int Partial = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.Partial)?.Count ?? 0;
        int Failed = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.Failed)?.Count ?? 0;
        int Skipped = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.Skipped)?.Count ?? 0;
        int Rejected = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.Rejected)?.Count ?? 0;
        int BudgetExhausted = byResult.FirstOrDefault(c => c.Result == AgentAttemptResult.BudgetExhausted)?.Count ?? 0;
        int Other = total - Fixed - Partial - Failed - Skipped - Rejected - BudgetExhausted;

        double successRate = total == 0 ? 0 : (double)(Fixed + Partial) / total;

        double? avgFix = await q.Where(a => a.CompletedAt != null)
            .Select(a => (a.CompletedAt!.Value - a.CreatedAt).TotalMinutes)
            .AverageAsync(ct);

        var byCategory = await q.GroupBy(a => a.FixCategory)
            .Select(g => new { Cat = (int)g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Cat, g => g.Count, ct);

        // 误判检测：被标记「自愈通过(Fixed)」的步骤，在**同一用例后续执行**的相同 StepOrder 上再次失败。
        // 嵌套 Any → 嵌套 EXISTS，EF 可翻译。
        int misjudged = await (
            from a in q
            where a.Result == AgentAttemptResult.Fixed
            where db.Executions.Any(he => he.Id == a.ExecutionId && he.TestCaseId != null
                && db.Executions.Any(le => le.TestCaseId == he.TestCaseId
                    && le.CreatedAt > he.CreatedAt
                    && db.ExecutionResults.Any(lr => lr.ExecutionId == le.Id
                        && lr.StepOrder == a.TargetStepOrder
                        && (lr.Status == ExecutionStatus.Failed || lr.Status == ExecutionStatus.Error))))
            select a.Id).CountAsync(ct);

        return new AgentHealMetricsDto(
            total, Fixed, Partial, Failed, Skipped, Rejected, BudgetExhausted, Other,
            Math.Round(successRate, 4), avgFix, byCategory, misjudged);
    }
}
