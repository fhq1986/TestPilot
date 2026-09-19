using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Executions;

/// <summary>
/// 一次套件运行里的执行快照（只带编排决策需要的字段）。
/// 有了它，决策逻辑就是纯函数：不碰数据库、不依赖时钟，可以整段单测。
/// </summary>
public record OrchestrationExecution(
    Guid Id, Guid? TestCaseId, ExecutionStatus Status, Guid? DependsOnTestCaseId);

/// <summary>一条编排跳过决策：把哪条执行跳过、原因写什么</summary>
public record OrchestrationSkip(Guid ExecutionId, string Reason);

/// <summary>
/// 套件执行的编排决策（前置依赖 + 失败策略）。
///
/// 只回答「这批执行里哪些应该被跳过」，不负责写库——写库条件更新在 ExecutionOrchestrator。
/// 这样「什么时候该跳过」可以用普通单测覆盖（含链式依赖、多浏览器展开、快停），
/// 而不需要真的把 Postgres 跑起来。
/// </summary>
public static class SuiteOrchestration
{
    /// <summary>前置用例未通过时的跳过原因（含前置名与它自己的结局）</summary>
    public static string DependencySkipReason(string dependsOnName, string outcome) =>
        $"前置用例「{dependsOnName}」{outcome}，本条已跳过";

    /// <summary>失败快停的跳过原因（整批共用）</summary>
    public const string StopOnFailureReason = "套件失败快停：本轮已出现失败/错误用例，未开始的用例全部跳过";

    /// <summary>
    /// 计算要跳过的执行。
    ///
    /// 两条规则，顺序不可交换：
    /// 1. 前置依赖：前置用例的结局**已经确定且不是通过**（失败/错误/被跳过）→ 跳过；
    ///    前置还在待执行/执行中 → 什么都不做（等它落地，抢占门禁会挡住本条）；
    /// 2. 失败快停：策略为 StopOnFailure 且本轮已有失败/错误 → 余下未开始的全部跳过。
    ///    放在依赖之后：先让依赖链自己得出「因为前置失败而跳过」，剩下的才是「因为快停而跳过」。
    ///
    /// 依赖规则要迭代到不动点：A 失败 → B 跳过（B 是被跳过的，不再是「未通过」以外的结局）
    /// → 依赖 B 的 C 也必须跳过。一轮扫不出链式效果，所以循环到没有新增为止。
    /// </summary>
    public static List<OrchestrationSkip> PlanSkips(
        IReadOnlyList<OrchestrationExecution> executions,
        SuiteFailurePolicy failurePolicy,
        Func<Guid, string>? nameOf = null)
    {
        var skips = new List<OrchestrationSkip>();
        if (executions.Count == 0)
            return skips;

        // 用例 → 它的各条执行（浏览器矩阵/数据行展开后一条用例会有多条）
        var byCase = executions
            .Where(e => e.TestCaseId is not null)
            .GroupBy(e => e.TestCaseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var label = (Guid id) => nameOf?.Invoke(id) is { Length: > 0 } name ? name : id.ToString("N")[..8];

        // 已经决定跳过、以及已经是最终结局的执行，都从「待决策」里逐步剔除
        var decided = new HashSet<Guid>();
        var pending = executions.Where(e => e.Status == ExecutionStatus.Pending).ToList();

        // ---- 规则 1：前置未通过（含链式）
        bool changed;
        do
        {
            changed = false;
            foreach (var execution in pending)
            {
                if (execution.DependsOnTestCaseId is not { } dependsOnId || decided.Contains(execution.Id))
                    continue;
                if (!byCase.TryGetValue(dependsOnId, out var dependsOnExecutions))
                    continue; // 前置用例没进这次运行（例如被判为不支持执行）→ 不拦，按无前置处理

                var outcome = ResolveOutcome(dependsOnExecutions, decided);
                if (outcome is null)
                    continue; // 仍在待执行/执行中：等它落地
                if (outcome == ExecutionStatus.Passed)
                    continue; // 前置已全部通过：本条保持待执行，抢占门禁会放它进来

                decided.Add(execution.Id);
                skips.Add(new OrchestrationSkip(execution.Id,
                    DependencySkipReason(label(dependsOnId), DescribeOutcome(outcome.Value))));
                changed = true;
            }
        } while (changed);

        // ---- 规则 2：失败快停
        if (failurePolicy == SuiteFailurePolicy.StopOnFailure &&
            executions.Any(e => e.Status is ExecutionStatus.Failed or ExecutionStatus.Error
                or ExecutionStatus.Canceled))
        {
            foreach (var execution in pending.Where(e => !decided.Contains(e.Id)))
            {
                decided.Add(execution.Id);
                skips.Add(new OrchestrationSkip(execution.Id, StopOnFailureReason));
            }
        }

        return skips;
    }

    /// <summary>
    /// 判定一个用例的结局。返回 null = 还有执行没落地（既不定通过也不定不通过）；
    /// 返回 Passed = 全部通过；返回其它状态 = 已确定「不是通过」，值即用来解释原因的决定性状态。
    ///
    /// 一条用例展开成多条执行时（浏览器矩阵 × 数据行）按**全部通过**才算通过：
    /// 只要有一条失败，依赖它的用例就不该继续跑——这正是「前置」要表达的语义。
    /// </summary>
    private static ExecutionStatus? ResolveOutcome(
        List<OrchestrationExecution> caseExecutions, HashSet<Guid> justDecided)
    {
        ExecutionStatus? blocking = null;

        foreach (var execution in caseExecutions)
        {
            // 本轮刚被判为跳过的，按「跳过」结局参与判定（链式传播靠这一句）
            var status = justDecided.Contains(execution.Id) ? ExecutionStatus.Skipped : execution.Status;
            switch (status)
            {
                case ExecutionStatus.Passed:
                    break;
                case ExecutionStatus.Failed:
                case ExecutionStatus.Error:
                case ExecutionStatus.Skipped:
                case ExecutionStatus.Canceled:
                    blocking ??= status;
                    break;
                default:
                    return null; // 还没落地
            }
        }

        return blocking ?? ExecutionStatus.Passed;
    }

    private static string DescribeOutcome(ExecutionStatus? status) => status switch
    {
        ExecutionStatus.Failed => "未通过（失败）",
        ExecutionStatus.Error => "未通过（执行错误）",
        ExecutionStatus.Skipped => "已被跳过",
        ExecutionStatus.Canceled => "已被手动终止",
        _ => "未通过",
    };
}
