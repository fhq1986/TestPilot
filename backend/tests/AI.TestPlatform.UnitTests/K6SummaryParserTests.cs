using AI.TestPlatform.Api.LoadTesting;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// k6 summary 解析（迭代 F·P2-9）。
///
/// 两个用例的 JSON 都是**真实抓取**的 k6 v2.3.0 输出（不是照着自己的解析器编的）：
/// 曾经只按 handleSummary 的嵌套形状实现、运行时却优先读 --summary-export 的扁平形状，
/// 结果所有指标静默归零、阈值全判失败——所以这里必须两种形状都钉住。
/// </summary>
public class K6SummaryParserTests
{
    /// <summary>--summary-export：数值与 thresholds 同级，rate 类指标聚合值叫 value</summary>
    private const string FlatExport = """
    {
      "root_group": {},
      "metrics": {
        "http_reqs": { "count": 80, "rate": 79.43997037262473 },
        "http_req_duration": {
          "p(99)": 14.539735189999996, "avg": 11.953626012499996, "min": 10.006255,
          "med": 11.7400915, "max": 14.935477, "p(90)": 13.538105900000005,
          "p(95)": 14.15950405, "thresholds": { "p(95)<3000": false }
        },
        "http_req_failed": { "passes": 0, "fails": 80, "value": 0 },
        "checks": { "passes": 80, "fails": 0, "thresholds": { "rate>0.9": false }, "value": 1 },
        "iterations": { "rate": 79.43997037262473, "count": 80 },
        "vus_max": { "value": 1, "min": 1, "max": 1 }
      }
    }
    """;

    /// <summary>handleSummary(data)：数值在 values 下，阈值是 { ok }</summary>
    private const string NestedHandleSummary = """
    {
      "root_group": {},
      "state": { "isStdOutTTY": false, "testRunDurationMs": 1007.049721 },
      "metrics": {
        "http_reqs": { "type": "counter", "contains": "default", "values": { "rate": 79.43997037262473, "count": 80 } },
        "http_req_duration": {
          "type": "trend", "contains": "time",
          "values": { "p(99)": 14.539735189999996, "avg": 11.953626012499996, "min": 10.006255,
                      "med": 11.7400915, "max": 14.935477, "p(95)": 14.15950405 },
          "thresholds": { "p(95)<3000": { "ok": true } }
        },
        "http_req_failed": { "type": "rate", "contains": "default", "values": { "rate": 0, "passes": 0, "fails": 80 } },
        "checks": { "type": "rate", "contains": "default", "values": { "rate": 1, "passes": 80, "fails": 0 },
                    "thresholds": { "rate>0.9": { "ok": true } } },
        "iterations": { "type": "counter", "contains": "default", "values": { "count": 80, "rate": 79.43997037262473 } },
        "vus_max": { "type": "gauge", "contains": "default", "values": { "min": 1, "max": 1, "value": 1 } }
      }
    }
    """;

    [Fact]
    public void 扁平summary_指标不归零()
    {
        var m = K6SummaryParser.Parse(FlatExport);

        Assert.NotNull(m);
        Assert.Equal(80, m!.TotalRequests);
        Assert.Equal(80, m.Iterations);
        Assert.Equal(1, m.VusMax);
        Assert.Equal(79.43997037262473, m.Rps!.Value, 3);
        // 这些以前全是 null —— 扁平形状没有 rate 键，聚合值叫 value
        Assert.Equal(1, m.ChecksRate!.Value, 3);
        Assert.Equal(0, m.ErrorRate!.Value, 3);
    }

    [Fact]
    public void 扁平summary_趋势分位可取()
    {
        var m = K6SummaryParser.Parse(FlatExport)!;

        Assert.Equal(14.5397, m.P99Ms!.Value, 3);
        Assert.Equal(14.1595, m.P95Ms!.Value, 3);
        Assert.Equal(11.7400, m.P50Ms!.Value, 3);
        Assert.Equal(14.9354, m.MaxMs!.Value, 3);
        Assert.Equal(11.9536, m.AvgMs!.Value, 3);
    }

    [Fact]
    public void 扁平summary_阈值false表示通过()
    {
        // k6 的 --summary-export 里阈值是裸布尔，且 true = 未通过。
        // 认反了会把「全过」显示成「全失败」，并把运行判成 Failed。
        var m = K6SummaryParser.Parse(FlatExport)!;

        Assert.Equal(2, m.ThresholdTotal);
        Assert.Equal(0, m.ThresholdFailed);
        Assert.True(m.ThresholdsPassed);
        Assert.All(m.ThresholdResults, r => Assert.True(r.Ok));
    }

    [Fact]
    public void 嵌套summary_阈值ok对象表示通过()
    {
        var m = K6SummaryParser.Parse(NestedHandleSummary)!;

        Assert.Equal(2, m.ThresholdTotal);
        Assert.Equal(0, m.ThresholdFailed);
        Assert.True(m.ThresholdsPassed);
    }

    [Fact]
    public void 嵌套summary_指标与时长()
    {
        var m = K6SummaryParser.Parse(NestedHandleSummary)!;

        Assert.Equal(80, m.TotalRequests);
        Assert.Equal(79.4399, m.Rps!.Value, 3);
        Assert.Equal(14.5397, m.P99Ms!.Value, 3);
        Assert.Equal(1, m.ChecksRate!.Value, 3);
        Assert.Equal(1007, m.TestRunDurationMs);
    }

    [Fact]
    public void 阈值失败时结论为未通过()
    {
        var json = FlatExport.Replace("\"p(95)<3000\": false", "\"p(95)<3000\": true");

        var m = K6SummaryParser.Parse(json)!;

        Assert.Equal(1, m.ThresholdFailed);
        Assert.False(m.ThresholdsPassed);
    }

    [Fact]
    public void 缺少p99时留null而不是零()
    {
        // 脚本没声明 summaryTrendStats 就没有 p(99)：留 null 让前端显示「未采集」，
        // 兜 0 会被读成「很快」，是假指标。
        var json = FlatExport.Replace("\"p(99)\": 14.539735189999996, ", "");

        var m = K6SummaryParser.Parse(json)!;

        Assert.Null(m.P99Ms);
        Assert.Equal(14.1595, m.P95Ms!.Value, 3);
    }

    [Fact]
    public void 没有阈值声明时结论为null()
    {
        var json = FlatExport
            .Replace(", \"thresholds\": { \"p(95)<3000\": false }", "")
            .Replace(", \"thresholds\": { \"rate>0.9\": false }", "");

        var m = K6SummaryParser.Parse(json)!;

        Assert.Equal(0, m.ThresholdTotal);
        Assert.Null(m.ThresholdsPassed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void 坏输入不抛异常(string? json)
    {
        var m = K6SummaryParser.Parse(json);

        if (json is "{}") Assert.NotNull(m);   // 空对象是合法 JSON，只是没指标
        else Assert.Null(m);
    }
}
