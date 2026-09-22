using System.Linq;
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
    private readonly AILivenessBreaker _breaker;

    public AIClient(HttpClient http, IServiceScopeFactory scopeFactory, AILivenessBreaker breaker)
    {
        _http = http;
        _scopeFactory = scopeFactory;
        _breaker = breaker;
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
    /// M8 结构化失败归因：在诊断基础上拿到 fix_category 与可执行 proposed_fixes。
    /// ⚠ 下游判定一律以 FixCategory 为准（Category 仅展示）。
    /// </summary>
    public async Task<AttributedResultDto> AttributeAsync(
        Dictionary<string, object?> testCase,
        List<FailedStepEvidence> failedSteps,
        List<SimilarCaseEvidence> similarCases,
        CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/attribute-failure",
            new { test_case = testCase, failed_steps = failedSteps, similar_cases = similarCases, llm_config = await GetLlmConfigAsync(ct) }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return MapAttributedResult(doc?.RootElement ?? default);
    }

    /// <summary>
    /// D4：真语义向量化。把单条文本发给 AIWorker 的 /api/embed（再由其转发到 OpenAI 兼容 /v1/embeddings），
    /// 返回模型原始向量。失败（Worker 不可达 / 熔断打开 / 格式无效）一律抛 <see cref="AIWorkerException"/>，
    /// 由上层 <see cref="EmbeddingProvider"/> 决定是否降级为字符匹配。
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, EmbeddingConfigDto cfg, CancellationToken ct)
    {
        var embeddingConfig = new Dictionary<string, string?>
        {
            ["base_url"] = cfg.BaseUrl,
            ["api_key"] = cfg.ApiKey,
            ["model"] = cfg.Model,
        };
        var response = await PostJsonAsync("/api/embed",
            new { texts = new[] { text }, embedding_config = embeddingConfig }, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var root = doc?.RootElement ?? default;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("embeddings", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            throw new AIWorkerException("AI Worker 语义向量响应格式无效");
        float[]? vector = null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array) continue;
            vector = item.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            break;
        }
        if (vector is null) throw new AIWorkerException("AI Worker 语义向量为空");
        return vector;
    }

    /// <summary>
    /// 把 <c>/api/attribute-failure</c> 的响应 JSON 映射为 <see cref="AttributedResultDto"/>。
    /// 公开仅为可测（同 MetricsEndpoint.StatusLabel 的做法）——单测用 HttpMessageHandler 桩喂入
    /// 各种响应即可钉死「fix_category 解析 / proposed_fixes 过滤 / 未知值兜底」。
    /// </summary>
    public static AttributedResultDto MapAttributedResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("category", out var category))
            throw new AIWorkerException("AI Worker 归因响应格式无效");

        var fixCategory = FixCategory.Unknown;
        if (root.TryGetProperty("fix_category", out var fc) && fc.ValueKind == JsonValueKind.String &&
            Enum.TryParse<FixCategory>(fc.GetString(), ignoreCase: true, out var parsed))
            fixCategory = parsed;

        var fixes = new List<FixActionDto>();
        if (root.TryGetProperty("proposed_fixes", out var fixesEl) && fixesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fixesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var actionType = GetString(item, "action_type");
                if (string.IsNullOrEmpty(actionType))
                    continue;
                int? stepOrder = item.TryGetProperty("step_order", out var so) && so.ValueKind == JsonValueKind.Number
                    ? so.GetInt32() : null;
                var prms = new Dictionary<string, object>();
                if (item.TryGetProperty("params", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in pEl.EnumerateObject())
                        prms[p.Name] = p.Value.ValueKind == JsonValueKind.String
                            ? p.Value.GetString() ?? string.Empty
                            : p.Value.GetRawText();
                }
                var conf = item.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number
                    ? (float)cf.GetDouble() : 0f;
                fixes.Add(new FixActionDto(actionType, stepOrder, prms, conf));
            }
        }

        return new AttributedResultDto(
            category.GetString() ?? "其它",
            GetString(root, "root_cause"),
            root.TryGetProperty("confidence", out var conf2) && conf2.ValueKind == JsonValueKind.Number ? (float)conf2.GetDouble() : 0f,
            GetString(root, "suggested_fix"),
            root.TryGetProperty("retry_recommended", out var retry) && retry.ValueKind == JsonValueKind.True,
            fixCategory,
            fixes,
            root.TryGetProperty("needs_human_approval", out var needs) && needs.ValueKind == JsonValueKind.True,
            root.TryGetProperty("approval_reason", out var ar) && ar.ValueKind == JsonValueKind.String ? ar.GetString() : null);
    }

    /// <summary>
    /// M8 Planner（Phase 3）：按测试目标 + 失败历史生成/重构步骤序列。
    /// 返回结构与 <see cref="GenerateAsync"/> 一致（复用 MapCase 做步骤映射）。
    /// </summary>
    public async Task<PlanResultDto> PlanAsync(
        string requirement, string? baseUrl, string? failureContext,
        IReadOnlyList<object>? previousAttempts, CancellationToken ct)
    {
        var response = await PostJsonAsync("/api/plan-steps", new
        {
            requirement,
            base_url = baseUrl,
            failure_context = failureContext,
            previous_attempts = previousAttempts ?? new List<object>(),
            min_cases = 1,
            llm_config = await GetLlmConfigAsync(ct),
        }, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var cases = new List<GeneratedCaseDto>();
        if (doc is not null && doc.RootElement.TryGetProperty("cases", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
            cases = arr.EnumerateArray().Select(MapCase).Where(c => c is not null).Select(c => c!).ToList();
        return new PlanResultDto(cases, 1f, Array.Empty<string>());
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

        // M8：对话同样是 AI 路径，纳入统一熔断
        if (!_breaker.AllowCall())
            throw new AIWorkerException("AI 服务已熔断（连续不可用），本次调用已短路");

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
            _breaker.OnFailure();
            throw new AIWorkerException("AI Worker 调用超时");
        }
        catch (HttpRequestException ex)
        {
            _breaker.OnFailure();
            throw new AIWorkerException($"AI Worker 连接失败: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            _breaker.OnFailure();
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = body.Length > 200 ? body[..200] : body;
            response.Dispose();
            throw new AIWorkerException($"AI Worker 调用失败 ({(int)response.StatusCode}): {detail}");
        }
        _breaker.OnSuccess();
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
        // M8：这里是**所有 AI 调用的统一入口**（生成/定位/诊断/归因/规划/断言/视觉），
        // 可用性熔断在此集中生效：熔断打开时**不发起请求**直接短路，省掉注定超时的那次等待。
        if (!_breaker.AllowCall())
            throw new AIWorkerException("AI 服务已熔断（连续不可用），本次调用已短路");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, payload, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _breaker.OnFailure();
            throw new AIWorkerException("AI Worker 调用超时");
        }
        catch (HttpRequestException ex)
        {
            _breaker.OnFailure();
            throw new AIWorkerException($"AI Worker 连接失败: {ex.Message}");
        }
        if (!response.IsSuccessStatusCode)
        {
            _breaker.OnFailure();
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = body.Length > 200 ? body[..200] : body;
            throw new AIWorkerException($"AI Worker 调用失败 ({(int)response.StatusCode}): {detail}");
        }
        _breaker.OnSuccess();
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
