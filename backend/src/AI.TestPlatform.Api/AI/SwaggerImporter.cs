using AI.TestPlatform.Application.ApiTesting;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace AI.TestPlatform.Api.AI;

// Swagger/OpenAPI 导入：解析 OpenAPI 文档为 ApiEndpointSpec 列表
public class SwaggerImporter
{
    public (string apiName, string? baseUrl, List<ApiEndpointSpec> endpoints) Parse(string specJson)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(specJson, out var diagnostic);
        // 1.6.31 实测：非法 JSON → document 非 null 但 Paths 为 null；
        // 复合 type（["string","null"]）→ document 正常，仅诊断报错，可容忍
        if (document is null || document.Paths is null)
            throw new InvalidOperationException(
                $"OpenAPI 解析失败: {string.Join("; ", diagnostic.Errors.Select(e => e.Message).Take(3))}");

        var apiName = document.Info?.Title ?? "未命名 API";
        var baseUrl = document.Servers?.FirstOrDefault()?.Url;

        var endpoints = new List<ApiEndpointSpec>();
        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var op in pathItem.Operations)
            {
                var parameters = (pathItem.Parameters ?? new List<OpenApiParameter>())
                    .Concat(op.Value.Parameters ?? new List<OpenApiParameter>())
                    .Select(p => new ApiParameterSpec(p.Name, p.In?.ToString().ToLowerInvariant() ?? "query",
                        p.Required, ToSchemaSpec(p.Schema, required: p.Required)))
                    .ToList();
                var requestBody = op.Value.RequestBody?.Content
                    .FirstOrDefault(c => c.Key.Contains("json")).Value?.Schema;
                var responses = op.Value.Responses.Keys
                    .Select(k => int.TryParse(k, out var code) ? code : 0)
                    .Where(c => c > 0).ToList();
                endpoints.Add(new ApiEndpointSpec(
                    op.Key.ToString().ToUpperInvariant(), path, parameters,
                    requestBody is null ? null : ToSchemaSpec(requestBody, required: false),
                    responses));
            }
        }
        return (apiName, baseUrl, endpoints);
    }

    private static ApiSchemaSpec ToSchemaSpec(OpenApiSchema schema, bool required)
        => ToSchemaSpec(schema, required, 0, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance));

    private static ApiSchemaSpec ToSchemaSpec(
        OpenApiSchema schema, bool required, int depth, HashSet<OpenApiSchema> visited)
    {
        if (schema is null)
            return new ApiSchemaSpec(null, null, required, null, null, false, false,
                null, null, null, null, null);

        // 循环引用（自引用 $ref）或深度超限：浅拷贝，不再递归
        if (!visited.Add(schema) || depth > 8)
            return new ApiSchemaSpec(
                schema.Type, schema.Format, required,
                schema.Minimum, schema.Maximum,
                schema.ExclusiveMinimum ?? false, schema.ExclusiveMaximum ?? false,
                schema.MinLength, schema.MaxLength,
                schema.Enum?.Select(e => AnyToString(e) ?? "").ToList(),
                null, null);

        try
        {
            var props = schema.Properties?
                .ToDictionary(p => p.Key, p => ToSchemaSpec(p.Value,
                    schema.Required?.Contains(p.Key) == true, depth + 1, visited));
            return new ApiSchemaSpec(
                schema.Type, schema.Format, required,
                schema.Minimum, schema.Maximum,
                schema.ExclusiveMinimum ?? false, schema.ExclusiveMaximum ?? false,
                schema.MinLength, schema.MaxLength,
                schema.Enum?.Select(e => AnyToString(e) ?? "").ToList(),
                schema.Items is null ? null : ToSchemaSpec(schema.Items, false, depth + 1, visited),
                props);
        }
        finally
        {
            visited.Remove(schema);
        }
    }

    // OpenApiPrimitive 未覆写 ToString（返回类型名），按具体类型取值
    private static string? AnyToString(IOpenApiAny? any) => any switch
    {
        OpenApiString s => s.Value,
        OpenApiInteger i => i.Value.ToString(),
        OpenApiLong l => l.Value.ToString(),
        OpenApiDouble d => d.Value.ToString(),
        OpenApiFloat f => f.Value.ToString(),
        OpenApiBoolean b => b.Value.ToString(),
        OpenApiNull => null,
        _ => any?.ToString(),
    };
}
