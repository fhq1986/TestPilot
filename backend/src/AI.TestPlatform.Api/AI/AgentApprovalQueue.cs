using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// Agent 审批**待办队列**的写入侧规则：同一用例只保留最新一条待审批建议。
///
/// 为什么需要：<see cref="AgentAttempt"/> 是"某次执行的自愈轨迹"，唯一键是
/// (ExecutionId, AttemptNumber)——同一用例反复失败就会攒出多条待审批，内容还高度相似。
/// 待审批列表的消费单位是"这个用例有个待办"，堆多条会让人对着同一个用例反复采纳，
/// 而 add_step 不是幂等操作，采纳两次就把同样的步骤插两遍。
///
/// 为什么不直接不生成：轨迹表出现空洞会丢掉"这次执行失败过、也分析过"的事实。
/// 所以只把旧的那条标记为 <see cref="AgentAttemptResult.Superseded"/>，记录仍在。
/// </summary>
public static class AgentApprovalQueue
{
    /// <summary>
    /// 把 <paramref name="testCaseId"/> 下尚未审批的旧建议标记为「已被取代」，返回被标记的条数。
    ///
    /// 刻意不 SaveChanges：让"作废旧建议"与"插入新建议"在同一次提交里落库，
    /// 中途异常时两者一起回滚，不会留下"旧建议被作废、新建议却没记录"的窟窿。
    /// </summary>
    public static async Task<int> SupersedePendingAsync(
        TestDbContext db, Guid testCaseId, Guid currentAttemptId, CancellationToken ct)
    {
        var stale = await db.AgentAttempts
            .Where(a => a.Id != currentAttemptId
                && a.NeedsApproval
                // 已有人工结论的不动：那是历史决定，不能被后续建议"翻案"
                && a.Approved == null
                && a.Result != AgentAttemptResult.Superseded
                && a.Execution != null
                && a.Execution.TestCaseId == testCaseId)
            .ToListAsync(ct);
        if (stale.Count == 0)
            return 0;

        foreach (var s in stale)
            s.Result = AgentAttemptResult.Superseded;

        return stale.Count;
    }
}
