using System.Net.Http.Json;
using System.Text.Json;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.Settings;
using AI.TestPlatform.Application.Visual;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.AI;

public class AIWorkerException : Exception
{
    public AIWorkerException(string message) : base(message) { }
}

public class AIClient
{
    private readonly HttpClient _http;
    private readonly IServiceScopeFactory _scopeFactory;

    public AIClient(HttpClient http, IServiceScopeFactory scopeFactory)
    {
        _http = http;
        _scopeFactory = scopeFactory;
    }

    public async Task<List<GeneratedCaseDto>> GenerateAsync(
        string requirement, int minCases, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/generate-cases",
            new { requirement, min_cases = minCases, llm_config = await GetLlmConfigAsync(ct) }, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (doc is null || !doc.RootElement.TryGetProperty("cases", out var cases) ||
            cases.ValueKind != JsonValueKind.Array)
            return new List<GeneratedCaseDto>();
        return cases.EnumerateArray().Select(MapCase).Where(c => c is not null).Select(c => c!).ToList();
    }

    public async Task<List<GeneratedCaseDto>> AnalyzeApiFlowAsync(
        AnalyzeApiFlowRequest request, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/analyze-api-flow",
            new { endpoints = request.Endpoints, llm_config = await GetLlmConfigAsync(ct) }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (doc is null || !doc.RootElement.TryGetProperty("cases", out var cases) ||
            cases.ValueKind != JsonValueKind.Array)
            return new List<GeneratedCaseDto>();
        return cases.EnumerateArray().Select(MapCase).Where(c => c is not null).Select(c => c!).ToList();
    }

    /// <summary>
    /// Excel 导入：把一批用例的「操作步骤 + 预期结果」文字转换为可执行步骤（按 CaseCode 归组）。
    /// </summary>
    public async Task<Dictionary<string, List<GeneratedStepDto>>> ImportCaseStepsAsync(
        string? baseUrl, IReadOnlyList<ImportCaseItemDto> cases, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/import-case-steps", new
        {
            base_url = baseUrl,
            cases = cases.Select(c => new
            {
                case_code = c.CaseCode,
                scenario = c.Scenario,
                module = c.Module,
                source_steps = c.SourceSteps,
                expected = c.Expected,
            }),
            llm_config = await GetLlmConfigAsync(ct),
        }, ct);

