using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 不稳定用例（flake）识别。
///
/// 判定口径：取该用例最近 <see cref="SampleSize"/> 次「已结束」执行的通过率 p，
/// 不稳定度 = 2 * min(p, 1 - p)，取值 [0,1]：
/// - 全通过 / 全失败 → 0（结果稳定，只是恰好一直失败，属真失败而非 flake）
/// - 通过失败各半 → 1（最不稳定）
/// 当样本数不少于 <see cref="MinSampleSize"/> 且不稳定度不小于阈值时标记 IsFlaky。
///
/// 注意：这是「结果抖动」识别，同一提交上的必然失败不会误判为 flaky，
/// 因为全失败的不稳定度为 0。
/// </summary>
public class FlakeDetectionService
{
    /// <summary>采样窗口：最近多少次已结束的执行</summary>
    public const int SampleSize = 10;

    /// <summary>判定所需最小样本数（样本太少不下结论）</summary>
    public const int MinSampleSize = 4;

    /// <summary>标记为不稳定的不稳定度阈值</summary>
    public const double FlakyThreshold = 0.3;

    private readonly TestDbContext _db;

    public FlakeDetectionService(TestDbContext db) => _db = db;

    /// <summary>重算指定用例的 flake 指标并落库（幂等）</summary>
    public async Task<bool> RefreshAsync(Guid testCaseId, CancellationToken ct)
    {
        var statuses = await _db.Executions.AsNoTracking()
            .Where(e => e.TestCaseId == testCaseId &&
                        e.Status != ExecutionStatus.Pending &&
                        e.Status != ExecutionStatus.Running)
            .OrderByDescending(e => e.CreatedAt)
            .Take(SampleSize)
            .Select(e => e.Status)
            .ToListAsync(ct);

        var (isFlaky, rate) = Evaluate(statuses);

        var testCase = await _db.TestCases.FirstOrDefaultAsync(t => t.Id == testCaseId, ct);
        if (testCase is null) return false;

        var changed = testCase.IsFlaky != isFlaky || Math.Abs(testCase.FlakeRate - rate) >= 0.001;
        // 结论没变时不必每次执行都写库，但「最近统计时间」需要保持新鲜（一天至少刷新一次），
        // 否则界面上会长期显示一个过期的时间戳。
        var stale = testCase.FlakeCheckedAt is null ||
                    DateTime.UtcNow - testCase.FlakeCheckedAt.Value >= TimeSpan.FromDays(1);
        if (!changed && !stale) return false;

        testCase.IsFlaky = isFlaky;
        testCase.FlakeRate = rate;
        testCase.FlakeCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>纯函数：按执行状态序列评估是否 flaky 与不稳定度（便于单测）</summary>
    public static (bool IsFlaky, double FlakeRate) Evaluate(IReadOnlyCollection<ExecutionStatus> statuses)
    {
        // 只统计明确通过/未通过的样本；Skipped 不参与（多半是上游失败导致未执行）
        var scored = statuses.Where(s => s is ExecutionStatus.Passed or ExecutionStatus.Failed or ExecutionStatus.Error)
            .ToList();
        if (scored.Count < MinSampleSize) return (false, 0d);

        var passCount = scored.Count(s => s == ExecutionStatus.Passed);
        var passRate = (double)passCount / scored.Count;
        var rate = Math.Round(2 * Math.Min(passRate, 1 - passRate), 4);
        return (rate >= FlakyThreshold, rate);
    }
}
