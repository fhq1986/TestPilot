using System.Text.Json;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 把压测场景装配成 <see cref="K6ScriptInput"/>（迭代 F·P2-9）。
///
/// 放在 Api 层是因为要碰 EF 与 <see cref="StepConfig"/>；真正的翻译逻辑在
/// <see cref="K6ScriptGenerator"/>（纯函数，可单测）。
/// </summary>
public class LoadTestScriptBuilder
{
    private readonly TestDbContext _db;
    private readonly SwaggerImporter _importer;

    public LoadTestScriptBuilder(TestDbContext db, SwaggerImporter importer)
    {
        _db = db;
        _importer = importer;
    }

    public async Task<K6ScriptInput> BuildAsync(LoadTestScenario scenario, CancellationToken ct)
    {
        var cases = scenario.Source == LoadTestSource.OpenApi
            ? await BuildFromOpenApiAsync(scenario, ct)
            : await BuildFromCasesAsync(scenario, ct);

        return new K6ScriptInput(
            scenario.Name,
            ResolveBaseUrl(scenario),
            scenario.Variables ?? new Dictionary<string, string>(),
            scenario.Profile,
            scenario.Thresholds,
            cases);
    }

    /// <summary>
    /// 靶站地址：场景显式配置优先，其次环境的 BaseUrl。
    /// 两者都没有时给一个空串——脚本里会退化成 BASE_URL 的默认值，由 -e BASE_URL 注入，
    /// 总比在这里抛异常让「生成脚本」整个失败好。
    /// </summary>
    public string ResolveBaseUrl(LoadTestScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.TargetBaseUrl)) return scenario.TargetBaseUrl!.TrimEnd('/');
        if (scenario.Environment is { BaseUrl: { Length: > 0 } envUrl }) return envUrl.TrimEnd('/');
        return string.Empty;
    }

    // ---------------------------------------------------------------- 来源：接口用例

    private async Task<List<K6CaseSpec>> BuildFromCasesAsync(LoadTestScenario scenario, CancellationToken ct)
    {
        var ordered = await _db.LoadTestScenarioCases.AsNoTracking()
            .Where(c => c.ScenarioId == scenario.Id)
            .OrderBy(c => c.Order)
            .Select(c => c.TestCaseId)
            .ToListAsync(ct);
        if (ordered.Count == 0) return new List<K6CaseSpec>();

        var testCases = await _db.TestCases.AsNoTracking()
            .Where(t => ordered.Contains(t.Id))
            .Include(t => t.Steps)
            .ToListAsync(ct);

        var result = new List<K6CaseSpec>();
        foreach (var caseId in ordered)
        {
            var testCase = testCases.FirstOrDefault(t => t.Id == caseId);
            if (testCase is null) continue;

            var steps = testCase.Steps
                .OrderBy(s => s.StepOrder)
                .Select(ToStepSpec)
                .ToList();

            result.Add(new K6CaseSpec(testCase.Name, steps));
        }
        return result;
    }

    private static K6StepSpec ToStepSpec(TestStep step)
    {
        // 共享步骤引用是「占位」，运行时才展开。压测脚本不做展开（展开规则在
        // SharedStepExpander 里，与执行链路耦合），明确标成不支持而不是生成一个空步骤——
        // 空步骤会让 checks 通过率虚高，比少测一步更危险。
        if (step.SharedGroupId is not null)
            return new K6StepSpec(step.StepOrder, null, null, null, "共享步骤组（压测暂不展开）");

        var cfg = step.Config;
        return step.ActionType switch
        {
            ActionType.Request => new K6StepSpec(step.StepOrder,
                new K6RequestSpec(cfg.Method ?? "GET", cfg.Endpoint ?? string.Empty,
                    (cfg.Headers ?? new List<HeaderEntry>())
                        .Select(h => new K6HeaderSpec(h.Name, h.Value)).ToList(),
                    cfg.Body),
                null, null, null),

            ActionType.AssertResponse => new K6StepSpec(step.StepOrder, null,
                new K6AssertSpec(cfg.Value, cfg.Body), null, null),

            ActionType.ExtractVariable => new K6StepSpec(step.StepOrder, null, null,
                new K6ExtractSpec(cfg.Value ?? string.Empty, cfg.Endpoint ?? string.Empty), null),

            _ => new K6StepSpec(step.StepOrder, null, null, null, step.ActionType.ToString()),
        };
    }

    // ---------------------------------------------------------------- 来源：OpenAPI

    private async Task<List<K6CaseSpec>> BuildFromOpenApiAsync(LoadTestScenario scenario, CancellationToken ct)
    {
        if (scenario.ApiDefinitionId is not { } defId) return new List<K6CaseSpec>();

        var definition = await _db.ApiDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == defId, ct);
        if (definition is null) return new List<K6CaseSpec>();

        var (_, _, endpoints) = _importer.Parse(definition.Spec);
        var selected = scenario.Operations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cases = new List<K6CaseSpec>();
        foreach (var endpoint in endpoints)
        {
            var key = $"{endpoint.Method.ToUpperInvariant()} {endpoint.Path}";
            if (!selected.Contains(key)) continue;

            var steps = new List<K6StepSpec>
            {
                new(1, new K6RequestSpec(endpoint.Method.ToUpperInvariant(),
                    FillPathParameters(endpoint.Path), new List<K6HeaderSpec>(), BuildBody(endpoint)), null, null, null),
            };

            // 状态码断言：取声明里最小的 2xx；一个都没有时用 2xx 区间
            var success = endpoint.ResponseCodes.Where(c => c is >= 200 and < 300).OrderBy(c => c).ToList();
            steps.Add(new K6StepSpec(2, null,
                new K6AssertSpec(success.Count > 0 ? success[0].ToString() : "2xx", null), null, null));

            cases.Add(new K6CaseSpec(key, steps));
        }
        return cases;
    }

    /// <summary>路径参数填占位值：压测只关心"这条链路能不能扛量"，具体取值不是重点</summary>
    private static string FillPathParameters(string path) =>
        System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]+\}", "1");

    /// <summary>按 requestBody 的 schema 造一份合法 JSON；没有 schema 时返回 null（不带 body）</summary>
    private static string? BuildBody(ApiEndpointSpec endpoint)
    {
        var props = endpoint.RequestBody?.Properties;
        if (props is null || props.Count == 0) return null;
        return JsonSerializer.Serialize(BoundaryCaseGenerator.BuildValidBody(props));
    }
}
