using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class BoundaryCaseGeneratorTests
{
    private static ApiSchemaSpec StringSchema(int min, int max) => new(
        "string", null, true, null, null, false, false, min, max, null, null, null);

    [Fact]
    public void Generate_HappyPath_UsesExampleValues()
    {
        var endpoint = new ApiEndpointSpec("GET", "/users/{id}",
            new List<ApiParameterSpec>
            {
                new("id", "path", true, new ApiSchemaSpec("integer", "int64", true, null, null, false, false, null, null, null, null, null)),
            },
            null, new List<int> { 200, 404 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        Assert.Contains(cases, c => c.Name.Contains("正向") && c.Steps.Count == 2);
        Assert.All(cases, c => Assert.Equal(2, c.Steps.Count)); // Request + AssertResponse
    }

    [Fact]
    public void Generate_StringBoundaries_ProducesMinMinusOneMaxPlusOne()
    {
        var endpoint = new ApiEndpointSpec("POST", "/users", new List<ApiParameterSpec>(),
            new ApiSchemaSpec("object", null, true, null, null, false, false, null, null, null, null,
                new Dictionary<string, ApiSchemaSpec>
                {
                    ["name"] = StringSchema(1, 10),
                }),
            new List<int> { 201 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        var names = cases.Select(c => c.Name).ToList();
        Assert.Contains(names, n => n.Contains("最小长度") || n.Contains("min"));
        Assert.Contains(names, n => n.Contains("最大长度") || n.Contains("max"));
    }

    [Fact]
    public void Generate_RequiredMissing_Produces4xxCase()
    {
        var endpoint = new ApiEndpointSpec("POST", "/users", new List<ApiParameterSpec>(),
            new ApiSchemaSpec("object", null, true, null, null, false, false, null, null, null, null,
                new Dictionary<string, ApiSchemaSpec>
                {
                    ["name"] = StringSchema(1, 10),
                }),
            new List<int> { 201 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        Assert.Contains(cases, c => c.Name.Contains("缺失") && c.Steps.Any(s => s.ActionType == ActionType.AssertResponse));
    }

    [Fact]
    public void Generate_EnumInvalidValue_Produced()
    {
        var endpoint = new ApiEndpointSpec("POST", "/users", new List<ApiParameterSpec>(),
            new ApiSchemaSpec("object", null, true, null, null, false, false, null, null, null, null,
                new Dictionary<string, ApiSchemaSpec>
                {
                    ["status"] = new ApiSchemaSpec("string", null, true, null, null, false, false, null, null,
                        new List<string> { "active", "disabled" }, null, null),
                }),
            new List<int> { 201 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        Assert.Contains(cases, c => c.Name.Contains("枚举"));
    }

    [Fact]
    public void Generate_TypeViolation_Produced()
    {
        var endpoint = new ApiEndpointSpec("POST", "/users", new List<ApiParameterSpec>(),
            new ApiSchemaSpec("object", null, true, null, null, false, false, null, null, null, null,
                new Dictionary<string, ApiSchemaSpec>
                {
                    ["age"] = new ApiSchemaSpec("integer", null, true, null, null, false, false, null, null, null, null, null),
                }),
            new List<int> { 201 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        Assert.Contains(cases, c => c.Name.Contains("类型"));
    }

    [Fact]
    public void Generate_CapsAtMaxBoundaryCases()
    {
        var props = new Dictionary<string, ApiSchemaSpec>
        {
            ["a"] = StringSchema(1, 100),
            ["b"] = StringSchema(1, 100),
            ["c"] = StringSchema(1, 100),
            ["d"] = StringSchema(1, 100),
        };
        var endpoint = new ApiEndpointSpec("POST", "/users", new List<ApiParameterSpec>(),
            new ApiSchemaSpec("object", null, true, null, null, false, false, null, null, null, null, props),
            new List<int> { 201 });

        var cases = BoundaryCaseGenerator.Generate(endpoint);

        Assert.True(cases.Count <= 8 + 1); // 边界上限 8 + 1 正例
    }
}
