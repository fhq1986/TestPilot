using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 套件执行编排（前置依赖 + 失败策略）的决策逻辑。
///
/// 这段逻辑决定了「一批执行里哪些根本不跑」，出错的方式很隐蔽：
/// 判早了会把还没跑的用例误判为前置未通过、判晚了整批会卡在待执行等一个永远不会来的前置。
/// 所以每个状态组合都单独压一遍，尤其是「前置还在执行中」和「链式传播」两处。
/// </summary>
public class SuiteOrchestrationTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static OrchestrationExecution Exec(Guid caseId, ExecutionStatus status,
        Guid? dependsOn = null) =>
        new(Guid.NewGuid(), caseId, status, dependsOn);

    private static Func<Guid, string> Namer() => id =>
        id == A ? "订单创建" : id == B ? "订单支付" : "订单退款";

    // ------------------------------ 前置依赖

    [Fact]
    public void 前置失败时跳过依赖用例()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Failed),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        var skip = Assert.Single(skips);
        Assert.Contains("订单创建", skip.Reason);
        Assert.Contains("失败", skip.Reason);
    }

    [Fact]
    public void 前置错误时也跳过依赖用例()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Error),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        var skip = Assert.Single(skips);
        Assert.Contains("执行错误", skip.Reason);
    }

    [Fact]
    public void 前置被手动终止时跳过依赖用例()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Canceled),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        var skip = Assert.Single(skips);
        Assert.Contains("手动终止", skip.Reason);
    }

    [Fact]
    public void 快停策略下手动终止也触发快停()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Canceled),
            Exec(B, ExecutionStatus.Pending),
        }, SuiteFailurePolicy.StopOnFailure, Namer());

        // 终止的 A 已是终态不产出决策；快停把未开始的 B 跳过
        var skip = Assert.Single(skips);
        Assert.Equal(SuiteOrchestration.StopOnFailureReason, skip.Reason);
    }

    [Fact]
    public void 前置还在执行中时不跳过也不放行()
    {
        // 这条最容易写错：前置还在跑（Pending/Running），此时既不能说它通过、也不能说过不了，
        // 决策结果必须是「什么都不做」——抢占门禁会挡住依赖用例，等前置落地后再扫一次。
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Running),
            Exec(B, ExecutionStatus.Pending, A),
            Exec(C, ExecutionStatus.Pending, B),
        }, SuiteFailurePolicy.StopOnFailure, Namer());

        Assert.Empty(skips);
    }

    [Fact]
    public void 前置全部通过时不跳过()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Passed),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        Assert.Empty(skips);
    }

    [Fact]
    public void 链式依赖逐级跳过()
    {
        // A 失败 → B 因前置失败被跳过 → C 依赖 B，也必须跟着跳过。
        // 一轮扫描只能判出 B，所以决策必须迭代到不动点，否则 C 会永远停在待执行。
        var pendingB = Exec(B, ExecutionStatus.Pending, A);
        var pendingC = Exec(C, ExecutionStatus.Pending, B);

        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Failed),
            pendingB,
            pendingC,
        }, SuiteFailurePolicy.Continue, Namer());

        Assert.Equal(2, skips.Count);
        Assert.Contains(skips, s => s.ExecutionId == pendingB.Id && s.Reason.Contains("订单创建"));
        Assert.Contains(skips, s => s.ExecutionId == pendingC.Id && s.Reason.Contains("订单支付"));
    }

    [Fact]
    public void 前置展开成多条执行时必须全部通过()
    {
        // 浏览器矩阵/数据行会把一条用例展开成多条执行。只要有一条没通过，
        // 前置就不能算通过——「前置」要表达的正是这个语义。
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Passed),
            Exec(A, ExecutionStatus.Failed),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        Assert.Single(skips);
    }

    [Fact]
    public void 前置被跳过时依赖用例也跳过()
    {
        // 前置本身因为别的编排原因被跳过（残留的 Skipped 状态）→ 依赖它的也不该跑
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Skipped),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        var skip = Assert.Single(skips);
        Assert.Contains("已被跳过", skip.Reason);
    }

    [Fact]
    public void 前置用例不在本次运行时不作拦截()
    {
        // 前置用例被计划期剔除（例如移动端用例不支持执行）时本条按无前置处理：
        // 否则它会去等一个本次根本不跑的用例，永远卡在待执行。
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue, Namer());

        Assert.Empty(skips);
    }

    // ------------------------------ 失败策略

    [Fact]
    public void 快停策略下有失败则跳过全部未开始用例()
    {
        var pendingB = Exec(B, ExecutionStatus.Pending);
        var pendingC = Exec(C, ExecutionStatus.Pending, A);

        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Failed),
            Exec(C, ExecutionStatus.Passed),
            pendingB,
            pendingC,
        }, SuiteFailurePolicy.StopOnFailure, Namer());

        Assert.Equal(2, skips.Count);
        // 无依赖的那条也要跳过：快停针对整个批次，而不仅是依赖链下游
        Assert.Contains(skips, s => s.ExecutionId == pendingB.Id &&
            s.Reason == SuiteOrchestration.StopOnFailureReason);
        // 依赖链下游则是「前置未通过」优先——它先被判定了，就不会再被快停覆盖
        Assert.Contains(skips, s => s.ExecutionId == pendingC.Id && s.Reason.Contains("订单创建"));
    }

    [Fact]
    public void 快停策略下没有失败则不跳过()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Passed),
            Exec(B, ExecutionStatus.Pending),
        }, SuiteFailurePolicy.StopOnFailure, Namer());

        Assert.Empty(skips);
    }

    [Fact]
    public void 继续策略下有失败也不因快停跳过()
    {
        // Continue 是升级前的既有语义：一条失败不影响同批其它用例照跑
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Failed),
            Exec(B, ExecutionStatus.Pending),
        }, SuiteFailurePolicy.Continue, Namer());

        Assert.Empty(skips);
    }

    [Fact]
    public void 快停不因前置跳过而触发()
    {
        // 前置未通过导致的下游跳过不是「本轮出现失败」，不该再触发快停去砍掉其余无关用例
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Skipped),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.StopOnFailure, Namer());

        var skip = Assert.Single(skips);
        Assert.NotEqual(SuiteOrchestration.StopOnFailureReason, skip.Reason);
    }

    // ------------------------------ 边界

    [Fact]
    public void 空批次与全部落地时不产出决策()
    {
        Assert.Empty(SuiteOrchestration.PlanSkips([], SuiteFailurePolicy.StopOnFailure, Namer()));
        Assert.Empty(SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Passed),
            Exec(B, ExecutionStatus.Failed),
        }, SuiteFailurePolicy.StopOnFailure, Namer()));
    }

    [Fact]
    public void 没有名称查询时用用例编号前缀兜底()
    {
        var skips = SuiteOrchestration.PlanSkips(new[]
        {
            Exec(A, ExecutionStatus.Failed),
            Exec(B, ExecutionStatus.Pending, A),
        }, SuiteFailurePolicy.Continue);

        var skip = Assert.Single(skips);
        Assert.Contains("aaaaaaaa", skip.Reason);
    }
}
