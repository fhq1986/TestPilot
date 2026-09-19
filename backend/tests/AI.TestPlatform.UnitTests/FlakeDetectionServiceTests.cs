using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class FlakeDetectionServiceTests
{
    private static List<ExecutionStatus> Repeat(ExecutionStatus status, int count)
        => Enumerable.Repeat(status, count).ToList();

    [Fact]
    public void 样本不足时不下结论()
    {
        var (isFlaky, rate) = FlakeDetectionService.Evaluate(
            new[] { ExecutionStatus.Passed, ExecutionStatus.Failed, ExecutionStatus.Failed });

        Assert.False(isFlaky);
        Assert.Equal(0d, rate);
    }

    [Fact]
    public void 全通过不算不稳定()
    {
        var (isFlaky, rate) = FlakeDetectionService.Evaluate(Repeat(ExecutionStatus.Passed, 10));
        Assert.False(isFlaky);
        Assert.Equal(0d, rate);
    }

    [Fact]
    public void 全失败不算不稳定_属真失败而非抖动()
    {
        var (isFlaky, rate) = FlakeDetectionService.Evaluate(Repeat(ExecutionStatus.Failed, 10));
        Assert.False(isFlaky);
        Assert.Equal(0d, rate);
    }

    [Fact]
    public void 通过失败各半最不稳定()
    {
        var statuses = new List<ExecutionStatus>
        {
            ExecutionStatus.Passed, ExecutionStatus.Failed,
            ExecutionStatus.Passed, ExecutionStatus.Failed,
            ExecutionStatus.Passed, ExecutionStatus.Failed,
        };

        var (isFlaky, rate) = FlakeDetectionService.Evaluate(statuses);

        Assert.True(isFlaky);
        Assert.Equal(1d, rate);
    }

    [Fact]
    public void 九成一通过未达阈值_不被标记()
    {
        var statuses = Repeat(ExecutionStatus.Passed, 10);
        statuses[0] = ExecutionStatus.Failed;

        var (isFlaky, rate) = FlakeDetectionService.Evaluate(statuses);

        Assert.False(isFlaky);
        Assert.Equal(0.2d, rate);
        Assert.True(rate < FlakeDetectionService.FlakyThreshold);
    }

    [Fact]
    public void Error与Failed同样计入未通过()
    {
        var statuses = new List<ExecutionStatus>
        {
            ExecutionStatus.Passed, ExecutionStatus.Error,
            ExecutionStatus.Passed, ExecutionStatus.Error,
        };

        var (isFlaky, rate) = FlakeDetectionService.Evaluate(statuses);

        Assert.True(isFlaky);
        Assert.Equal(1d, rate);
    }

    [Fact]
    public void Skipped不参与统计()
    {
        // 4 个有效样本（2 通过 2 失败）+ 10 个 Skipped，判定应与 50% 抖动一致
        var statuses = new List<ExecutionStatus>
        {
            ExecutionStatus.Passed, ExecutionStatus.Failed,
            ExecutionStatus.Passed, ExecutionStatus.Failed,
        };
        statuses.AddRange(Repeat(ExecutionStatus.Skipped, 10));

        var (isFlaky, rate) = FlakeDetectionService.Evaluate(statuses);

        Assert.True(isFlaky);
        Assert.Equal(1d, rate);
    }
}
