using AI.TestPlatform.Application.Suites;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 套件内「前置用例」依赖图的规范化与校验。
///
/// 重点是拦住环形依赖：A→B→A 会让两条用例都永远等对方，批次既不结束也不报错，
/// 只能靠人工发现批次卡住——这种状态在运行期几乎无法自愈，所以必须写入时就拒绝。
/// </summary>
public class SuiteDependencyGraphTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static string? Normalize(List<SuiteCaseSpec> specs, out List<SuiteCaseSpec> normalized) =>
        SuiteDependencyGraph.TryNormalize(specs, NameOf, out normalized, out var error) ? null : error;

    private static string NameOf(Guid id) =>
        id == A ? "订单创建" : id == B ? "订单支付" : id == C ? "订单退款" : id.ToString("N")[..8];

    [Fact]
    public void 合法链式依赖通过且保持顺序()
    {
        var error = Normalize(new List<SuiteCaseSpec>
        {
            new(A),
            new(B, A),
            new(C, B),
        }, out var normalized);

        Assert.Null(error);
        Assert.Equal(new[] { A, B, C }, normalized.Select(s => s.TestCaseId));
        Assert.Null(normalized[0].DependsOnTestCaseId);
        Assert.Equal(A, normalized[1].DependsOnTestCaseId);
        Assert.Equal(B, normalized[2].DependsOnTestCaseId);
    }

    [Fact]
    public void 依赖可以指向列表中靠后的用例()
    {
        // 依赖方向与列表顺序无关：编排只看依赖关系，不要求「前置必须排在前面」
        var error = Normalize(new List<SuiteCaseSpec> { new(A, C), new(C) }, out var normalized);

        Assert.Null(error);
        Assert.Equal(C, normalized[0].DependsOnTestCaseId);
    }

    [Fact]
    public void 自依赖被拒绝()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A, A) }, out _);

        Assert.NotNull(error);
        Assert.Contains("不能把自己设为前置", error!);
        Assert.Contains("订单创建", error!);
    }

    [Fact]
    public void 指向套件外的依赖被拒绝()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A), new(B, C) }, out _);

        Assert.NotNull(error);
        Assert.Contains("不在本套件内", error!);
    }

    [Fact]
    public void 环形依赖被拒绝并指出环的路径()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A, B), new(B, A) }, out _);

        Assert.NotNull(error);
        Assert.Contains("环", error!);
        // 报错要能直接指路：只写「存在环形依赖」的话，上百条用例里根本找不到是哪两条
        Assert.Contains("订单创建", error!);
        Assert.Contains("订单支付", error!);
    }

    [Fact]
    public void 三元环也被拒绝()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A, C), new(B, A), new(C, B) }, out _);

        Assert.NotNull(error);
        Assert.Contains("环", error!);
    }

    [Fact]
    public void 重复用例去重且保留先出现的依赖配置()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A, B), new(B), new(A) }, out var normalized);

        Assert.Null(error);
        Assert.Equal(2, normalized.Count);
        Assert.Equal(B, normalized.Single(s => s.TestCaseId == A).DependsOnTestCaseId);
    }

    [Fact]
    public void 空依赖与空Guid按无前置处理()
    {
        var error = Normalize(new List<SuiteCaseSpec> { new(A, Guid.Empty) }, out var normalized);

        Assert.Null(error);
        Assert.Null(normalized[0].DependsOnTestCaseId);
    }

    [Fact]
    public void 空集合直接通过()
    {
        Assert.Null(Normalize(new List<SuiteCaseSpec>(), out var empty));
        Assert.Empty(empty);

        Assert.True(SuiteDependencyGraph.TryNormalize(null, null, out var fromNull, out var nullError));
        Assert.Empty(fromNull);
        Assert.Null(nullError);
    }
}
