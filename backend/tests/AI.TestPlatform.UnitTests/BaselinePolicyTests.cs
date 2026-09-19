using AI.TestPlatform.Api.Visual;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 「哪张截图可以当视觉基线」的单测。
///
/// 这条规则值得单独锁住，因为它曾经缺失：建立基线的两条路径（执行时自动建立、人工接受变化）
/// 都没有校验步骤状态，于是"用例首次执行就失败"时错误页被存成基线，
/// 之后每次执行都跟错误页比对、一直显示"无变化"，真实问题反而被掩盖。
///
/// 同时它**不能是简单的"要求步骤通过"**——视觉差异本身会把步骤改判为 Failed，
/// 而「接受变化」要处理的正是这种失败。下面单独为这个例外留了用例，
/// 免得以后有人"顺手收紧"成只放行 Passed，把那个按钮改死。
/// </summary>
public class BaselinePolicyTests
{
    [Theory]
    [InlineData(VisualStatus.BaselineCreated)]
    [InlineData(VisualStatus.Unchanged)]
    [InlineData(VisualStatus.Changed)]
    [InlineData(VisualStatus.Skipped)]
    public void 步骤通过时可以建立基线(VisualStatus visualStatus)
        => Assert.True(BaselinePolicy.CanBecomeBaseline(ExecutionStatus.Passed, visualStatus));

    [Fact]
    public void 视觉差异导致的失败必须放行否则接受变化永远点不动()
    {
        // 判定为 Changed 时 ApplyAsync 会把步骤改判为 Failed，
        // 所以「接受变化」面对的必然是这个组合——不放行就等于功能不可用
        Assert.True(BaselinePolicy.CanBecomeBaseline(ExecutionStatus.Failed, VisualStatus.Changed));
    }

    [Theory]
    [InlineData(VisualStatus.BaselineCreated)]
    [InlineData(VisualStatus.Unchanged)]
    [InlineData(VisualStatus.Skipped)]
    public void 出于非视觉原因失败的步骤不能当基线(VisualStatus visualStatus)
    {
        // 这是修复的核心：失败步骤的截图很可能是错误页，
        // 固化成基准之后后续执行会一直"无变化"，比不做视觉回归更糟
        Assert.False(BaselinePolicy.CanBecomeBaseline(ExecutionStatus.Failed, visualStatus));
    }

    [Theory]
    [InlineData(VisualStatus.BaselineCreated)]
    [InlineData(VisualStatus.Unchanged)]
    [InlineData(VisualStatus.Changed)]
    [InlineData(VisualStatus.Skipped)]
    public void 执行报错的步骤一律不能当基线(VisualStatus visualStatus)
    {
        // Error 是环境异常 / 脚本抛错，截图大概率是异常页面，
        // 即使比对结论是 Changed 也不接受（那条 Changed 判断本身建立在异常页上）
        Assert.False(BaselinePolicy.CanBecomeBaseline(ExecutionStatus.Error, visualStatus));
    }

    [Theory]
    [InlineData(ExecutionStatus.Skipped)]
    [InlineData(ExecutionStatus.Running)]
    [InlineData(ExecutionStatus.Pending)]
    public void 没有有效截图的中间状态不能当基线(ExecutionStatus status)
        => Assert.False(BaselinePolicy.CanBecomeBaseline(status, VisualStatus.Changed));

    [Theory]
    [InlineData(ExecutionStatus.Failed, "失败")]
    [InlineData(ExecutionStatus.Error, "报错")]
    [InlineData(ExecutionStatus.Skipped, "跳过")]
    public void 拒绝原因是一句能看懂的中文(ExecutionStatus status, string keyword)
    {
        var reason = BaselinePolicy.DescribeRejection(status);

        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains(keyword, reason);
    }
}
