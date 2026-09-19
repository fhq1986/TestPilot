using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ExecutionEntity = AI.TestPlatform.Domain.Entities.Execution;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 执行计划器：把「一批用例」展开成待入队的执行记录。
///
/// 四条触发链路（手动/批量、CI Webhook、定时任务、测试套件）共用这里的展开规则，避免口径不一致：
/// 1. 浏览器矩阵：请求指定多个浏览器时按「用例 × 浏览器」展开；未指定则逐用例按「环境 → 用例 → chromium」解析；
/// 2. 数据驱动：用例绑定数据集且开启展开时，按数据行逐行生成执行（行内变量在执行时注入步骤配置）；
/// 3. 变量覆盖：请求携带的 Variables 优先于数据集行（CI 可用不同账号跑同一用例）。
/// 4. 套件编排：来自套件时把成员的前置用例（DependsOn）复制到执行记录上，供抢占门禁与收尾编排判定。
/// </summary>
public class ExecutionPlanner
{
    private readonly TestDbContext _db;
    private readonly ILogger<ExecutionPlanner> _logger;

    public ExecutionPlanner(TestDbContext db, ILogger<ExecutionPlanner> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>展开结果</summary>
    public record PlanResult(List<ExecutionEntity> Executions, List<Guid> SkippedCaseIds, int CasesWithoutData);

    public async Task<PlanResult> PlanAsync(
        IEnumerable<Guid> testCaseIds,
        Guid? environmentId,
        List<string>? browsers,
        bool expandDataSets,
        Dictionary<string, string>? variables,
        TriggerType triggerType,
        string? triggerSource,
        Guid? triggeredById,
        Guid? suiteId = null,
        Guid? suiteRunId = null,
        Guid? planId = null,
        Guid? planRoundId = null,
        string? commitSha = null,
        string? branch = null,
        string? buildNumber = null,
        CancellationToken ct = default)
    {
        var requested = testCaseIds.Distinct().ToList();
        var cases = await _db.TestCases.AsNoTracking()
            .Include(t => t.DataSet)
            .Where(t => requested.Contains(t.Id))
            .ToListAsync(ct);

        // 不支持的用例（移动端）与不存在的用例都计入 skipped
        var supported = cases.Where(t => t.Type != TestType.Mobile).ToList();
        var skipped = requested.Except(supported.Select(t => t.Id)).ToList();

        var environment = environmentId is null
            ? null
            : await _db.Environments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == environmentId.Value, ct);

        // 套件编排：取成员的前置用例（用例 → 前置用例）
        var dependencies = await LoadDependenciesAsync(suiteId, supported.Select(t => t.Id).ToList(), ct);

        // 请求显式指定浏览器 → 全用例统一按矩阵执行；未指定 → 逐用例按「环境 → 用例 → chromium」解析
        var explicitBrowsers = NormalizeBrowsers(browsers);
        var executions = new List<ExecutionEntity>();
        var casesWithoutData = 0;

        foreach (var testCase in supported)
        {
            var rows = expandDataSets
                ? ExtractRows(testCase)
                : new List<RowSpec>();

            // 绑定了数据集却没有任何数据行：仍执行一次（不带变量），并单独计数以便前端提示
            if (expandDataSets && testCase.DataSetId is not null && rows.Count == 0)
                casesWithoutData++;
            if (rows.Count == 0)
                rows.Add(new RowSpec(null, null, null));

            var browserForCase = explicitBrowsers.Count > 0
                ? explicitBrowsers
                : new List<string> { BrowserCatalog.Normalize(environment?.Browser ?? testCase.Browser) };

            foreach (var browser in browserForCase)
            {
                foreach (var row in rows)
                {
                    executions.Add(new ExecutionEntity
                    {
                        TestCaseId = testCase.Id,
                        EnvironmentId = environmentId,
                        TriggerType = triggerType,
                        TriggerSource = triggerSource,
                        TriggeredById = triggeredById,
                        Status = ExecutionStatus.Pending,
                        BrowserName = browser,
                        DataSetRowIndex = row.Index,
                        DataSetRowLabel = row.Label,
                        Variables = MergeVariables(row.Data, variables),
                        SuiteId = suiteId,
                        SuiteRunId = suiteRunId,
                        DependsOnTestCaseId = dependencies.GetValueOrDefault(testCase.Id),
                        PlanId = planId,
                        PlanRoundId = planRoundId,
                        CommitSha = commitSha,
                        Branch = branch,
                        BuildNumber = buildNumber,
                    });
                }
            }
        }

        _logger.LogInformation("执行计划：用例 {Cases} 个 → 执行 {Executions} 条（浏览器 {Browsers}）",
            supported.Count, executions.Count,
            explicitBrowsers.Count > 0 ? string.Join('/', explicitBrowsers) : "按用例/环境解析");

        return new PlanResult(executions, skipped, casesWithoutData);
    }

