using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Observability;

/// <summary>
/// <c>GET /metrics</c>：Prometheus 文本格式的运行指标（最小集）。
///
/// 定位是"让跑得稳不稳可见"：队列积压多少、在跑多少、有几个节点活着、
/// 执行耗时落在什么区间。此前这些只能翻日志，扩容与排障全靠猜。
///
/// **刻意不引入 OpenTelemetry / Prometheus 客户端库**：那几个包会带进一整套采集与导出模型，
/// 而这里真正需要的只是四个数——从库里现算一次就够了（一次 15~30 秒的抓取，代价可以忽略）。
/// 等指标种类真的多起来再上库也不迟。
///
/// 鉴权：<c>Metrics:Token</c>；**不配置就不暴露该端点**（404）。
/// 用独立的头 <c>X-Metrics-Token</c> 而不是复用 Webhook token——
/// 抓取方与 CI 触发是两件不同的事，共用一个密钥会让"谁能看指标"和"谁能触发执行"绑死。
/// </summary>
public static class MetricsEndpoint
{
    /// <summary>耗时分位统计的时间窗口（小时）。用滑动窗口是因为全量累计会让指标无上限增长</summary>
    private const int WindowHours = 24;

    /// <summary>窗口内参与直方图统计的执行数上限，防止极端情况下一次抓取拉回过多行</summary>
    private const int MaxSamples = 200_000;

    /// <summary>耗时直方图的桶边界（秒）</summary>
    private static readonly double[] Buckets = [1, 5, 15, 30, 60, 120, 300, 600];

    public static void MapMetrics(this WebApplication app, IConfiguration configuration)
    {
        var expectedToken = configuration["Metrics:Token"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            // 不配置 = 不暴露。不给出"默认开的、无鉴权的运维端点"——
            // 那等于把所有运行细节挂到公网上。
            return;
        }

        app.MapGet("/metrics", async (HttpContext http, TestDbContext db, IOptions<ExecutionOptions> options,
            CancellationToken ct) =>
        {
            var provided = http.Request.Headers["X-Metrics-Token"].FirstOrDefault();
            if (!TokenEquals(provided, expectedToken))
                return Results.Unauthorized();

            // M8：AI 可用性熔断状态（0=Closed 1=HalfOpen 2=Open）——"AI 有没有在降级"一眼可见
            var breakerState = http.RequestServices.GetRequiredService<AILivenessBreaker>().StateCode;
            var text = await RenderAsync(db, options.Value, breakerState, ct);
            // Prometheus 文本格式的 Content-Type 必须带 version，否则抓取端会拒绝
            return Results.Text(text, "text/plain; version=0.0.4; charset=utf-8");
        })
        // 全局限流是按用户/IP 分桶的：抓取方与办公网出口 NAT 撞同一个桶时，
        // 指标会因为"别人用得多"而抓不到——那是纯噪声，直接免疫
        .DisableRateLimiting();
    }

