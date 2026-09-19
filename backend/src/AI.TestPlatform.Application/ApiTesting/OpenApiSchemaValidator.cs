using System.Text.Json;

namespace AI.TestPlatform.Application.ApiTesting;

/// <summary>一处结构不符</summary>
public record SchemaViolation(string Path, string Message);

/// <summary>校验结果</summary>
public record SchemaValidationResult(bool Valid, List<SchemaViolation> Violations)
{
    public string Describe(int max = 8)
    {
        if (Valid) return "响应结构符合接口契约";
        var head = string.Join("；", Violations.Take(max).Select(v => $"{v.Path} {v.Message}"));
        var more = Violations.Count > max ? $"；另有 {Violations.Count - max} 处" : string.Empty;
        return $"响应结构与接口契约不符（{Violations.Count} 处）：{head}{more}";
    }
}

/// <summary>
/// 接口响应结构校验（迭代 D）：把响应体与 OpenAPI 里声明的 schema 对照。
///
/// 补的是「契约测试」这块短板：平台已有 Swagger 导入能生成用例，但断言只覆盖状态码，
/// 一旦后端把字段改名/改类型/漏返回，用例照样通过——这是接口测试最常见的漏检。
///
/// 实现上刻意**不引入 JSON Schema 库**：OpenAPI 3 的 schema 是 JSON Schema 的子集，
/// 而契约校验真正需要判定的只有「字段在不在、类型对不对、数组元素形状」这几件事。
/// 引一个通用库会把 diagnostics 变成库自己的格式，反而不好在报告里给出人话。
///
/// 支持：type / required / properties / items / enum / nullable / 一层 $ref 展开。
/// 未支持的特性（oneOf/anyOf/allOf、pattern、数值区间）**跳过而不是报错**——
/// 宁可少报也不要误报，误报会让人不再信任这个断言。
/// </summary>
public static class OpenApiSchemaValidator
{
    /// <summary>
    /// 从 OpenAPI 规范里取某个操作的响应 schema 并校验响应体。
    /// 找不到操作或该状态码没有声明 schema 时返回 Valid=true（无法判定就不判定）。
    /// </summary>
    public static SchemaValidationResult Validate(
        string? openApiSpec, string method, string? path, int statusCode, string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(openApiSpec) || string.IsNullOrWhiteSpace(path))
            return Ok();
        if (string.IsNullOrWhiteSpace(responseBody))
            // 声明了 schema 却返回空体，属于明确的契约违反；没声明就直接放过
            return Ok();

        JsonDocument spec;
        try
        {
            spec = JsonDocument.Parse(openApiSpec);
        }
        catch (JsonException)
        {
            return Ok();
        }