    /// <summary>
    /// 套件成员的前置用例映射（只有套件触发的计划才有值）。
    ///
    /// 前置用例必须**也在本次计划范围内**：否则这一条会去等一个本次根本不跑的用例，
    /// 而抢占门禁要求「前置全部通过」才放行——它会永远停在待执行。
    /// 套件保存时已经校验过依赖都在套件内，走到这里还被剔除只可能是计划期剔掉的
    /// （例如移动端用例不支持执行），此时丢掉该依赖并记日志，让用例照常跑。
    /// </summary>
    private async Task<Dictionary<Guid, Guid?>> LoadDependenciesAsync(
        Guid? suiteId, List<Guid> plannedCaseIds, CancellationToken ct)
    {
        if (suiteId is null)
            return new Dictionary<Guid, Guid?>();

        var members = await _db.TestSuiteCases.AsNoTracking()
            .Where(c => c.SuiteId == suiteId.Value && c.DependsOnTestCaseId != null)
            .Select(c => new { c.TestCaseId, c.DependsOnTestCaseId })
            .ToListAsync(ct);
        if (members.Count == 0)
            return new Dictionary<Guid, Guid?>();

        var planned = plannedCaseIds.ToHashSet();
        var result = new Dictionary<Guid, Guid?>();
        foreach (var member in members)
        {
            if (!planned.Contains(member.TestCaseId))
                continue;
            if (!planned.Contains(member.DependsOnTestCaseId!.Value))
            {
                _logger.LogWarning("用例 {CaseId} 的前置用例 {DependsOnId} 不在本次计划范围内，本次按无前置处理",
                    member.TestCaseId, member.DependsOnTestCaseId);
                continue;
            }
            result[member.TestCaseId] = member.DependsOnTestCaseId;
        }
        return result;
    }

    /// <summary>归一化请求指定的浏览器集合（去重、剔除空值、按引擎数量截断）</summary>
    private static List<string> NormalizeBrowsers(List<string>? browsers) =>
        (browsers ?? new List<string>())
        .Where(b => !string.IsNullOrWhiteSpace(b))
        .Select(BrowserCatalog.Normalize)
        .Distinct()
        .Take(BrowserCatalog.All.Count)
        .ToList();

    private record RowSpec(int? Index, string? Label, Dictionary<string, string>? Data);

    /// <summary>取出用例数据集的所有数据行（含行号与可读标签）</summary>
    private static List<RowSpec> ExtractRows(TestCase testCase)
    {
        var dataSet = testCase.DataSet;
        if (dataSet is null || dataSet.Rows.Count == 0)
            return new List<RowSpec>();

        var result = new List<RowSpec>(dataSet.Rows.Count);
        for (var i = 0; i < dataSet.Rows.Count; i++)
        {
            var row = dataSet.Rows[i] ?? new Dictionary<string, string>();
            result.Add(new RowSpec(i, BuildRowLabel(dataSet.Columns, row, i),
                new Dictionary<string, string>(row)));
        }
        return result;
    }

    /// <summary>数据行标签：取前 3 个非空列拼成「列=值」，过长则截断</summary>
    public static string BuildRowLabel(IReadOnlyList<string> columns, Dictionary<string, string> row, int index)
    {
        var parts = new List<string>();
        foreach (var column in columns)
        {
            if (parts.Count >= 3) break;
            if (!row.TryGetValue(column, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            var shown = value.Length <= 30 ? value : value[..30] + "…";
            parts.Add($"{column}={shown}");
        }
        var label = parts.Count > 0 ? $"#{index + 1} {string.Join(", ", parts)}" : $"#{index + 1}";
        return label.Length <= 300 ? label : label[..300];
    }

    /// <summary>数据集行与请求变量的合并：请求变量优先</summary>
    private static Dictionary<string, string>? MergeVariables(
        Dictionary<string, string>? rowData, Dictionary<string, string>? overrides)
    {
        if (rowData is null or { Count: 0 })
            return overrides is { Count: > 0 } ? new Dictionary<string, string>(overrides) : null;

        var merged = new Dictionary<string, string>(rowData);
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides) merged[key] = value;
        }
        return merged;
    }
}
