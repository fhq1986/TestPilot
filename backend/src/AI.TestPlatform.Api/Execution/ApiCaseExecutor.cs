using System.Text;
using System.Text.Json;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Execution;

public record ApiStepResult(ApiResponse? Response, string Summary);

public record ApiResponse(int StatusCode, string Body, Dictionary<string, string> Headers);

public class ApiCaseExecutor
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _variables = new();
    private readonly string? _baseUrl;

    public ApiCaseExecutor(HttpClient http, string? baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl;
    }

    public IReadOnlyDictionary<string, string> Variables => _variables;

    public async Task<ApiResponse> ExecuteRequestAsync(TestStep step, int timeoutMs, CancellationToken ct)
    {
        var cfg = step.Config;
        var method = new HttpMethod((cfg.Method ?? "GET").ToUpperInvariant());
        var endpoint = JsonAssert.ResolveVariables(cfg.Endpoint ?? "", _variables);
        var url = BuildUrl(endpoint);
        using var request = new HttpRequestMessage(method, url);

        if (cfg.Headers is { Count: > 0 })
        {
            foreach (var header in cfg.Headers)
                request.Headers.TryAddWithoutValidation(header.Name, JsonAssert.ResolveVariables(header.Value, _variables));
        }
        if (!string.IsNullOrWhiteSpace(cfg.Body) && method != HttpMethod.Get)
        {
            request.Content = new StringContent(
                JsonAssert.ResolveVariables(cfg.Body, _variables),
                Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
        return new ApiResponse((int)response.StatusCode, body, headers);
    }

    public void AssertResponse(TestStep step, ApiResponse response)
    {
        var cfg = step.Config;
        var expected = (cfg.Value ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(expected))
        {
            bool ok;
            try
            {
                ok = expected.EndsWith("xx", StringComparison.OrdinalIgnoreCase)
                    ? response.StatusCode / 100 == int.Parse(expected[..1])
                    : response.StatusCode == int.Parse(expected);
            }
            catch (FormatException)
            {
                throw new StepExecutionException($"无效的期望状态码: {expected}");
            }
            catch (OverflowException)
            {
                throw new StepExecutionException($"无效的期望状态码: {expected}");
            }
            if (!ok)
                throw new StepExecutionException($"状态码断言失败: 期望 {expected} 实际 {response.StatusCode}");
        }
        if (!string.IsNullOrWhiteSpace(cfg.Body))
        {
            JsonDocument expectedDoc;
            try { expectedDoc = JsonDocument.Parse(cfg.Body); }
            catch (JsonException ex) { throw new StepExecutionException($"期望响应 JSON 无效: {ex.Message}"); }
            JsonDocument actualDoc;
            try { actualDoc = JsonDocument.Parse(response.Body); }
            catch (JsonException) { throw new StepExecutionException($"实际响应不是 JSON: {response.Body[..Math.Min(100, response.Body.Length)]}"); }
            if (!JsonAssert.IsSubset(expectedDoc.RootElement, actualDoc.RootElement, out var mismatch))
                throw new StepExecutionException($"响应体断言失败: {mismatch}");
        }
    }

    public void ExtractVariable(TestStep step, ApiResponse response)
    {
        var name = (step.Config.Value ?? "").Trim();
        var path = (step.Config.Endpoint ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new StepExecutionException("ExtractVariable 缺少变量名（config.value）");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(response.Body); }
        catch (JsonException ex) { throw new StepExecutionException($"提取变量时响应不是 JSON: {ex.Message}"); }
        var value = string.IsNullOrWhiteSpace(path) ? doc.RootElement.GetRawText() : JsonAssert.EvaluateJsonPath(doc.RootElement, path);
        if (value is null)
            throw new StepExecutionException($"提取失败: 路径 {path} 不存在");
        _variables[name] = value;
    }

    private string BuildUrl(string endpoint)
    {
        // 常见误用：在 endpoint 里手写 {BaseUrl} 占位符（BaseUrl 由用例/环境自动拼接，不是变量）
        if (endpoint.Contains("{BaseUrl}", StringComparison.OrdinalIgnoreCase))
            throw new StepExecutionException(
                "端点中不要写 {BaseUrl}：相对路径直接写 /api/xxx 即可，BaseUrl 会自动拼接（用例未配置时使用执行环境的地址）");

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            return endpoint;
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new StepExecutionException("相对路径需要用例 BaseUrl（可在用例编辑页填写，或为执行环境配置 BaseUrl）");
        return _baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
    }

    public static string Summarize(ApiResponse response) =>
        $"{response.StatusCode} {response.Body[..Math.Min(500, response.Body.Length)]}";
}
