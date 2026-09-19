namespace AI.TestPlatform.Application.Suites;

/// <summary>
/// 套件内「前置用例」依赖图的规范化与校验（执行编排）。
///
/// 为什么必须在保存时拦住环：前置未通过的用例会被编排层跳过、未开始的一直等前置，
/// 一旦 A→B→A，两条用例都会永远「等对方」，批次永远不会结束（既不跑完也报不出失败）。
/// 这种状态在运行时很难自愈，所以只能在写入时拒绝，并给出环的具体路径。
///
/// 只做纯计算（不碰数据库），名字通过 nameOf 回调注入——便于单测与复用。
/// </summary>
public static class SuiteDependencyGraph
{
    /// <summary>
    /// 规范化并校验依赖：去重、剔除空值、拒绝自依赖 / 指向套件外的依赖 / 环形依赖。
    /// 失败时 error 是可直接展示给用户的中文原因。
    /// </summary>
    public static bool TryNormalize(
        IReadOnlyList<SuiteCaseSpec>? cases,
        Func<Guid, string>? nameOf,
        out List<SuiteCaseSpec> normalized,
        out string? error)
    {
        normalized = new List<SuiteCaseSpec>();
        error = null;
        if (cases is null || cases.Count == 0)
            return true;

        // 去重：同一个用例在一个套件里只能出现一次（数据库上也是唯一索引），
        // 重复提交时以**先出现**的那条为准，避免后一条静默覆盖前一条的依赖配置。
        var seen = new HashSet<Guid>();
        foreach (var spec in cases)
        {
            if (!seen.Add(spec.TestCaseId))
                continue;
            normalized.Add(new SuiteCaseSpec(spec.TestCaseId,
                spec.DependsOnTestCaseId is { } dep && dep != Guid.Empty ? dep : null));
        }

        var ids = normalized.Select(s => s.TestCaseId).ToHashSet();
        var label = (Guid id) => nameOf?.Invoke(id) is { Length: > 0 } name ? name : id.ToString("N")[..8];

        foreach (var spec in normalized)
        {
            if (spec.DependsOnTestCaseId is not { } dep)
                continue;
            if (dep == spec.TestCaseId)
            {
                error = $"用例「{label(spec.TestCaseId)}」不能把自己设为前置用例";
                return false;
            }
            if (!ids.Contains(dep))
            {
                error = $"用例「{label(spec.TestCaseId)}」的前置用例不在本套件内，请先把它加入套件";
                return false;
            }
        }

        var map = normalized.ToDictionary(s => s.TestCaseId, s => s.DependsOnTestCaseId);
        if (TryFindCycle(map, out var cycle))
        {
            error = "前置依赖存在环：" + string.Join(" → ", cycle.Select(label));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 找出依赖环（存在则返回环上的用例顺序，首尾为同一条用例）。
    ///
    /// 每条用例最多只有一个前置，所以这张图是「若干条链」而不是一般有向图——
    /// 顺着链走、记住走过节点的位置即可：一旦走回本次已访问过的节点就是环。
    /// 比三色 DFS 简单得多，也更容易讲清「环是从哪开始的」。
    /// </summary>
    private static bool TryFindCycle(
        IReadOnlyDictionary<Guid, Guid?> edges, out List<Guid> cycle)
    {
        cycle = new List<Guid>();
        var settled = new HashSet<Guid>(); // 已经确认不在环上的节点，不必再走一遍

        foreach (var start in edges.Keys)
        {
            if (settled.Contains(start))
                continue;

            var walk = new List<Guid>();
            var position = new Dictionary<Guid, int>();
            var node = start;

            while (true)
            {
                if (position.TryGetValue(node, out var at))
                {
                    cycle = walk.Skip(at).ToList();
                    cycle.Add(node);
                    return true;
                }

                position[node] = walk.Count;
                walk.Add(node);

                if (!edges.TryGetValue(node, out var next) || next is not { } target ||
                    !edges.ContainsKey(target))
                    break;
                node = target;
            }

            foreach (var visited in walk)
                settled.Add(visited);
        }

        return false;
    }
}
