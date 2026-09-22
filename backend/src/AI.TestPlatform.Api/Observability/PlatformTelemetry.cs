using System.Diagnostics;

namespace AI.TestPlatform.Api.Observability;

/// <summary>
/// 平台自有的 <see cref="ActivitySource"/>（迭代 E·③）。
///
/// 这是"执行链路埋点"的唯一入口：一次执行的完整链路
/// （HTTP 触发 → ExecutionWorker 认领 → TestRunner 跑 Playwright → AIClient 调 AIWorker）
/// 都挂在同一 trace 上，span 带 <c>execution.id</c> / <c>node</c> 标签，
/// 便于回答"某次慢执行到底卡在哪一步"。
///
/// 说明：AIWorker 是独立 Python 进程，其内部不会生成 span；但 C# 侧的 HttpClient 埋点会自动
/// 注入 W3C <c>traceparent</c>，AIWorker 记录该头即可把日志关联回同一条 trace（跨进程不强求导出）。
/// </summary>
public static class PlatformTelemetry
{
    public const string ServiceName = "AI.TestPlatform";

    /// <summary>自定义 span 的活动源；已在 <see cref="ObservabilityExtensions"/> 里注册进 OTel。</summary>
    public static readonly ActivitySource Source = new(ServiceName);
}
