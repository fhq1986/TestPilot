using AI.TestPlatform.Api.Observability;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// /metrics 的标签映射契约。
///
/// 这类"把枚举映射成字符串"的代码有个共同的失效模式：**漏一个取值不会报错**，
/// 那个状态会被静默归到兜底值里。指标表面上完全正常，但一个独立状态被合并掉了——
/// 本轮就真漏了 <see cref="ExecutionStatus.Canceled"/>（"被手动终止"和"未知"不是一回事）。
/// 所以这里遍历枚举来钉住。
/// </summary>
public class MetricsLabelTests
{
    [Fact]
    public void 每个执行状态都有专属标签且不落到unknown()
    {
        var labels = Enum.GetValues<ExecutionStatus>()
            .Select(s => (Status: s, Label: MetricsEndpoint.StatusLabel(s)))
            .ToList();

        var unknown = labels.Where(x => x.Label == "unknown").Select(x => x.Status).ToList();
        Assert.Empty(unknown);

        // 标签还要互不重复：两个状态映成同一个名字，等于又合并了一次
        var duplicated = labels.GroupBy(x => x.Label).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToList();
        Assert.Empty(duplicated);
    }

    [Fact]
    public void 终止状态有专属标签()
        // 单点确认一下：这个是最容易被漏掉的那个（它排在枚举末尾，加的时候常被忘）
        => Assert.Equal("canceled", MetricsEndpoint.StatusLabel(ExecutionStatus.Canceled));
}
