using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace AI.TestPlatform.IntegrationTests;

/// <summary>
/// OpenAPI 结构校验。
///
/// Swashbuckle 的 schema 生成是惰性的：只要 swagger.json 生成不出来（500），
/// UI 与前端类型生成都跟着坏，而常规接口测试根本碰不到这条路径——必须专门请求一次文档。
/// 校验三件事：
/// 1. 文档能生成（200 且是合法 OpenAPI 3.0 JSON）；
/// 2. 核心端点齐全（防止模块注册被误删/漏挂后无人察觉）；
/// 3. 所有 $ref 可解析（Swashbuckle 遇到无法生成 schema 的类型时会产生悬空引用）。
/// </summary>
[Collection("api")]
public class OpenApiStructureTests
{
    private readonly TestApiFactory _factory;

    public OpenApiStructureTests(TestApiFactory factory) => _factory = factory;

    private static readonly string[] CorePaths =
    {
        "/api/auth/login",
        "/api/projects",
        "/api/projects/{projectId}/api-tokens",
        "/api/testcases",
        "/api/executions",
        "/api/projects/{projectId}/environments",
        "/api/webhooks/executions",
        "/api/datasets",
        "/api/defects",
        "/api/requirements",
    };

    private HttpClient? _devClient;

    /// <summary>
    /// UseSwagger 只在 Development 注册，而测试宿主的环境未必是——显式指定。
    /// 派生宿主会重新走一遍启动流程（含迁移），三个测试共享一个实例。
    /// </summary>
    private HttpClient CreateDevelopmentClient() =>
        _devClient ??= _factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    [Fact]
    public async Task SwaggerDocument_可生成且结构合法()
    {
        var client = CreateDevelopmentClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.True(response.IsSuccessStatusCode, $"swagger.json 生成失败: {(int)response.StatusCode}");

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.True(json.RootElement.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.", version.GetString());

        Assert.True(json.RootElement.TryGetProperty("info", out _));
        Assert.True(json.RootElement.TryGetProperty("paths", out var paths));
        Assert.True(paths.GetArrayLength() > 30, $"paths 只有 {paths.GetArrayLength()} 个，明显偏少");
    }

    [Fact]
    public async Task SwaggerDocument_核心端点齐全()
    {
        var client = CreateDevelopmentClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var declared = json.RootElement.GetProperty("paths")
            .EnumerateObject().Select(p => p.Name).ToHashSet();

        var missing = CorePaths.Where(p => !declared.Contains(p)).ToList();
        Assert.True(missing.Count == 0, $"核心端点缺失: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task SwaggerDocument_所有Schema引用可解析()
    {
        var client = CreateDevelopmentClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        // 收集全部 $ref 引用
        var refs = new HashSet<string>();
        CollectRefs(root, refs);

        // 收集已定义的组件（schemas/parameters/responses...）
        var defined = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("components", out var components))
        {
            foreach (var group in components.EnumerateObject())
                foreach (var item in group.Value.EnumerateObject())
                    defined.Add($"#/components/{group.Name}/{item.Name}");
        }

        var dangling = refs.Where(r => !defined.Contains(r)).ToList();
        Assert.True(dangling.Count == 0,
            $"悬空 $ref（Swashbuckle 无法生成 schema 的类型会这样暴露）: {string.Join(", ", dangling.Take(5))}");
    }

    private static void CollectRefs(JsonElement element, HashSet<string> refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("$ref") && prop.Value.ValueKind == JsonValueKind.String)
                        refs.Add(prop.Value.GetString()!);
                    else
                        CollectRefs(prop.Value, refs);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectRefs(item, refs);
                break;
        }
    }
}
