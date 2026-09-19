using System.Diagnostics;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>
/// 测试用例 Excel 导入：解析模板 → （可选）AI 把文字步骤转成可执行步骤 → 落库。
/// 单行失败不影响其它行，逐行统计结果。
/// </summary>
public class TestCaseImportService
{
    /// <summary>单次 AI 请求携带的用例条数（过多会超出模型输出上限）。</summary>
    private const int AiBatchSize = 5;

    /// <summary>AI 解析整体时间上限，超出后剩余用例按「仅基础信息」导入并给出提示。</summary>
    private const int AiTotalDeadlineSeconds = 300;

    private readonly TestDbContext _db;
    private readonly AIClient _aiClient;
    private readonly ILogger<TestCaseImportService> _logger;

    public TestCaseImportService(TestDbContext db, AIClient aiClient, ILogger<TestCaseImportService> logger)
    {
        _db = db;
        _aiClient = aiClient;
        _logger = logger;
    }

    public async Task<TestCaseImportResultDto> ImportAsync(
        Stream stream, Guid projectId, bool useAi, string? baseUrl, bool overwrite,
        Guid? testPlanId, CancellationToken ct)
    {
        var parsed = ExcelTestCaseParser.Parse(stream);
        var warnings = new List<string>(parsed.Warnings);
        var errors = new List<ImportRowErrorDto>();
        var moduleStats = new List<ImportModuleStatDto>();

        var duplicated = parsed.Modules
            .SelectMany(m => m.Rows.Select(r => r.CaseCode))
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicated.Count > 0)
            warnings.Add($"检测到 {duplicated.Count} 个用例编号在多个模块中重复" +
                         $"（如 {string.Join("、", duplicated.Take(3))}），已按「模块 + 编号」分别导入");

        var aiSteps = new Dictionary<string, List<GeneratedStepDto>>(StringComparer.OrdinalIgnoreCase);
        var aiParsedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (useAi)
            await FillAiStepsAsync(parsed, baseUrl, aiSteps, aiParsedCodes, warnings, ct);

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        // 本次导入实际落库的用例（新增 + 覆盖更新）：供可选的测试计划关联使用
        var importedCaseIds = new List<Guid>();

