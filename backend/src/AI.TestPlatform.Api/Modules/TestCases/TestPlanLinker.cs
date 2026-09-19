using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>
/// 把用例追加关联到测试计划（Excel 导入 / 脚本导入等入口共用）。
///
/// 追加去重：已在计划范围内的用例跳过（同一批用例重复导入不会在计划里堆重复项），
/// 执行顺序接在现有条目之后。
///
/// 刻意**不抛异常**：关联是导入的附加动作，失败不能拖垮已成功的用例落库——
/// 返回 (0, 0) 并把原因写进 warnings（调用方把 warnings 展示给用户即可）。
/// </summary>
public static class TestPlanLinker
{
    public static async Task<(int Linked, int Skipped)> LinkAsync(
        TestDbContext db, ILogger logger, Guid? testPlanId,
        IReadOnlyCollection<Guid> caseIds, List<string>? warnings, CancellationToken ct)
    {
        if (testPlanId is not { } planId || caseIds.Count == 0)
            return (0, 0);

        try
        {
            var plan = await db.TestPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan is null)
            {
                warnings?.Add("所选测试计划不存在，已跳过关联");
                return (0, 0);
            }

            var existing = await db.TestPlanItems.AsNoTracking()
                .Where(i => i.PlanId == planId)
                .Select(i => new { i.TestCaseId, i.Order })
                .ToListAsync(ct);
            var existingCases = existing.Select(i => i.TestCaseId).ToHashSet();

            var order = existing.Count == 0 ? 0 : existing.Max(i => i.Order) + 1;
            var linked = 0;
            var skippedInPlan = 0;
            foreach (var caseId in caseIds)
            {
                if (!existingCases.Add(caseId))
                {
                    skippedInPlan++;
                    continue;
                }
                db.TestPlanItems.Add(new TestPlanItem { PlanId = planId, TestCaseId = caseId, Order = order++ });
                linked++;
            }

            if (linked > 0)
            {
                await db.SaveChangesAsync(ct);
                await db.TestPlans.Where(p => p.Id == planId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
            }

            warnings?.Add(linked > 0
                ? $"已关联测试计划「{plan.Name}」：新增 {linked} 条" +
                  (skippedInPlan > 0 ? $"，{skippedInPlan} 条已在计划范围内跳过" : "")
                : $"测试计划「{plan.Name}」已包含本次导入的全部用例，无需重复关联");
            return (linked, skippedInPlan);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "导入用例关联测试计划 {PlanId} 失败", planId);
            warnings?.Add("关联测试计划失败，可稍后在测试计划详情页手动加入用例");
            return (0, 0);
        }
    }
}
