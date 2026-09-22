using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.ApiTesting;

/// <summary>
/// 把接口用例（或 OpenAPI 操作）翻译成一份可执行的 k6 脚本（迭代 F·P2-9）。
///
/// 设计要点：
/// - **纯函数**：同输入必得同输出（含脚本哈希），这样「脚本有没有过期」可以用哈希判断，
///   也让单测能直接断言字节级结果，不必起 Host。
/// - **不做朴素字符串替换**：请求体里的 <c>{变量}</c> 必须按占位符**切块**、
///   每块用 <c>JsonSerializer.Serialize</c> 转义后再拼。直接 Replace 的话，
///   请求体里带引号/反斜杠就会生成非法 JS，而且等于开了个 JS 注入口子。
/// - **必须声明 summaryTrendStats**：k6 默认只输出到 p(95)，不声明就永远采不到 p(99)，
///   而 p99 是压测最常被问的那个数。
/// </summary>
public static class K6ScriptGenerator
{
    /// <summary>与后端 <c>JsonAssert.ResolveVariables</c> 一致的占位符语法（单花括号）</summary>
    private static readonly Regex Placeholder = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static K6GenerateResult Generate(K6ScriptInput input)
    {
        var warnings = new List<string>();

        // 只生成一次：Build 会往 warnings 里追加，调两次会把告警重复一遍。
        // 哈希也只对**正文**取（正文不含生成时间戳），所以同输入必得同哈希——
        // 「脚本是否已过期」正是靠它判断的。
        var body = Build(input, warnings);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        var sb = new StringBuilder();
        sb.AppendLine("// === AI 测试平台自动生成 · 请勿手工编辑（重新生成会覆盖）===");
        sb.AppendLine($"// 场景：{input.ScenarioName}");
        sb.AppendLine($"// 生成时间：{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"// 脚本哈希：{hash}");
        sb.AppendLine();
        sb.Append(body);

        return new K6GenerateResult(sb.ToString(), hash, warnings);
    }

    private static string Build(K6ScriptInput input, List<string> warnings)
    {
        var sb = new StringBuilder();

        sb.AppendLine("import http from 'k6/http';");
        sb.AppendLine("import { check, group, sleep } from 'k6';");
        sb.AppendLine();

        sb.AppendLine($"const BASE_URL = __ENV.BASE_URL || {Js(input.BaseUrl)};");
        sb.Append("const VARS = ");
        sb.Append(JsonSerializer.Serialize(input.Variables, JsonOpts));
        sb.AppendLine(";");
        sb.AppendLine();

        AppendOptions(sb, input);
        AppendRuntime(sb);
        AppendDefault(sb, input, warnings);
        AppendSummaryHandler(sb);

        return sb.ToString();
    }

    // ---------------------------------------------------------------- options

    private static void AppendOptions(StringBuilder sb, K6ScriptInput input)
    {
        var p = input.Profile;
        sb.AppendLine("export const options = {");
        sb.AppendLine("  scenarios: {");
        sb.AppendLine("    main: {");

        switch (p.Kind)
        {
            case "constant-vus":
                sb.AppendLine("      executor: 'constant-vus',");
                sb.AppendLine($"      vus: {Math.Max(1, p.Vus)},");
                sb.AppendLine($"      duration: {Js(string.IsNullOrWhiteSpace(p.Duration) ? "1m" : p.Duration)},");
                break;

            case "constant-arrival-rate":
                sb.AppendLine("      executor: 'constant-arrival-rate',");
                sb.AppendLine($"      rate: {Math.Max(1, p.Rate)},");
                sb.AppendLine($"      timeUnit: {Js(string.IsNullOrWhiteSpace(p.TimeUnit) ? "1s" : p.TimeUnit)},");
                sb.AppendLine($"      duration: {Js(string.IsNullOrWhiteSpace(p.Duration) ? "1m" : p.Duration)},");
                sb.AppendLine($"      preAllocatedVUs: {Math.Max(1, p.PreAllocatedVUs)},");
                sb.AppendLine($"      maxVUs: {Math.Max(Math.Max(1, p.PreAllocatedVUs), p.MaxVUs)},");
                break;

            default: // ramping-vus
                sb.AppendLine("      executor: 'ramping-vus',");
                sb.AppendLine("      startVUs: 0,");
                sb.AppendLine("      stages: [");
                if (p.Stages.Count == 0)
                {
                    // 没配阶段也要给一个能跑的默认曲线，否则 k6 会以 0 VU 跑完、什么都没测到
                    sb.AppendLine("        { duration: '30s', target: 10 },");
                    sb.AppendLine("        { duration: '1m', target: 10 },");
                    sb.AppendLine("        { duration: '30s', target: 0 },");
                }
                else
                {
                    foreach (var stage in p.Stages)
                    {
                        var dur = string.IsNullOrWhiteSpace(stage.Duration) ? "30s" : stage.Duration;
                        sb.AppendLine($"        {{ duration: {Js(dur)}, target: {Math.Max(0, stage.Target)} }},");
                    }
                }
                sb.AppendLine("      ],");
                sb.AppendLine($"      gracefulRampDown: {Js(string.IsNullOrWhiteSpace(p.GracefulRampDown) ? "10s" : p.GracefulRampDown)},");
                break;
        }

        sb.AppendLine("    },");
        sb.AppendLine("  },");

        // 阈值按 metric 分组：k6 的 thresholds 是 { 指标名: [表达式, ...] }
        sb.AppendLine("  thresholds: {");
        foreach (var group in input.Thresholds.GroupBy(t => t.Metric))
        {
            var exprs = group.Select(RenderThreshold).Where(e => e is not null).ToList();
            if (exprs.Count == 0) continue;
            sb.AppendLine($"    {Js(group.Key)}: [{string.Join(", ", exprs.Select(e => Js(e!)))}],");
        }
        sb.AppendLine("  },");

        // ⚠ 不声明 p(99) 就永远采不到 p99（k6 默认只到 p(95)）
        sb.AppendLine("  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],");
        sb.AppendLine("  discardResponseBodies: false,");
        sb.AppendLine("};");
        sb.AppendLine();
    }

    /// <summary>把结构化阈值渲染回 k6 原生表达式，如 <c>p(95)&lt;500</c>；metric/聚合方式不合法时返回 null</summary>
    public static string? RenderThreshold(LoadTestThreshold t)
    {
        var aggregators = new[] { "p(90)", "p(95)", "p(99)", "rate", "avg", "med", "max", "min", "count" };
        if (!aggregators.Contains(t.Aggregator)) return null;
        if (t.Operator is not ("<" or "<=" or ">" or ">=")) return null;
        var value = t.Value.ToString(CultureInfo.InvariantCulture);
        return $"{t.Aggregator}{t.Operator}{value}";
    }

    // ---------------------------------------------------------------- 内联运行时

    /// <summary>
    /// 内联的 JS 运行时，语义对齐后端 <c>JsonAssert</c>：
    /// 同一条接口用例在「功能执行」与「压测」两种模式下必须对同一个响应得出同样的结论，
    /// 否则压测里的 checks 通过率会和功能执行的通过率对不上。
    /// </summary>
    private static void AppendRuntime(StringBuilder sb)
    {
        sb.AppendLine("// ---- 内联运行时（语义对齐后端 JsonAssert，勿改）----");
        sb.AppendLine("function vars(name, ctx) {");
        sb.AppendLine("  if (ctx[name] !== undefined) return ctx[name];");
        sb.AppendLine("  if (VARS[name] !== undefined) return VARS[name];");
        sb.AppendLine("  const fromEnv = __ENV['VAR_' + name];");
        sb.AppendLine("  return fromEnv !== undefined ? fromEnv : '';");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function jsonPath(root, path) {");
        sb.AppendLine("  if (!path || path[0] !== '$') return null;");
        sb.AppendLine("  let cur = root;");
        sb.AppendLine("  const re = /(?:\\.([A-Za-z0-9_-]+))|(?:\\[(\\d+)\\])/g;");
        sb.AppendLine("  let m;");
        sb.AppendLine("  while ((m = re.exec(path.slice(1))) !== null) {");
        sb.AppendLine("    if (m[1] !== undefined) {");
        sb.AppendLine("      if (cur === null || typeof cur !== 'object' || Array.isArray(cur)) return null;");
        sb.AppendLine("      if (!(m[1] in cur)) return null;");
        sb.AppendLine("      cur = cur[m[1]];");
        sb.AppendLine("    } else {");
        sb.AppendLine("      if (!Array.isArray(cur)) return null;");
        sb.AppendLine("      const i = parseInt(m[2], 10);");
        sb.AppendLine("      if (i >= cur.length) return null;");
        sb.AppendLine("      cur = cur[i];");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("  if (cur === null || cur === undefined) return null;");
        sb.AppendLine("  return typeof cur === 'object' ? JSON.stringify(cur) : String(cur);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function subset(expected, actual) {");
        sb.AppendLine("  if (Array.isArray(expected)) {");
        sb.AppendLine("    if (!Array.isArray(actual)) return false;");
        sb.AppendLine("    return expected.every(e => actual.some(a => subset(e, a)));");
        sb.AppendLine("  }");
        sb.AppendLine("  if (expected !== null && typeof expected === 'object') {");
        sb.AppendLine("    if (actual === null || typeof actual !== 'object' || Array.isArray(actual)) return false;");
        sb.AppendLine("    return Object.keys(expected).every(k => k in actual && subset(expected[k], actual[k]));");
        sb.AppendLine("  }");
        sb.AppendLine("  return expected === actual;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ---------------------------------------------------------------- 主流程

    private static void AppendDefault(StringBuilder sb, K6ScriptInput input, List<string> warnings)
    {
        sb.AppendLine("export default function () {");
        sb.AppendLine("  const ctx = {};");
        sb.AppendLine();

        foreach (var testCase in input.Cases)
        {
            sb.AppendLine($"  group({Js(testCase.Name)}, function () {{");

            var requestIndex = 0;
            // 最近一次请求的响应变量名：断言/提取都作用于它
            string? lastResponse = null;

            foreach (var step in testCase.Steps)
            {
                if (step.UnsupportedAction is not null)
                {
                    warnings.Add($"用例「{testCase.Name}」第 {step.Order} 步的动作 {step.UnsupportedAction} 不支持压测，已跳过");
                    continue;
                }

                if (step.Request is { } req)
                {
                    var resVar = $"res_{requestIndex++}";
                    AppendRequest(sb, req, resVar);
                    lastResponse = resVar;
                    continue;
                }

                if (step.Assert is { } assert)
                {
                    if (lastResponse is null)
                    {
                        warnings.Add($"用例「{testCase.Name}」第 {step.Order} 步是断言但没有前置请求，已跳过");
                        continue;
                    }
                    AppendAssert(sb, assert, lastResponse, testCase.Name, step.Order, warnings);
                    continue;
                }

                if (step.Extract is { } extract)
                {
                    if (lastResponse is null)
                    {
                        warnings.Add($"用例「{testCase.Name}」第 {step.Order} 步要提取变量但没有前置请求，已跳过");
                        continue;
                    }
                    sb.AppendLine($"    ctx[{Js(extract.VariableName)}] = jsonPath({lastResponse}.json(), {Js(extract.JsonPath)});");
                }
            }

            sb.AppendLine("  });");
            sb.AppendLine();
        }

        if (input.Profile.ThinkTimeSeconds > 0)
        {
            var seconds = input.Profile.ThinkTimeSeconds.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine($"  sleep({seconds});");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendRequest(StringBuilder sb, K6RequestSpec req, string resVar)
    {
        var method = string.IsNullOrWhiteSpace(req.Method) ? "GET" : req.Method.Trim().ToUpperInvariant();
        var url = BuildUrlExpression(req.Endpoint);
        var body = string.IsNullOrWhiteSpace(req.Body) ? "null" : ToJsExpression(req.Body);

        var headerPairs = req.Headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .Select(h => $"{Js(h.Name)}: {ToJsExpression(h.Value)}");

        sb.AppendLine($"    const {resVar} = http.request({Js(method)}, {url}, {body}, {{");
        sb.AppendLine($"      headers: {{ {string.Join(", ", headerPairs)} }},");
        sb.AppendLine($"      tags: {{ name: {Js($"{method} {req.Endpoint}")} }},");
        sb.AppendLine("    });");
    }

    /// <summary>绝对 URL 直接透出，相对路径拼 BASE_URL</summary>
    private static string BuildUrlExpression(string endpoint)
    {
        var isAbsolute = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        return isAbsolute ? Js(endpoint) : $"BASE_URL + {Js(endpoint)}";
    }

    private static void AppendAssert(StringBuilder sb, K6AssertSpec assert, string resVar,
        string caseName, int stepOrder, List<string> warnings)
    {
        var checks = new List<string>();

        if (!string.IsNullOrWhiteSpace(assert.StatusExpectation))
        {
            var expect = assert.StatusExpectation.Trim();
            var label = $"状态码={expect}";
            // 对齐 ApiCaseExecutor.AssertResponse 的 "xx" 分支
            var predicate = expect.EndsWith("xx", StringComparison.OrdinalIgnoreCase) && expect.Length == 3
                ? $"(r) => r.status >= {expect[0]}00 && r.status < {(char)(expect[0] + 1)}00"
                : int.TryParse(expect, out var code)
                    ? $"(r) => r.status === {code}"
                    : null;

            if (predicate is null)
            {
                warnings.Add($"用例「{caseName}」第 {stepOrder} 步的状态码断言「{expect}」无法解析，已跳过");
            }
            else
            {
                checks.Add($"{Js($"{caseName} {label}")}: {predicate}");
            }
        }

        if (!string.IsNullOrWhiteSpace(assert.ExpectedBodyJson))
        {
            // 直接内嵌原始 JSON：合法 JSON 同时是合法 JS 表达式字面量。
            // 先解析一次，避免把坏 JSON 写进脚本导致整个脚本语法错误。
            try
            {
                using var _ = JsonDocument.Parse(assert.ExpectedBodyJson);
                checks.Add($"{Js($"{caseName} 响应体匹配")}: (r) => subset({assert.ExpectedBodyJson}, r.json())");
            }
            catch (JsonException)
            {
                warnings.Add($"用例「{caseName}」第 {stepOrder} 步的期望响应体不是合法 JSON，已跳过");
            }
        }

        if (checks.Count == 0) return;

        sb.AppendLine($"    check({resVar}, {{");
        foreach (var c in checks) sb.AppendLine($"      {c},");
        sb.AppendLine("    });");
    }

    private static void AppendSummaryHandler(StringBuilder sb)
    {
        sb.AppendLine("// k6 的 summary 通过文件交给平台解析（--summary-export 也会写一份，这里额外写一份便于定位）");
        sb.AppendLine("export function handleSummary(data) {");
        sb.AppendLine("  const out = {};");
        sb.AppendLine("  out[__ENV.SUMMARY_PATH || 'summary.json'] = JSON.stringify(data);");
        sb.AppendLine("  return out;");
        sb.AppendLine("}");
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>把一段可能含 <c>{变量}</c> 的文本转成 JS 表达式；无占位符时就是一个字符串字面量</summary>
    public static string ToJsExpression(string template)
    {
        var matches = Placeholder.Matches(template);
        if (matches.Count == 0) return Js(template);

        var parts = new List<string>();
        var cursor = 0;
        foreach (Match m in matches)
        {
            if (m.Index > cursor)
                parts.Add(Js(template[cursor..m.Index]));
            // 变量名由 [A-Za-z0-9_]+ 约束，可安全内联
            parts.Add($"vars('{m.Groups[1].Value}', ctx)");
            cursor = m.Index + m.Length;
        }
        if (cursor < template.Length)
            parts.Add(Js(template[cursor..]));

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// JSON 字符串字面量即合法 JS 字符串字面量（转义规则兼容），用它统一转义。
    ///
    /// 用 <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> 关掉「HTML 敏感字符」转义：
    /// System.Text.Json 默认会把 <c>&lt; &gt; &amp; '</c> 写成 <c>\u003C</c> 这类转义，
    /// 那是为了安全嵌入 HTML 的默认值，对生成 .js 文件毫无意义——阈值会被渲染成
    /// <c>"p(95)\u003C3000"</c>，k6 虽然能正确解析（JS 里等价），但脚本预览页满屏转义码，
    /// 用户没法读也没法改。这里是纯 JS 输出，不是 HTML 上下文，故用宽松转义。
    /// </summary>
    private static string Js(string value) =>
        JsonSerializer.Serialize(value, JsOptions);

    private static readonly JsonSerializerOptions JsOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
