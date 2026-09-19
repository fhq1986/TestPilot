using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AI.TestPlatform.IntegrationTests;

public sealed class AIWorkerStubHandler : HttpMessageHandler
{
    private const string DefaultLocateResult =
        "{\"matched_index\": 1, \"confidence\": 0.9, \"reasoning\": \"stub\"}";
    private const string DefaultAssertResult =
        "{\"passed\": true, \"reason\": \"stub ok\", \"confidence\": 0.9}";
    private const string DefaultDiagnoseResult =
        "{\"category\":\"断言失败\",\"root_cause\":\"期望文本不匹配\",\"confidence\":0.85,\"suggested_fix\":\"检查期望文本\",\"retry_recommended\":false}";

    // 按请求体中的 description 配置 locate-element 响应（无配置用默认值）
    public static readonly ConcurrentDictionary<string, string> LocateResults = new();
    // 按请求体中的 expectation 配置 ai-assert 响应（无配置用默认值）
    public static readonly ConcurrentDictionary<string, string> AssertResults = new();
    // 按请求体中 test_case.name 配置 diagnose-failure 响应（无配置用默认值）
    public static readonly ConcurrentDictionary<string, string> DiagnoseResults = new();
    // 按请求路径记录最近一次收到的 llm_config（JSON 原文），供断言验证 DB 配置透传
    public static readonly ConcurrentDictionary<string, string> LastLlmConfigs = new();

    private int _locateCallCount;
    private int _assertCallCount;
    private int _assertEvidenceShapeChecked;
    private int _flowCallCount;

    public int LocateCallCount => Volatile.Read(ref _locateCallCount);
    public int AssertCallCount => Volatile.Read(ref _assertCallCount);
    public int FlowCallCount => Volatile.Read(ref _flowCallCount);
    public bool AssertEvidenceShapeChecked => Volatile.Read(ref _assertEvidenceShapeChecked) == 1;

    public void ResetAssertEvidenceShape() => Volatile.Write(ref _assertEvidenceShapeChecked, 0);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var body = request.Content?.ReadAsStringAsync(cancellationToken).Result ?? "";

        RecordLlmConfig(path, body);

        if (path.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ok\":true,\"model\":\"stub-model\",\"latency_ms\":5}",
                    Encoding.UTF8, "application/json"),
            });
        }

        if (path.EndsWith("/api/locate-element", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _locateCallCount);
            var description = ReadStringField(body, "description");
            var payload = description is not null && LocateResults.TryGetValue(description, out var configured)
                ? configured
                : DefaultLocateResult;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }

        if (path.EndsWith("/api/ai-assert", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _assertCallCount);
            var expectation = ReadStringField(body, "expectation");
            CheckEvidenceShape(body);
            var payload = expectation is not null && AssertResults.TryGetValue(expectation, out var configured)
                ? configured
                : DefaultAssertResult;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }

        if (path.EndsWith("/api/analyze-api-flow", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _flowCallCount);
            var payload = body.Contains("wrapped-login", StringComparison.OrdinalIgnoreCase)
                ? "{\"cases\":[" +
                  "{\"name\":\"带请求头登录\",\"priority\":\"P0\",\"type\":\"api\",\"steps\":[" +
                  "{\"step_order\":0,\"action_type\":\"Request\",\"description\":\"带请求头登录\",\"method\":\"POST\",\"endpoint\":\"/login\",\"body\":\"{\\\"headers\\\":{\\\"X-Token\\\":\\\"{token}\\\"},\\\"body\\\":{\\\"id\\\":1}}\",\"value\":null,\"url\":null,\"selector\":null}" +
                  "]}]}"
                : "{\"cases\":[" +
                "{\"name\":\"登录后查询用户信息\",\"priority\":\"P0\",\"type\":\"api\",\"steps\":[" +
                "{\"step_order\":0,\"action_type\":\"Request\",\"description\":\"登录获取 token\",\"method\":\"POST\",\"endpoint\":\"/login\",\"body\":\"{\\\"username\\\":\\\"admin\\\",\\\"password\\\":\\\"123456\\\"}\",\"value\":null,\"url\":null,\"selector\":null}," +
                "{\"step_order\":1,\"action_type\":\"ExtractVariable\",\"description\":\"提取 token\",\"method\":null,\"endpoint\":\"$.data.token\",\"body\":null,\"value\":\"token\",\"url\":null,\"selector\":null}," +
                "{\"step_order\":2,\"action_type\":\"AssertResponse\",\"description\":\"断言状态码\",\"method\":null,\"endpoint\":null,\"body\":null,\"value\":\"200\",\"url\":null,\"selector\":null}" +
                "]}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }

        if (path.EndsWith("/api/diagnose-failure", StringComparison.OrdinalIgnoreCase))
        {
            var caseName = ReadNestedStringField(body, "test_case", "name");
            var payload = caseName is not null && DiagnoseResults.TryGetValue(caseName, out var configured)
                ? configured
                : DefaultDiagnoseResult;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }

        var isError = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var requirement = doc.RootElement.GetProperty("requirement").GetString() ?? "";
            isError = requirement.Contains("触发500错误响应");
        }
        catch (JsonException)
        {
            isError = body.Contains("触发500错误响应");
        }

        var payload2 = isError
            ? "{\"detail\":\"LLM 调用失败\"}"
            : "{\"cases\":[" +
              "{\"name\":\"登录成功\",\"priority\":\"P0\",\"type\":\"web\",\"steps\":[" +
              "{\"step_order\":0,\"action_type\":\"Navigate\",\"description\":\"打开登录页\",\"url\":\"https://example.com/login\",\"selector\":null,\"value\":null}," +
              "{\"step_order\":1,\"action_type\":\"Fill\",\"description\":\"输入用户名\",\"url\":null,\"selector\":{\"type\":\"css\",\"value\":\"#username\",\"description\":\"用户名输入框\"},\"value\":\"admin\"}]}," +
              "{\"name\":\"密码错误提示\",\"priority\":\"P1\",\"type\":\"web\",\"steps\":[{\"step_order\":0,\"action_type\":\"AssertText\",\"description\":\"断言提示\",\"url\":null,\"selector\":{\"type\":\"css\",\"value\":\".error\"},\"value\":\"密码错误\"}]}" +
              "]}";
        var status = isError ? HttpStatusCode.BadGateway : HttpStatusCode.OK;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(payload2, Encoding.UTF8, "application/json"),
        });
    }

    private static string? ReadStringField(string body, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void RecordLlmConfig(string path, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("llm_config", out var cfg) &&
                cfg.ValueKind == JsonValueKind.Object)
            {
                LastLlmConfigs[path] = cfg.GetRawText();
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string? ReadNestedStringField(string body, string outer, string inner)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(outer, out var o) && o.ValueKind == JsonValueKind.Object &&
                   o.TryGetProperty(inner, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void CheckEvidenceShape(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Object &&
                ev.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String &&
                ev.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                Volatile.Write(ref _assertEvidenceShapeChecked, 1);
            }
        }
        catch (JsonException)
        {
        }
    }
}
