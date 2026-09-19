using System.Text.Json;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.ApiTesting;

// 确定性边界值/非法输入用例生成（含 fuzz 类：类型违规、超长、特殊字符）
public static class BoundaryCaseGenerator
{
    public const int MaxBoundaryCases = 8;

    public static List<GeneratedApiCase> Generate(ApiEndpointSpec endpoint, int maxBoundary = MaxBoundaryCases)
    {
        var cases = new List<GeneratedApiCase>();
        var bodyProps = endpoint.RequestBody?.Properties ?? new Dictionary<string, ApiSchemaSpec>();

        // 1. 正向用例（每个 2xx 一个，合并为一正例）
        var successCode = endpoint.ResponseCodes.FirstOrDefault(c => c is >= 200 and < 300);
        var happyPayload = BuildValidBody(bodyProps);
        cases.Add(new GeneratedApiCase(
            $"{endpoint.Method} {endpoint.Path} 正向请求",
            "P1",
            "合法参数调用返回成功",
            new List<CreateTestStepRequest>
            {
                new(0, ActionType.Request, BuildRequestConfig(endpoint, happyPayload), null, null),
                new(1, ActionType.AssertResponse, new StepConfig
                {
                    Value = successCode == 0 ? "2xx" : successCode.ToString(),
                }, null, null),
            }));

        // 2. required 缺失
        var required = bodyProps.FirstOrDefault(p => p.Value.Required);
        if (required.Key is not null)
        {
            var missingPayload = BuildValidBody(bodyProps);
            missingPayload.Remove(required.Key);
            cases.Add(BoundaryCase($"{required.Key} 缺失", endpoint, missingPayload, "4xx"));
        }

        // 3. 字符串边界
        foreach (var (name, schema) in bodyProps.Where(p => p.Value.Type == "string" && p.Value.Enum is null))
        {
            if (cases.Count - 1 >= maxBoundary) break;
            if (schema.MinLength is { } min && min > 0)
            {
                cases.Add(BoundaryCase($"{name} 最小长度-1", endpoint, With(bodyProps, name, new string('a', Math.Max(0, min - 1))), "4xx"));
                if (cases.Count - 1 >= maxBoundary) break;
            }
            if (schema.MaxLength is { } max)
                cases.Add(BoundaryCase($"{name} 最大长度+1", endpoint, With(bodyProps, name, new string('a', max + 1)), "4xx"));
        }

        // 4. 数值边界
        foreach (var (name, schema) in bodyProps.Where(p => p.Value.Type is "integer" or "number"))
        {
            if (cases.Count - 1 >= maxBoundary) break;
            if (schema.Minimum is { } min)
            {
                cases.Add(BoundaryCase($"{name} 最小值-1", endpoint, With(bodyProps, name, min - 1m), "4xx"));
                if (cases.Count - 1 >= maxBoundary) break;
            }
            if (schema.Maximum is { } max)
                cases.Add(BoundaryCase($"{name} 最大值+1", endpoint, With(bodyProps, name, max + 1m), "4xx"));
        }

        // 5. 枚举非法值
        foreach (var (name, schema) in bodyProps.Where(p => p.Value.Enum is { Count: > 0 }))
        {
            if (cases.Count - 1 >= maxBoundary) break;
            cases.Add(BoundaryCase($"{name} 枚举非法值", endpoint, With(bodyProps, name, "INVALID_ENUM_VALUE"), "4xx"));
        }

        // 6. 类型违规（fuzz 类）
        var first = bodyProps.FirstOrDefault();
        if (first.Key is not null && cases.Count - 1 < maxBoundary)
            cases.Add(BoundaryCase($"{first.Key} 类型违规", endpoint, With(bodyProps, first.Key, "not-a-valid-type-value"), "4xx"));

        // 7. 超长字符串（fuzz 类）
        var anyString = bodyProps.FirstOrDefault(p => p.Value.Type == "string");
        if (anyString.Key is not null && cases.Count - 1 < maxBoundary)
            cases.Add(BoundaryCase($"{anyString.Key} 超长字符串", endpoint, With(bodyProps, anyString.Key, new string('x', 5000)), "4xx"));

        return cases;
    }

    private static GeneratedApiCase BoundaryCase(string name, ApiEndpointSpec endpoint, object payload, string expectedStatus) => new(
        $"{endpoint.Method} {endpoint.Path} 边界-{name}",
        "P2",
        "边界值/非法输入应被拒绝（4xx）",
        new List<CreateTestStepRequest>
        {
            new(0, ActionType.Request, BuildRequestConfig(endpoint, payload), null, null),
            new(1, ActionType.AssertResponse, new StepConfig { Value = expectedStatus }, null, null),
        });

    // 深拷贝合法 payload（BuildValidBody 每次全新建字典，含嵌套对象），覆盖指定字段
    private static Dictionary<string, object?> With(
        Dictionary<string, ApiSchemaSpec> props, string key, object value)
    {
        var payload = BuildValidBody(props);
        payload[key] = value;
        return payload;
    }

    public static Dictionary<string, object?> BuildValidBody(Dictionary<string, ApiSchemaSpec> props)
    {
        var obj = new Dictionary<string, object?>();
        foreach (var (name, schema) in props)
        {
            obj[name] = schema.Type switch
            {
                "string" => schema.Enum is { Count: > 0 } ? schema.Enum[0] : "示例",
                "integer" => 0,
                "number" => 0.0,
                "boolean" => true,
                "array" => schema.Items is null ? new List<object?>() : new List<object?> { BuildScalar(schema.Items) },
                "object" => schema.Properties is null ? new Dictionary<string, object?>() : BuildValidBody(schema.Properties),
                _ => null,
            };
        }
        return obj;
    }

    private static object? BuildScalar(ApiSchemaSpec schema) => schema.Type switch
    {
        "string" => schema.Enum is { Count: > 0 } ? schema.Enum[0] : "示例",
        "integer" => 0,
        "number" => 0.0,
        "boolean" => true,
        _ => null,
    };

    private static StepConfig BuildRequestConfig(ApiEndpointSpec endpoint, object payload)
    {
        var method = endpoint.Method.ToUpperInvariant();
        if (method == "GET")
            return new StepConfig { Method = "GET", Endpoint = endpoint.Path };
        return new StepConfig
        {
            Method = method,
            Endpoint = endpoint.Path,
            Body = JsonSerializer.Serialize(payload),
        };
    }
}
