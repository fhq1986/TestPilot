using System.Diagnostics;
using AI.TestPlatform.Api.Observability;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 迭代 E·③ 可观测性的契约测试：钉住 <see cref="PlatformTelemetry.Source"/> 真能产出 span，
/// 且执行链路 span 带 execution.id / node 标签（模板来自 ExecutionWorker.ProcessAsync）。
/// </summary>
public class IterationEObservabilityTests
{
    [Fact]
    public void ActivitySource_可产出带执行标签的span()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == PlatformTelemetry.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var executionId = Guid.NewGuid();
        using (var activity = PlatformTelemetry.Source.StartActivity("execution.process"))
        {
            activity?.SetTag("execution.id", executionId);
            activity?.SetTag("node", "test-node:1");
        }

        var span = Assert.Single(captured);
        Assert.Equal("execution.process", span.OperationName);
        Assert.Equal(PlatformTelemetry.ServiceName, PlatformTelemetry.Source.Name);
        Assert.Equal(executionId.ToString(), span.GetTagItem("execution.id")?.ToString());
        Assert.Equal("test-node:1", span.GetTagItem("node")?.ToString());
    }

    [Fact]
    public void 服务名常量稳定()
    {
        Assert.Equal("AI.TestPlatform", PlatformTelemetry.ServiceName);
        Assert.Equal(PlatformTelemetry.ServiceName, PlatformTelemetry.Source.Name);
    }
}