        foreach (var module in parsed.Modules)
        {
            var mImported = 0;
            var mSkipped = 0;
            var mFailed = 0;

            foreach (var row in module.Rows)
            {
                try
                {
                    // 去重键为「模块 + 用例编号」：模板中不同模块可能复用相同编号
                    var existing = await _db.TestCases
                        .Include(t => t.Steps)
                        .FirstOrDefaultAsync(t => t.ProjectId == projectId
                                                  && t.CaseCode == row.CaseCode
                                                  && t.Module == module.Name, ct);

                    if (existing is not null && !overwrite)
                    {
                        mSkipped++;
                        skipped++;
                        continue;
                    }

                    var steps = BuildSteps(aiSteps.TryGetValue(row.CaseCode, out var s) ? s : null, baseUrl);

                    if (existing is null)
                    {
                        var testCase = new TestCase
                        {
                            ProjectId = projectId,
                            Name = row.Scenario,
                            Type = row.Type,
                            Description = string.IsNullOrWhiteSpace(row.Expected) ? null : Truncate(row.Expected, 2000),
                            Priority = row.Priority,
                            CaseCode = row.CaseCode,
                            Module = module.Name,
                            SourceSteps = row.SourceSteps,
                            ExpectedResult = Truncate(row.Expected, 2000),
                            BaseUrl = baseUrl,
                            Status = TestCaseStatus.Draft,
                            AIGenerated = steps.Count > 0,
                            AIPrompt = steps.Count > 0
                                ? $"Excel 导入（{module.Name} / {row.CaseCode}）由 AI 生成可执行步骤"
                                : null,
                            Steps = steps,
                        };
                        _db.TestCases.Add(testCase);
                        imported++;
                        mImported++;
                        await _db.SaveChangesAsync(ct);
                        // SaveChanges 后主键已填充，才能用于计划关联
                        importedCaseIds.Add(testCase.Id);
                    }
                    else
                    {
                        existing.Name = row.Scenario;
                        existing.Type = row.Type;
                        existing.Description = string.IsNullOrWhiteSpace(row.Expected) ? null : Truncate(row.Expected, 2000);
                        existing.Priority = row.Priority;
                        existing.Module = module.Name;
                        existing.SourceSteps = row.SourceSteps;
                        existing.ExpectedResult = Truncate(row.Expected, 2000);
                        if (!string.IsNullOrWhiteSpace(baseUrl))
                            existing.BaseUrl = baseUrl;
                        existing.AIGenerated = steps.Count > 0;
                        existing.UpdatedAt = DateTime.UtcNow;
                        _db.TestSteps.RemoveRange(existing.Steps);
                        existing.Steps = steps;
                        updated++;
                        mImported++;
                        await _db.SaveChangesAsync(ct);
                        importedCaseIds.Add(existing.Id);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    mFailed++;
                    _logger.LogWarning(ex, "导录用例失败：{Module} 第 {Row} 行 {Code}", module.Name, row.RowNumber, row.CaseCode);
                    errors.Add(new ImportRowErrorDto(module.Name, row.RowNumber, row.CaseCode, ex.Message));
                    // 清空失败行的跟踪状态，避免污染后续保存
                    foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
            }

            moduleStats.Add(new ImportModuleStatDto(module.Name, module.Rows.Count, mImported, mSkipped, mFailed));
        }

        var (planLinked, planSkipped) = await TestPlanLinker.LinkAsync(
            _db, _logger, testPlanId, importedCaseIds, warnings, ct);

        if (useAi)
        {
            var missing = parsed.Modules.SelectMany(m => m.Rows)
                .Count(r => !aiParsedCodes.Contains(r.CaseCode));
            if (missing > 0)
                warnings.Add($"{missing} 条用例未成功生成可执行步骤（已按基础信息导入，可在用例编辑页补充或用 AI 重新生成）");
        }

        return new TestCaseImportResultDto(
            parsed.TotalRows, imported, updated, skipped, failed,
            aiParsedCodes.Count, useAi ? parsed.TotalRows : 0,
            moduleStats, errors, warnings,
            planLinked, planSkipped);
    }

    /// <summary>批量调用 AI Worker 生成可执行步骤（分批 + 整体超时保护，失败不影响导入）。</summary>
    private async Task FillAiStepsAsync(
        ParsedWorkbook parsed, string? baseUrl,
        Dictionary<string, List<GeneratedStepDto>> aiSteps,
        HashSet<string> aiParsedCodes, List<string> warnings, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        var rows = parsed.Modules
            .SelectMany(m => m.Rows.Select(r => (Module: m.Name, Row: r)))
            .ToList();

        foreach (var batch in rows.Chunk(AiBatchSize))
        {
            if (deadline.Elapsed.TotalSeconds > AiTotalDeadlineSeconds)
            {
                warnings.Add($"AI 解析超过 {AiTotalDeadlineSeconds} 秒，剩余用例按「仅基础信息」导入");
                break;
            }

            var items = batch.Select(x => new ImportCaseItemDto(
                x.Row.CaseCode, x.Row.Scenario, x.Module,
                Truncate(x.Row.SourceSteps, 1500), Truncate(x.Row.Expected, 1000))).ToList();

            try
            {
                var result = await _aiClient.ImportCaseStepsAsync(baseUrl, items, ct);
                foreach (var item in items)
                {
                    if (!result.TryGetValue(item.CaseCode, out var steps) || steps.Count == 0)
                        continue;
                    aiSteps[item.CaseCode] = steps;
                    aiParsedCodes.Add(item.CaseCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI 步骤解析失败，本批 {Count} 条用例按基础信息导入", items.Count);
                warnings.Add($"有 {items.Count} 条用例的 AI 步骤解析失败（{ex.Message}），已按基础信息导入");
            }
        }
    }

    private static List<TestStep> BuildSteps(List<GeneratedStepDto>? generated, string? baseUrl)
    {
        if (generated is null || generated.Count == 0)
            return new List<TestStep>();

        return generated
            .OrderBy(s => s.StepOrder)
            .Select((s, index) => new TestStep
            {
                StepOrder = index,
                ActionType = s.ActionType,
                Config = NormalizeConfig(s.Config, s.ActionType, baseUrl),
                AIElementDescription = s.AIElementDescription,
            })
            .ToList();
    }

    /// <summary>导入时补全配置：Navigate 缺 URL 时用环境地址兜底，其它动作不携带 URL。</summary>
    private static StepConfig NormalizeConfig(StepConfig config, ActionType actionType, string? baseUrl)
    {
        var clone = new StepConfig
        {
            Url = actionType == ActionType.Navigate ? config.Url : null,
            Method = config.Method,
            Endpoint = config.Endpoint,
            Headers = config.Headers,
            Body = config.Body,
            Value = config.Value,
            Selector = config.Selector is null ? null : new SelectorConfig
            {
                Type = config.Selector.Type,
                Value = config.Selector.Value,
                Description = config.Selector.Description,
            },
        };

        // 选择器只有描述没有值 → 交给运行时 AI 定位
        if (clone.Selector is not null &&
            string.IsNullOrWhiteSpace(clone.Selector.Value) &&
            !string.IsNullOrWhiteSpace(clone.Selector.Description))
        {
            clone.Selector.Type = "ai";
        }
        // Navigate 的相对 URL 执行时按环境 BaseUrl 拼接；完全缺失时用根路径兜底
        if (actionType == ActionType.Navigate &&
            string.IsNullOrWhiteSpace(clone.Url) &&
            !string.IsNullOrWhiteSpace(baseUrl))
        {
            clone.Url = "/";
        }
        return clone;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