    private static async Task<string> RenderAsync(TestDbContext db, ExecutionOptions options,
        int aiCircuitState, CancellationToken ct)
    {
        var sb = new StringBuilder(2048);

        // ------------------------------ 队列与并发（瞬时值）
        var queueDepth = await db.Executions.CountAsync(e => e.Status == ExecutionStatus.Pending, ct);
        var running = await db.Executions.CountAsync(e => e.Status == ExecutionStatus.Running, ct);

        // 节点存活窗口：连续错过 3 次心跳就认为它已经掉线
        var aliveSince = DateTime.UtcNow.AddSeconds(-3 * Math.Max(1, options.HeartbeatSeconds));
        var aliveNodes = await db.ExecutionNodes
            .Where(n => n.LastHeartbeatAt >= aliveSince)
            .Select(n => new { n.MaxConcurrency, n.RunningCount })
            .ToListAsync(ct);

        Gauge(sb, "aitest_execution_queue_depth", "待执行的执行数（Pending）", queueDepth);
        Gauge(sb, "aitest_execution_running", "正在执行的执行数（Running）", running);
        Gauge(sb, "aitest_nodes_alive", $"心跳正常的执行节点数（窗口 {3 * Math.Max(1, options.HeartbeatSeconds)} 秒）",
            aliveNodes.Count);
        Gauge(sb, "aitest_node_capacity", "在线节点声明的并发上限之和", aliveNodes.Sum(n => n.MaxConcurrency));
        Gauge(sb, "aitest_node_running", "在线节点上报的运行中执行数之和", aliveNodes.Sum(n => n.RunningCount));

        // AI 可用性熔断状态：AI 挂掉时全局短路，运维需要能一眼看到"当前是否在降级"
        Gauge(sb, "aitest_ai_circuit_state", "AI 可用性熔断状态（0=Closed 1=HalfOpen 2=Open）", aiCircuitState);

        // ------------------------------ 累计执行数（真正的计数型：只增不减）
        var byStatus = await db.Executions
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        sb.AppendLine("# HELP aitest_executions_total 执行总数（按状态累计，只增不减）");
        sb.AppendLine("# TYPE aitest_executions_total counter");
        foreach (var row in byStatus.OrderBy(r => r.Status))
        {
            sb.Append("aitest_executions_total{status=\"")
              .Append(StatusLabel(row.Status)).Append("\"} ").AppendLine(row.Count.ToString(CultureInfo.InvariantCulture));
        }

        // ------------------------------ 耗时分布（滑动窗口）
        // ⚠ 刻意用 gauge 而不是 histogram：窗口是滑动的，_count 会随老数据滑出而**下降**，
        // 而 Prometheus 的 histogram 被假定只增不减——一旦下降会被当成"计数器重置"，
        // rate()/histogram_quantile 会算出完全错误的数字。名字里带 window24h 是让人一眼看出语义。
        var cutoff = DateTime.UtcNow.AddHours(-WindowHours);
        var durations = await db.Executions
            .Where(e => e.EndedAt != null && e.EndedAt >= cutoff && e.DurationMs != null)
            .OrderByDescending(e => e.EndedAt)
            .Take(MaxSamples)
            .Select(e => e.DurationMs!.Value)
            .ToListAsync(ct);

        var seconds = durations.Select(ms => ms / 1000.0).ToList();
        sb.AppendLine($"# HELP aitest_execution_duration_seconds_window{WindowHours}h " +
                      $"近 {WindowHours} 小时已完成执行的耗时分布（滑动窗口，非累计计数）");
        sb.AppendLine($"# TYPE aitest_execution_duration_seconds_window{WindowHours}h gauge");
        foreach (var bound in Buckets)
        {
            var count = seconds.Count(s => s <= bound);
            sb.Append($"aitest_execution_duration_seconds_window{WindowHours}h_bucket{{le=\"")
              .Append(bound.ToString("0.###", CultureInfo.InvariantCulture)).Append("\"} ")
              .AppendLine(count.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append($"aitest_execution_duration_seconds_window{WindowHours}h_bucket{{le=\"+Inf\"}} ")
          .AppendLine(seconds.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append($"aitest_execution_duration_seconds_window{WindowHours}h_sum ")
          .AppendLine(seconds.Sum().ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append($"aitest_execution_duration_seconds_window{WindowHours}h_count ")
          .AppendLine(seconds.Count.ToString(CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static void Gauge(StringBuilder sb, string name, string help, int value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
        sb.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 状态名用小写英文，避免中文 label 值在各种面板/告警规则里被转义折腾。
    ///
    /// ⚠ **必须覆盖 <see cref="ExecutionStatus"/> 的全部取值**。漏一个的后果是那个状态
    /// 全被归到 <c>unknown</c>：指标看着有、数字也像回事，但实际把一个独立状态悄悄合并掉了
    /// （本轮就漏过 <see cref="ExecutionStatus.Canceled"/>——"被手动终止"和"未知"完全不是一回事）。
    /// 有单测遍历枚举钉住这一点。
    ///
    /// 公开仅为可测。
    /// </summary>
    public static string StatusLabel(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Pending => "pending",
        ExecutionStatus.Running => "running",
        ExecutionStatus.Passed => "passed",
        ExecutionStatus.Failed => "failed",
        ExecutionStatus.Error => "error",
        ExecutionStatus.Skipped => "skipped",
        ExecutionStatus.Canceled => "canceled",
        _ => "unknown",
    };

    /// <summary>定长比较，避免用响应时间差猜 token</summary>
    private static bool TokenEquals(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
