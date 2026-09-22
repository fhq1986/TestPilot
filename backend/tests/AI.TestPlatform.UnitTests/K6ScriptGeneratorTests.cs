using System.Text.Json;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// k6 脚本生成（迭代 F·P2-9）。
///
/// 这组用例盯的是三件「错了就会静默出问题」的事：
/// ① 脚本哈希必须稳定（它决定「脚本是否已过期」，抖了就会无谓地反复重新生成）；
/// ② p(99) 必须在 summaryTrendStats 里（漏了 p99 永远采不到，而它是最常被问的数）；
/// ③ 带引号/反斜杠的请求体经 {变量} 替换后仍必须是合法 JS（否则脚本语法错误、整场压测跑不起来，
///    而且朴素替换等于开了 JS 注入口子）。
/// </summary>
public class K6ScriptGeneratorTests
{
    private static K6ScriptInput Input(
        IReadOnlyList<K6CaseSpec>? cases = null,
        LoadTestProfile? profile = null,
        IReadOnlyList<LoadTestThreshold>? thresholds = null,
        IReadOnlyDictionary<string, string>? variables = null) =>
        new("订单接口压测",
            "http://127.0.0.1:5210",
            variables ?? new Dictionary<string, string> { ["username"] = "admin" },
            profile ?? new LoadTestProfile(),
            thresholds ?? new List<LoadTestThreshold>
            {
                new() { Metric = "http_req_duration", Aggregator = "p(95)", Operator = "<", Value = 500 },
                new() { Metric = "http_req_failed", Aggregator = "rate", Operator = "<", Value = 0.01m },
            },
            cases ?? new List<K6CaseSpec> { Case("登录接口") });

    private static K6CaseSpec Case(string name) => new(name, new List<K6StepSpec>
    {
        new(1, new K6RequestSpec("POST", "/api/auth/login",
            new List<K6HeaderSpec> { new("Content-Type", "application/json") },
            """{"username":"{username}","password":"p@ss"}"""), null, null, null),
        new(2, null, new K6AssertSpec("200", """{"code":0}"""), null, null),
        new(3, null, null, new K6ExtractSpec("token", "$.data.token"), null),
    });

    [Fact]
    public void 同输入两次生成的脚本哈希必须一致()
    {
        var a = K6ScriptGenerator.Generate(Input());
        var b = K6ScriptGenerator.Generate(Input());

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(64, a.Hash.Length); // SHA256 十六进制
    }

    [Fact]
    public void 必须声明p99否则永远采不到()
    {
        var script = K6ScriptGenerator.Generate(Input()).Script;

        Assert.Contains("summaryTrendStats", script);
        Assert.Contains("'p(99)'", script);
    }

    [Fact]
    public void 含引号与反斜杠的请求体替换变量后仍是合法JS()
    {
        // 这段 body 里同时有双引号、反斜杠和占位符——朴素 Replace 会直接产出非法 JS
        var body = """{"user":"{name}","path":"C:\\tmp","quote":"say \"hi\""}""";
        var cases = new List<K6CaseSpec>
        {
            new("转义用例", new List<K6StepSpec>
            {
                new(1, new K6RequestSpec("POST", "/api/x", new List<K6HeaderSpec>(), body), null, null, null),
            }),
        };

        var script = K6ScriptGenerator.Generate(Input(cases: cases)).Script;

        // 占位符被翻成运行时取值，而不是被替换成裸值
        Assert.Contains("vars('name', ctx)", script);

        // 每个字符串字面量块都必须能被 JSON 解析——JSON 字符串即合法 JS 字符串字面量，
        // 反过来说：解析得通就证明引号/反斜杠都转义对了
        var expr = K6ScriptGenerator.ToJsExpression(body);
        var literals = expr.Split(" + ").Where(p => p.StartsWith('"')).ToList();
        Assert.NotEmpty(literals);
        foreach (var literal in literals)
        {
            using var _ = JsonDocument.Parse(literal); // 转义错会抛 JsonException
        }
    }

    [Fact]
    public void 状态码断言_2xx_应映射成区间判断()
    {
        var cases = new List<K6CaseSpec>
        {
            new("区间用例", new List<K6StepSpec>
            {
                new(1, new K6RequestSpec("GET", "/api/x", new List<K6HeaderSpec>(), null), null, null, null),
                new(2, null, new K6AssertSpec("2xx", null), null, null),
            }),
        };

        var script = K6ScriptGenerator.Generate(Input(cases: cases)).Script;

        Assert.Contains("r.status >= 200 && r.status < 300", script);
    }