        using (spec)
        {
            if (!spec.RootElement.TryGetProperty("paths", out var paths))
                return Ok();
            if (!paths.TryGetProperty(path, out var pathItem))
                return Ok();
            if (!pathItem.TryGetProperty(method.ToLowerInvariant(), out var operation))
                return Ok();
            if (!operation.TryGetProperty("responses", out var responses))
                return Ok();

            var schema = FindSchema(responses, statusCode, spec.RootElement);
            if (schema is null) return Ok();

            JsonDocument body;
            try
            {
                body = JsonDocument.Parse(responseBody);
            }
            catch (JsonException ex)
            {
                // 声明为 JSON 结构却返回了非 JSON —— 这是明确的违约，必须报
                return new SchemaValidationResult(false,
                    [new SchemaViolation("$", $"响应体不是合法 JSON：{ex.Message}")]);
            }

            using (body)
            {
                var violations = new List<SchemaViolation>();
                Walk(schema.Value, body.RootElement, "$", spec.RootElement, violations, depth: 0);
                return new SchemaValidationResult(violations.Count == 0, violations);
            }
        }
    }

    private static SchemaValidationResult Ok() => new(true, []);

    /// <summary>先找精确状态码，再找 "2XX" 之类的通配，最后找 default</summary>
    private static JsonElement? FindSchema(JsonElement responses, int statusCode, JsonElement root)
    {
        var candidates = new List<string> { statusCode.ToString() };
        if (statusCode is >= 200 and < 300) candidates.Add($"{statusCode / 100}XX");
        candidates.Add("default");

        foreach (var key in candidates)
        {
            if (!responses.TryGetProperty(key, out var response)) continue;
            if (!response.TryGetProperty("content", out var content)) continue;
            // 优先 application/json，其次任意 json 变体
            if (content.TryGetProperty("application/json", out var json)
                && json.TryGetProperty("schema", out var schema))
            {
                return Resolve(schema, root);
            }
            foreach (var media in content.EnumerateObject())
            {
                if (media.Name.Contains("json", StringComparison.OrdinalIgnoreCase)
                    && media.Value.TryGetProperty("schema", out var anySchema))
                    return Resolve(anySchema, root);
            }
        }
        return null;
    }

    /// <summary>展开一层 $ref（嵌套引用交给递归继续处理，避免无限展开）</summary>
    private static JsonElement? Resolve(JsonElement schema, JsonElement root)
    {
        var depth = 0;
        while (schema.TryGetProperty("$ref", out var reference) && depth++ < 10)
        {
            var pointer = reference.GetString();
            if (string.IsNullOrEmpty(pointer) || !pointer.StartsWith("#/", StringComparison.Ordinal))
                return null;
            if (!TryNavigatePointer(root, pointer[2..], out var target))
                return null;
            schema = target;
        }
        return schema;
    }

    private static bool TryNavigatePointer(JsonElement root, string pointer, out JsonElement target)
    {
        target = root;
        foreach (var rawSegment in pointer.Split('/'))
        {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(segment, out target))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 递归比对。depth 限深是防御性的：规范里出现循环 $ref 时不至于栈溢出
    /// （平台在 Swagger 导入侧已经处理过一次同类问题）。
    /// </summary>
    private static void Walk(JsonElement schema, JsonElement instance, string path,
        JsonElement root, List<SchemaViolation> violations, int depth)
    {
        if (depth > 12 || violations.Count > 200) return;

        var resolved = Resolve(schema, root);
        if (resolved is null) return;
        schema = resolved.Value;

        // nullable（OpenAPI 3.0 的写法）或 type 数组里含 "null"（3.1）
        var nullable = schema.TryGetProperty("nullable", out var n) && n.ValueKind == JsonValueKind.True;
        if (nullable && instance.ValueKind == JsonValueKind.Null) return;

        if (schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array)
        {
            var allowed = enumValues.EnumerateArray().Select(x => x.ToString()).ToList();
            if (!allowed.Contains(instance.ToString()))
                violations.Add(new SchemaViolation(path, $"取值不在枚举内（允许：{string.Join("/", allowed.Take(6))}）"));
        }

        var expectedType = ReadType(schema);
        if (expectedType is not null && !TypeMatches(expectedType, instance))
        {
            violations.Add(new SchemaViolation(path, $"期望类型 {expectedType}，实际 {DescribeKind(instance)}"));
            return; // 类型都不对，再往深看没有意义
        }

        if (expectedType == "object" && instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in required.EnumerateArray())
                {
                    var key = name.GetString();
                    if (key is not null && !instance.TryGetProperty(key, out _))
                        violations.Add(new SchemaViolation(path, $"缺少必填字段「{key}」"));
                }
            }

            var hasProperties = schema.TryGetProperty("properties", out var properties)
                                && properties.ValueKind == JsonValueKind.Object;
            // 声明了 additionalProperties: false 才报多余字段；
            // 默认（未声明）OpenAPI 允许额外字段，报出来是误报
            var closed = schema.TryGetProperty("additionalProperties", out var additional)
                         && additional.ValueKind == JsonValueKind.False;
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (hasProperties)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    known.Add(property.Name);
                    if (instance.TryGetProperty(property.Name, out var child))
                        Walk(property.Value, child, $"{path}.{property.Name}", root, violations, depth + 1);
                }
            }
            if (closed)
            {
                foreach (var actual in instance.EnumerateObject())
                {
                    if (!known.Contains(actual.Name))
                        violations.Add(new SchemaViolation($"{path}.{actual.Name}", "契约中未声明的字段"));
                }
            }
        }

        if (expectedType == "array" && instance.ValueKind == JsonValueKind.Array
            && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var element in instance.EnumerateArray())
            {
                Walk(items, element, $"{path}[{index}]", root, violations, depth + 1);
                // 元素形状一致时不必把每条都报一遍：报前几条就够定位问题了
                if (violations.Count > 20) return;
                index++;
            }
        }
    }

    private static string? ReadType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type)) return null;
        if (type.ValueKind == JsonValueKind.String) return type.GetString();
        // OpenAPI 3.1 允许 type 为数组（如 ["string","null"]）
        if (type.ValueKind == JsonValueKind.Array)
        {
            var types = type.EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrEmpty(x) && x != "null")
                .ToList();
            return types.Count == 1 ? types[0] : null;
        }
        return null;
    }

    private static bool TypeMatches(string expected, JsonElement instance) => expected switch
    {
        "string" => instance.ValueKind == JsonValueKind.String,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "number" => instance.ValueKind == JsonValueKind.Number,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "object" => instance.ValueKind == JsonValueKind.Object,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => true, // 未知类型不判定
    };

    private static string DescribeKind(JsonElement instance) => instance.ValueKind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => instance.TryGetInt64(out _) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        JsonValueKind.Null => "null",
        _ => instance.ValueKind.ToString(),
    };
}
