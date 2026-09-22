using System.Text.Json;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 解析 k6 的 summary JSON（迭代 F·P2-9）。
///
/// k6 有**两种**summary 结构，本类必须同时吃下（实测 k6 v2.3.0）：
///   ① <c>--summary-export=FILE</c>（k6 原生，扁平）：
///      <c>{ "metrics": { "http_reqs": { "count": 80, "rate": 79.4 } } }</c>
///      —— rate 类指标聚合值叫 <c>value</c>；阈值是布尔，且 **true = 未通过**；
///      没有 <c>state</c>，所以拿不到 testRunDurationMs。
///   ② 脚本里 <c>handleSummary(data)</c> 写出的（嵌套）：
///      <c>{ "metrics": { "http_reqs": { "type": "counter", "values": { "count": 80 } } },
///         "state": { "testRunDurationMs": 1007 } }</c>
///      —— 数值在 <c>values</c> 下；阈值是 <c>{ "ok": true }</c>。
/// 之前只按 ② 写、却优先读 ①，结果是**所有指标静默归零**（请求数 0、p99 缺失、
/// 阈值全判失败）——不报错，只是数字全错，比缺指标更难发现。故此处两种形状都认。
///
/// 两条原则：
/// ① **全字段容错**：summary 的结构随 k6 版本与 <c>summaryTrendStats</c> 配置而变，
///    键可能整批缺失。一律 TryGetValue，取不到就留 null——宁可少一个指标，也不要假指标。
/// ② **p99 可能是 null**：k6 默认只统计到 p(95)，只有脚本声明了 summaryTrendStats 才有 p(99)。
///    这里不兜 0（0 会被读成"很快"），留 null 让前端显示"未采集"。
/// </summary>
public static class K6SummaryParser
{
    /// <summary>解析结果。字段名与 <see cref="LoadTestRun"/> 的指标列一一对应</summary>
    public sealed record ParsedMetrics(
        long TotalRequests,
        double? Rps,
        double? AvgMs,
        double? P50Ms,
        double? P95Ms,
        double? P99Ms,
        double? MaxMs,
        double? ErrorRate,
        double? ChecksRate,
        long Iterations,
        int? VusMax,
        int? TestRunDurationMs,
        bool? ThresholdsPassed,
        int ThresholdTotal,
        int ThresholdFailed,
        List<LoadTestThresholdResult> ThresholdResults);

    /// <summary>解析 summary JSON。解析失败返回 null（调用方据此落 Error，而不是编造指标）</summary>
    public static ParsedMetrics? Parse(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(summaryJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var metrics = root.TryGetProperty("metrics", out var m) && m.ValueKind == JsonValueKind.Object
                ? m
                : default;

            var httpReqs = Metric(metrics, "http_reqs");
            var duration = Metric(metrics, "http_req_duration");
            var failed = Metric(metrics, "http_req_failed");
            var checks = Metric(metrics, "checks");
            var iterations = Metric(metrics, "iterations");
            var vusMax = Metric(metrics, "vus_max");

            // 阈值结论分散在各 metric 的 thresholds 字段里，需要逐个指标收集
            var results = new List<LoadTestThresholdResult>();
            if (metrics.ValueKind == JsonValueKind.Object)
            {
                foreach (var metric in metrics.EnumerateObject())
                {
                    if (!metric.Value.TryGetProperty("thresholds", out var thresholds) ||
                        thresholds.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var threshold in thresholds.EnumerateObject())
                    {
                        results.Add(new LoadTestThresholdResult
                        {
                            Metric = metric.Name,
                            Expression = threshold.Name,
                            Ok = IsThresholdOk(threshold.Value),
                        });
                    }
                }
            }

            int? runDurationMs = null;
            if (root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object
                && TryNumber(state, "testRunDurationMs", out var dur))
                runDurationMs = (int)dur;

            return new ParsedMetrics(
                TotalRequests: (long)(Value(httpReqs, "count") ?? 0),
                Rps: Rate(httpReqs),
                AvgMs: Value(duration, "avg"),
                P50Ms: Value(duration, "med"),
                P95Ms: Value(duration, "p(95)"),
                P99Ms: Value(duration, "p(99)"),
                MaxMs: Value(duration, "max"),
                ErrorRate: Rate(failed),
                ChecksRate: Rate(checks),
                Iterations: (long)(Value(iterations, "count") ?? 0),
                VusMax: vusMax.ValueKind == JsonValueKind.Object && TryNumber(vusMax, "max", out var vmax)
                    ? (int)vmax
                    : null,
                TestRunDurationMs: runDurationMs,
                ThresholdsPassed: results.Count == 0 ? null : results.All(r => r.Ok),
                ThresholdTotal: results.Count,
                ThresholdFailed: results.Count(r => !r.Ok),
                ThresholdResults: results);
        }
    }

    /// <summary>
    /// 取指标的「数值视图」：嵌套形状在 <c>values</c> 下，扁平形状数值直接挂在指标对象上。
    /// 两种都返回同一层，后面的 <see cref="Value"/> 调用不必关心来源。
    /// </summary>
    private static JsonElement Metric(JsonElement metrics, string name)
    {
        if (metrics.ValueKind != JsonValueKind.Object
            || !metrics.TryGetProperty(name, out var metric)
            || metric.ValueKind != JsonValueKind.Object)
            return default;

        // 嵌套形状（handleSummary）：{ type, contains, values: {...}, thresholds: {...} }
        if (metric.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
            return values;

        // 扁平形状（--summary-export）：{ count, rate, thresholds, ... } 数值与 thresholds 同级
        return metric;
    }

    /// <summary>
    /// 阈值是否通过。两种形状的语义**相反**，认错就会把「全过」显示成「全失败」：
    ///   - handleSummary：<c>{ "ok": true }</c> → ok 为 true 表示通过
    ///   - --summary-export：裸布尔，且 **true 表示失败**（k6 的既有语义）
    /// </summary>
    private static bool IsThresholdOk(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => false,   // 扁平形状：true = 未通过
        JsonValueKind.False => true,   // 扁平形状：false = 通过
        JsonValueKind.Object => value.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
        _ => false,                    // 认不出来就别报「通过」
    };

    /// <summary>
    /// rate 类指标的聚合值：嵌套形状给 <c>rate</c>，扁平形状给 <c>value</c>（实测两者只居其一）。
    /// 只认 rate 的话，扁平来源下 checks / http_req_failed 的比率会整批变 null。
    /// </summary>
    private static double? Rate(JsonElement values) => Value(values, "rate") ?? Value(values, "value");

    /// <summary>从 values 对象里取数值；键不存在或类型不对都返回 null（不抛）</summary>
    private static double? Value(JsonElement values, string key)
    {
        if (values.ValueKind != JsonValueKind.Object) return null;
        if (!values.TryGetProperty(key, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number ? prop.GetDouble() : null;
    }

    private static bool TryNumber(JsonElement obj, string key, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Number) return false;
        value = prop.GetDouble();
        return true;
    }
}
