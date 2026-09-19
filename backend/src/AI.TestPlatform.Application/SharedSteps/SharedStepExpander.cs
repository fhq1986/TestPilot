using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Application.Executions;

namespace AI.TestPlatform.Application.SharedSteps;

/// <summary>
/// 共享步骤展开：把用例里「引用了共享步骤组」的占位步骤，替换成组内的真实步骤，并重排序号。
///
/// 为什么在运行时展开而不是保存用例时展开：
/// 保存时展开等于把组的副本固化进每个用例，"改一处全生效"就失效了——
/// 那和复制粘贴没有区别。运行时展开才能让共享步骤组真正成为单一事实来源。
///
/// 展开是**纯函数**：不碰数据库、不改入参，方便单测与在多个调用点复用。
/// </summary>
public static class SharedStepExpander
{
    /// <summary>
    /// 展开步骤列表。
    /// </summary>
    /// <param name="steps">用例的步骤（未排序也可，内部会按 StepOrder 排序）</param>
    /// <param name="groups">被引用的共享步骤组，键为组 ID</param>
    /// <param name="warnings">收集展开放弃的引用（组被删 / 组内无步骤），便于在结果里给用户交代</param>
    public static List<TestStep> Expand(
        IEnumerable<TestStep> steps,
        IReadOnlyDictionary<Guid, SharedStepGroup> groups,
        List<string>? warnings = null)
    {
        var expanded = new List<TestStep>();
        var order = 0;

        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            if (step.SharedGroupId is null)
            {
                expanded.Add(Clone(step, order++));
                continue;
            }

            if (!groups.TryGetValue(step.SharedGroupId.Value, out var group))
            {
                warnings?.Add($"第 {step.StepOrder + 1} 步引用的共享步骤组已不存在，已跳过");
                continue;
            }

            var items = group.Items.OrderBy(i => i.StepOrder).ToList();
            if (items.Count == 0)
            {
                warnings?.Add($"共享步骤组「{group.Name}」内没有任何步骤，已跳过");
                continue;
            }

            // 变量优先级（低 → 高）：组内默认值 → 执行期变量（数据集行 / 执行参数） → 本引用覆盖。
            // 「本引用覆盖」最高是因为它是最具体的意图表达：某个用例明确写了要用什么值，
            // 就不该被组默认值或数据集行改写。
            var variables = BuildVariables(group, step.SharedVariables);

            foreach (var item in items)
            {
                expanded.Add(new TestStep
                {
                    TestCaseId = step.TestCaseId,
                    StepOrder = order++,
                    ActionType = item.ActionType,
                    // 组内步骤里的 {{变量}} 在展开时就地解析：展开后的步骤是自洽的，
                    // 下游执行器不需要知道它来自共享步骤组
                    Config = StepVariableResolver.Resolve(item.Config, variables) ?? item.Config,
                    AIInstruction = item.AIInstruction,
                    AIElementDescription = item.AIElementDescription,
                    CreatedAt = step.CreatedAt,
                });
            }
        }

        return expanded;
    }

    /// <summary>
    /// 合并变量。返回字典的键大小写不敏感，与 StepVariableResolver 的查找语义保持一致
    /// （否则 {{Login}} 和 {{login}} 会一个命中一个不命中）。
    /// </summary>
    public static Dictionary<string, string> BuildVariables(
        SharedStepGroup group,
        IReadOnlyList<SharedVariableEntry>? overrides)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in group.Variables)
        {
            if (!string.IsNullOrWhiteSpace(entry.Name)) variables[entry.Name.Trim()] = entry.Value;
        }
        if (overrides is not null)
        {
            foreach (var entry in overrides)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name)) variables[entry.Name.Trim()] = entry.Value;
            }
        }
        return variables;
    }

    /// <summary>
    /// 复制步骤并改写序号。必须复制：同一个 TestStep 实例被塞进结果列表两次会让
    /// 下游按引用去重/改号的逻辑出现难以排查的串味。
    /// </summary>
    private static TestStep Clone(TestStep step, int order) => new()
    {
        Id = step.Id,
        TestCaseId = step.TestCaseId,
        StepOrder = order,
        ActionType = step.ActionType,
        Config = step.Config,
        AIInstruction = step.AIInstruction,
        AIElementDescription = step.AIElementDescription,
        CreatedAt = step.CreatedAt,
    };

    /// <summary>用例引用了哪些共享步骤组（批量查组时用，避免逐个查库）</summary>
    public static List<Guid> ReferencedGroupIds(IEnumerable<TestStep> steps) =>
        steps.Where(s => s.SharedGroupId is not null)
            .Select(s => s.SharedGroupId!.Value)
            .Distinct()
            .ToList();
}