    [Fact]
    public void 绝对URL不再拼BASE_URL_相对路径要拼()
    {
        var cases = new List<K6CaseSpec>
        {
            new("URL用例", new List<K6StepSpec>
            {
                new(1, new K6RequestSpec("GET", "https://example.com/ping", new List<K6HeaderSpec>(), null), null, null, null),
                new(2, new K6RequestSpec("GET", "/api/rel", new List<K6HeaderSpec>(), null), null, null, null),
            }),
        };

        var script = K6ScriptGenerator.Generate(Input(cases: cases)).Script;

        Assert.Contains("http.request(\"GET\", \"https://example.com/ping\"", script);
        Assert.Contains("BASE_URL + \"/api/rel\"", script);
    }

    [Fact]
    public void 不支持的动作为告警而非静默丢弃()
    {
        var cases = new List<K6CaseSpec>
        {
            new("混合用例", new List<K6StepSpec>
            {
                new(1, null, null, null, "Click"),
                new(2, new K6RequestSpec("GET", "/api/x", new List<K6HeaderSpec>(), null), null, null, null),
            }),
        };

        var result = K6ScriptGenerator.Generate(Input(cases: cases));

        Assert.Single(result.Warnings);
        Assert.Contains("Click", result.Warnings[0]);
        Assert.Contains("/api/x", result.Script); // 支持的那步仍然生成
    }

    [Fact]
    public void 告警不应重复出现()
    {
        // 生成器内部若对同一份输入跑两遍（例如为算哈希多跑一次），告警会被追加两次
        var cases = new List<K6CaseSpec>
        {
            new("重复用例", new List<K6StepSpec>
            {
                new(1, null, null, null, "Click"),
            }),
        };

        var result = K6ScriptGenerator.Generate(Input(cases: cases));

        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("http_req_duration", "p(95)", "<", 500, "p(95)<500")]
    [InlineData("http_req_failed", "rate", "<", 0.01, "rate<0.01")]
    [InlineData("checks", "rate", ">", 0.95, "rate>0.95")]
    public void 阈值渲染回k6原生表达式(string metric, string agg, string op, double value, string expected)
    {
        var t = new LoadTestThreshold { Metric = metric, Aggregator = agg, Operator = op, Value = (decimal)value };
        Assert.Equal(expected, K6ScriptGenerator.RenderThreshold(t));
    }

    [Fact]
    public void 非法的阈值聚合方式或比较符应被拒绝()
    {
        Assert.Null(K6ScriptGenerator.RenderThreshold(new LoadTestThreshold { Aggregator = "median" }));
        Assert.Null(K6ScriptGenerator.RenderThreshold(new LoadTestThreshold { Operator = "==" }));
    }

    [Fact]
    public void 三种executor都应生成对应的k6配置()
    {
        var ramping = K6ScriptGenerator.Generate(Input()).Script;
        Assert.Contains("executor: 'ramping-vus'", ramping);

        var constant = K6ScriptGenerator.Generate(Input(profile: new LoadTestProfile
        {
            Kind = "constant-vus", Vus = 20, Duration = "2m",
        })).Script;
        Assert.Contains("executor: 'constant-vus'", constant);
        Assert.Contains("vus: 20", constant);

        var arrival = K6ScriptGenerator.Generate(Input(profile: new LoadTestProfile
        {
            Kind = "constant-arrival-rate", Rate = 50, TimeUnit = "1s", PreAllocatedVUs = 30, MaxVUs = 80,
        })).Script;
        Assert.Contains("executor: 'constant-arrival-rate'", arrival);
        Assert.Contains("rate: 50", arrival);
        Assert.Contains("maxVUs: 80", arrival);
    }

    [Fact]
    public void 期望响应体不是合法JSON时应告警并跳过该断言()
    {
        var cases = new List<K6CaseSpec>
        {
            new("坏断言", new List<K6StepSpec>
            {
                new(1, new K6RequestSpec("GET", "/api/x", new List<K6HeaderSpec>(), null), null, null, null),
                new(2, null, new K6AssertSpec(null, "{ not json"), null, null),
            }),
        };

        var result = K6ScriptGenerator.Generate(Input(cases: cases));

        Assert.Contains(result.Warnings, w => w.Contains("不是合法 JSON"));
        Assert.DoesNotContain("subset({ not json", result.Script);
    }
}