        var map = new Dictionary<string, List<GeneratedStepDto>>(StringComparer.OrdinalIgnoreCase);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("case_code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String)
                continue;
            var steps = new List<GeneratedStepDto>();
            if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var stepEl in stepsEl.EnumerateArray())
                {
                    var mapped = MapStep(stepEl);
                    if (mapped is not null)
                        steps.Add(mapped);
                }
            }
            map[codeEl.GetString()!] = steps;
        }
        return map;
    }

    public async Task<LocateElementResultDto> LocateElementAsync(        LocateElementRequestDto request, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/locate-element",
            new { page_url = request.PageUrl, description = request.Description, elements = request.Elements, llm_config = await GetLlmConfigAsync(ct) }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("matched_index", out var idx) ||
            !root.TryGetProperty("confidence", out var conf))
            throw new AIWorkerException("AI Worker 定位响应格式无效");
        return new LocateElementResultDto(
            idx.GetInt32(),
            conf.ValueKind == JsonValueKind.Number ? conf.GetSingle() : 0f,
            root.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "");
    }

    public async Task<AIAssertResultDto> SmartAssertAsync(
        string expectation, JsonElement evidence, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/ai-assert",
            new { expectation, evidence, llm_config = await GetLlmConfigAsync(ct) }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("passed", out var passed))
            throw new AIWorkerException("AI Worker 断言响应格式无效");
        return new AIAssertResultDto(
            passed.ValueKind == JsonValueKind.True,
            root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "",
            root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetSingle() : 0f);
    }

    public async Task<DiagnosisResultDto> DiagnoseAsync(
        Dictionary<string, object?> testCase,
        List<FailedStepEvidence> failedSteps,
        List<SimilarCaseEvidence> similarCases,
        CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/diagnose-failure",
            new { test_case = testCase, failed_steps = failedSteps, similar_cases = similarCases, llm_config = await GetLlmConfigAsync(ct) }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("category", out var category))
            throw new AIWorkerException("AI Worker 诊断响应格式无效");
        return new DiagnosisResultDto(
            category.GetString() ?? "其它",
            root.TryGetProperty("root_cause", out var rc) && rc.ValueKind == JsonValueKind.String ? rc.GetString()! : "",
            root.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number ? conf.GetSingle() : 0f,
            root.TryGetProperty("suggested_fix", out var fix) && fix.ValueKind == JsonValueKind.String ? fix.GetString()! : "",
            root.TryGetProperty("retry_recommended", out var retry) && retry.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// 打开与 AI Worker 的流式对话连接（SSE），由调用方直接把响应体裁剪转发给浏览器。
    /// </summary>
    public async Task<HttpResponseMessage> StreamChatAsync(ChatStreamRequestDto request, CancellationToken ct)
    {
        var payload = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            images = request.Images ?? new List<string>(),
            llm_config = await GetLlmConfigAsync(ct),
        };

        HttpResponseMessage response;
        try
        {
            // PostAsJsonAsync 不支持 ResponseHeadersRead，需手工构造请求以便流式读取
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
            {
                Content = JsonContent.Create(payload),
            };
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AIWorkerException("AI Worker 调用超时");
        }
        catch (HttpRequestException ex)
        {
            throw new AIWorkerException($"AI Worker 连接失败: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = body.Length > 200 ? body[..200] : body;
            response.Dispose();
            throw new AIWorkerException($"AI Worker 调用失败 ({(int)response.StatusCode}): {detail}");
        }
        return response;
    }

    public async Task<TestConnectionResult> PingAsync(CancellationToken ct)
    {
        try
        {
            var response = await PostJsonAsync("/api/ping", new { llm_config = await GetLlmConfigAsync(ct) }, ct);
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var root = doc?.RootElement ?? default;
            return new TestConnectionResult(true,
                root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                root.TryGetProperty("latency_ms", out var l) ? l.GetInt32() : null, null);
        }
        catch (AIWorkerException ex)
        {
            return new TestConnectionResult(false, "", null, ex.Message);
        }
        catch (JsonException)
        {
            return new TestConnectionResult(false, "", null, "响应格式无效");
        }
    }

    // ---------------------------------------------------------------- 视觉回归

    /// <summary>像素级图像比对（纯计算，不调用 LLM）</summary>
    public async Task<VisualCompareResultDto> CompareImagesAsync(
        string baselineBase64, string actualBase64,
        double threshold, int pixelTolerance, int maxRegions,
        IReadOnlyList<VisualIgnoreRegion>? ignoreRegions = null, CancellationToken ct = default)
    {
        var response = await PostJsonAsync("/api/visual/compare", new
        {
            baseline_base64 = baselineBase64,
            actual_base64 = actualBase64,
            threshold,
            pixel_tolerance = pixelTolerance,
            max_regions = maxRegions,
            // 百分比区域透传给 AIWorker，在差异掩码上直接屏蔽（比涂改图像干净）
            ignore_regions = ignoreRegions,
        }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("diffRatio", out var ratio))
            throw new AIWorkerException("AI Worker 视觉比对响应格式无效");

        var regions = new List<VisualRegionDto>();
        if (root.TryGetProperty("regions", out var regionsNode) && regionsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in regionsNode.EnumerateArray())
            {
                regions.Add(new VisualRegionDto(
                    GetInt(item, "x"), GetInt(item, "y"), GetInt(item, "width"), GetInt(item, "height"),
                    GetLong(item, "changedPixels"), GetDouble(item, "ratio")));
            }
        }

        return new VisualCompareResultDto(
            true,
            GetInt(root, "baselineWidth"), GetInt(root, "baselineHeight"),
            GetInt(root, "actualWidth"), GetInt(root, "actualHeight"),
            root.TryGetProperty("sizeMismatch", out var mismatch) && mismatch.ValueKind == JsonValueKind.True,
            GetLong(root, "totalPixels"), GetLong(root, "diffPixels"),
            ratio.GetDouble(),
            GetDouble(root, "threshold"),
            root.TryGetProperty("passed", out var passed) && passed.ValueKind == JsonValueKind.True,
            root.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "low" : "low",
            regions,
            root.TryGetProperty("diffImageBase64", out var diff) && diff.ValueKind == JsonValueKind.String
                ? diff.GetString() : null);
    }

    /// <summary>让视觉模型把差异说成人话（失败不影响比对结论，由调用方决定是否忽略异常）</summary>
    public async Task<VisualDescribeResultDto> DescribeVisualDiffAsync(
        string baselineBase64, string actualBase64, string? diffImageBase64,
        string caseName, string stepDescription, double diffRatio,
        IReadOnlyList<VisualRegionDto> regions, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/visual/describe", new
        {
            baseline_base64 = baselineBase64,
            actual_base64 = actualBase64,
            diff_image_base64 = diffImageBase64 ?? string.Empty,
            case_name = caseName,
            step_description = stepDescription,
            diff_ratio = diffRatio,
            regions,
            llm_config = await GetLlmConfigAsync(ct),
        }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object)
            throw new AIWorkerException("AI Worker 视觉说明响应格式无效");
        return new VisualDescribeResultDto(
            GetString(root, "summary"), GetString(root, "risk"), GetString(root, "suggestion"));
    }

    private static string GetString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static int GetInt(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : 0;

    private static long GetLong(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() : 0;

    private static double GetDouble(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : 0d;

    private async Task<Dictionary<string, object>?> GetLlmConfigAsync(CancellationToken ct)    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var config = await settings.GetAsync(ct);
        var result = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(config.AiBaseUrl)) result["base_url"] = config.AiBaseUrl;
        if (!string.IsNullOrWhiteSpace(config.AiApiKey)) result["api_key"] = config.AiApiKey;
        if (!string.IsNullOrWhiteSpace(config.AiModel)) result["model"] = config.AiModel;
        if (config.AiMaxTokens > 0) result["max_tokens"] = config.AiMaxTokens;
        return result.Count > 0 ? result : null;
    }

    private async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T payload, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, payload, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AIWorkerException("AI Worker 调用超时");
        }
        catch (HttpRequestException ex)
        {
            throw new AIWorkerException($"AI Worker 连接失败: {ex.Message}");
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = body.Length > 200 ? body[..200] : body;
            throw new AIWorkerException($"AI Worker 调用失败 ({(int)response.StatusCode}): {detail}");
        }
        return response;
    }

    private static GeneratedCaseDto? MapCase(JsonElement item)
    {
        if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return null;
        var steps = new List<GeneratedStepDto>();
        if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var stepEl in stepsEl.EnumerateArray())
            {
                var mapped = MapStep(stepEl);
                if (mapped is not null)
                    steps.Add(mapped);
            }
        }
        var priority = item.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "P2";
        var type = TestType.Web;
        if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            type = typeEl.GetString()?.ToLowerInvariant() switch
            {
                "api" => TestType.Api,
                "mobile" => TestType.Mobile,
                _ => TestType.Web,
            };
        }
        return new GeneratedCaseDto(nameEl.GetString()!, priority, type, steps);
    }

    private static GeneratedStepDto? MapStep(JsonElement step)
    {
        if (!step.TryGetProperty("action_type", out var actionEl) || actionEl.ValueKind != JsonValueKind.String)
            return null;
        if (!Enum.TryParse<ActionType>(actionEl.GetString(), true, out var actionType))
            return null;
        var config = new StepConfig();
        if (step.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
            config.Url = urlEl.GetString();
        if (step.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String)
            config.Value = valueEl.GetString();
        if (step.TryGetProperty("method", out var methodEl) && methodEl.ValueKind == JsonValueKind.String)
            config.Method = methodEl.GetString();
        if (step.TryGetProperty("endpoint", out var endpointEl) && endpointEl.ValueKind == JsonValueKind.String)
            config.Endpoint = endpointEl.GetString();
        if (step.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String)
        {
            var bodyRaw = bodyEl.GetString();
            if (!string.IsNullOrWhiteSpace(bodyRaw))
            {
                try
                {
                    using var bodyDoc = JsonDocument.Parse(bodyRaw);
                    var root = bodyDoc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("headers", out var headersEl) &&
                        headersEl.ValueKind == JsonValueKind.Object)
                    {
                        config.Headers = headersEl.EnumerateObject()
                            .Select(h => new HeaderEntry
                            {
                                Name = h.Name,
                                Value = h.Value.ValueKind == JsonValueKind.String ? h.Value.GetString() ?? "" : h.Value.GetRawText(),
                            })
                            .ToList();
                        config.Body = root.TryGetProperty("body", out var inner)
                            ? inner.GetRawText()
                            : null;
                    }
                    else
                    {
                        config.Body = bodyRaw;
                    }
                }
                catch (JsonException)
                {
                    config.Body = bodyRaw;
                }
            }
        }
        if (step.TryGetProperty("selector", out var selEl) && selEl.ValueKind == JsonValueKind.Object)
        {
            config.Selector = new SelectorConfig
            {
                Type = selEl.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "css",
                Value = selEl.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
                Description = selEl.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
            };
        }
        var order = step.TryGetProperty("step_order", out var o) && o.ValueKind == JsonValueKind.Number
            ? o.GetInt32()
            : 0;
        var description = step.TryGetProperty("description", out var de) && de.ValueKind == JsonValueKind.String
            ? de.GetString()
            : null;
        return new GeneratedStepDto(order, actionType, config, description);
    }
}
