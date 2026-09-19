using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestCases;

public record ReportFile(byte[] Content, string FileName);

/// <summary>
/// 测试报告导出（对齐 MES_测试报告_v1.0.xlsx 结构）：
/// 测试概览 + 每模块明细 sheet（截图列是可点击的内部链接）+ 「截图证据」sheet（大图逐张标注）+ 缺陷报告。
///
/// 截图展示设计：明细行里塞缩略图会把行高撑到 100+ 且看不清，改为
/// 「明细行超链接 → 跳到截图证据 sheet 的对应大图」——明细紧凑，大图看得清。
/// 每个场景最多 3 张，选取按信息量：失败/错误步骤的截图最优先（问题现场），
/// 名额有剩时取执行末尾的终态截图（很多用例前几步是登录流程，按步骤序取前几张全是登录页）。
/// 整份报告上限 60 张防体积失控。
/// 截图经 IArtifactStore 读取（本地磁盘与 MinIO 都能出报告），不再拼本地文件路径。
/// </summary>
public class TestCaseReportService
{
    /// <summary>每个测试场景（用例行）最多展示的截图数。</summary>
    private const int MaxScreenshotsPerCase = 3;

    /// <summary>整份报告最多嵌入的截图数（防体积失控：一张 PNG 约 100-300KB）。</summary>
    private const int MaxScreenshotsTotal = 60;

    /// <summary>截图证据 sheet 的名字（明细表超链接跳转目标）。</summary>
    private const string ScreenshotsSheetName = "截图证据";

    /// <summary>截图证据 sheet 里单张大图的显示尺寸（px）。</summary>
    private const int ShotWidth = 660;
    private const int ShotHeight = 400;

    /// <summary>大图锚定后纵向占用的行数（默认行高 15pt≈20px；400px≈20 行，留 1 行间隔）。</summary>
    private const int ShotRowsPerImage = 21;

    private readonly TestDbContext _db;
    private readonly IArtifactStore _store;
    private readonly ILogger<TestCaseReportService> _logger;

    public TestCaseReportService(TestDbContext db, IArtifactStore store, IConfiguration configuration,
        IHostEnvironment environment, ILogger<TestCaseReportService> logger)
    {
        _db = db;
        _store = store;
        _logger = logger;
    }

    private sealed record ShotInfo(string Url, int StepOrder, ExecutionStatus Status);

    private sealed record ReportRow(
        string? CaseCode, string Name, string? Priority,
        string? SourceSteps, string? Expected,
        ExecutionStatus? Status, int? DurationMs, string? Error,
        string? Diagnosis, string? SuggestedFix,
        List<ShotInfo> Screenshots,
        DateTime? ExecutedAt = null, string? EnvironmentName = null);

    private sealed record ReportModule(string Name, List<ReportRow> Rows);

    // ---------------------------------------------------------------- 单次执行
    public async Task<ReportFile> GenerateForExecutionAsync(Guid executionId, CancellationToken ct)
    {
        var execution = await _db.Executions.AsNoTracking()
            .Include(e => e.TestCase).ThenInclude(t => t!.Steps)
            .Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct)
            ?? throw new InvalidOperationException("执行记录不存在");

        var projectName = execution.TestCase is null
            ? "未知项目"
            : await _db.Projects.AsNoTracking()
                .Where(p => p.Id == execution.TestCase.ProjectId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(ct) ?? "未知项目";

        var row = new ReportRow(
            execution.TestCase?.CaseCode,
            execution.TestCase?.Name ?? "(用例已删除)",
            execution.TestCase?.Priority,
            TestCaseStepDescriber.ResolveSourceSteps(execution.TestCase),
            TestCaseStepDescriber.ResolveExpectedResult(execution.TestCase),
            execution.Status,
            execution.DurationMs,
            FirstError(execution.Results),
            execution.AIDiagnosis,
            execution.AISuggestedFix,
            CollectScreenshots(execution));

        var moduleName = string.IsNullOrWhiteSpace(execution.TestCase?.Module) ? "未分模块" : execution.TestCase!.Module!;
        var modules = new List<ReportModule> { new(moduleName, new List<ReportRow> { row }) };

        var title = $"{projectName} — 测试报告";
        var subtitle = $"用例：{row.Name}｜执行时间：{(execution.StartedAt ?? execution.CreatedAt).ToLocalTime():yyyy-MM-dd HH:mm:ss}｜环境：{execution.EnvironmentSnapshot?.Name ?? "-"}";
        var defects = execution.TestCaseId is null
            ? new List<Defect>()
            : await LoadDefectsAsync([execution.TestCaseId.Value], ct);
        var content = await BuildWorkbook(title, subtitle, modules, defects);
        return new ReportFile(content, $"测试报告_{Sanitize(row.CaseCode ?? row.Name)}_{DateTime.Now:yyyyMMddHHmm}.xlsx");
    }

    // -------------------------------------------------------- 选中执行记录报告
    /// <summary>
    /// 为选中的若干条执行记录生成报告：每条记录一行，按用例所属模块分表。
    /// </summary>
    public async Task<ReportFile> GenerateForExecutionsAsync(
        IReadOnlyList<Guid> executionIds, CancellationToken ct)
    {
        var ids = executionIds.Distinct().ToList();
        var executions = await _db.Executions.AsNoTracking()
            .Include(e => e.Results)
            .Include(e => e.Environment)
            .Include(e => e.TestCase).ThenInclude(t => t!.Steps)
            .Where(e => ids.Contains(e.Id))
            .OrderByDescending(e => e.StartedAt ?? e.CreatedAt)
            .ToListAsync(ct);

        if (executions.Count == 0)
            throw new InvalidOperationException("未找到可导出的执行记录");

        var projectIds = executions
            .Where(e => e.TestCase is not null)
            .Select(e => e.TestCase!.ProjectId)
            .Distinct()
            .ToList();
        var projectNames = await _db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => p.Name)
            .ToListAsync(ct);
        var projectTitle = projectNames.Count == 1 ? projectNames[0] : $"{projectNames.Count} 个项目";

        var entries = executions.Select(execution =>
        {
            var testCase = execution.TestCase;
            var row = new ReportRow(
                testCase?.CaseCode,
                testCase?.Name ?? "(用例已删除)",
                testCase?.Priority,
                TestCaseStepDescriber.ResolveSourceSteps(testCase),
                TestCaseStepDescriber.ResolveExpectedResult(testCase),
                execution.Status,
                execution.DurationMs,
                FirstError(execution.Results),
                execution.AIDiagnosis,
                execution.AISuggestedFix,
                CollectScreenshots(execution),
                execution.StartedAt ?? execution.CreatedAt,
                execution.EnvironmentSnapshot?.Name ?? execution.Environment?.Name);
            var module = string.IsNullOrWhiteSpace(testCase?.Module) ? "未分模块" : testCase!.Module!;
            return (Module: module, Row: row);
        }).ToList();

        var modules = entries
            .GroupBy(x => x.Module)
            .Select(g => new ReportModule(g.Key, g.Select(x => x.Row).ToList()))
            .OrderBy(m => m.Name)
            .ToList();

        var subtitle = $"导出范围：选中的 {executions.Count} 条执行记录｜生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}";
        var defectCaseIds = executions
            .Where(e => e.TestCaseId.HasValue)
            .Select(e => e.TestCaseId!.Value)
            .Distinct()
            .ToList();
        var defects = await LoadDefectsAsync(defectCaseIds, ct);
        var content = await BuildWorkbook($"{projectTitle} — 测试报告", subtitle, modules, defects);
        return new ReportFile(content, $"测试报告_选中{executions.Count}条执行_{DateTime.Now:yyyyMMddHHmm}.xlsx");
    }

    // ------------------------------------------------------------ 项目汇总报告
    /// <summary>
    /// 项目汇总报告。传入 <paramref name="planId"/> 时只统计该测试计划范围内的用例，
    /// 用于「不想导出全项目、只关心某个验收批次」的场景。
    /// 注意这只是**缩小用例集合**，报告结构不变（仍是概览 + 模块明细 + 缺陷报告）；
    /// 计划维度的达标判定与轮次趋势在计划验收报告里，两者刻意不混。
    /// </summary>
    public async Task<ReportFile> GenerateForProjectAsync(
        Guid projectId, DateTime? from, DateTime? to, Guid? planId, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException("项目不存在");

        // 计划范围快照：取计划当前的范围（TestPlanItems），而不是历史轮次的快照——
        // 用户点"按计划导出"时，想看到的通常是"这个计划现在管着哪些用例"。
        TestPlan? plan = null;
        Guid[]? scopeCaseIds = null;
        if (planId.HasValue)
        {
            plan = await _db.TestPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId.Value, ct)
                ?? throw new InvalidOperationException("测试计划不存在");
            // 计划跨项目取范围会导出别的项目的用例，属于调用方传错参数，明确拒绝
            if (plan.ProjectId != projectId)
                throw new InvalidOperationException("该测试计划不属于当前项目");

            scopeCaseIds = await _db.TestPlanItems.AsNoTracking()
                .Where(i => i.PlanId == plan.Id)
                .Select(i => i.TestCaseId)
                .ToArrayAsync(ct);
        }

        var casesQuery = _db.TestCases.AsNoTracking()
            .Include(t => t.Steps)
            .Where(t => t.ProjectId == projectId);
        if (scopeCaseIds is not null)
            casesQuery = casesQuery.Where(t => scopeCaseIds.Contains(t.Id));

        var cases = await casesQuery
            .OrderBy(t => t.Module).ThenBy(t => t.CaseCode).ThenBy(t => t.Name)
            .ToListAsync(ct);

        var executionsQuery = _db.Executions.AsNoTracking()
            .Include(e => e.Results)
            .Where(e => e.TestCaseId != null && e.TestCase!.ProjectId == projectId);
        // 执行记录同样收敛到范围内，否则会把范围外用例的执行全量拉进内存再逐条丢弃
        if (scopeCaseIds is not null)
            executionsQuery = executionsQuery.Where(e => scopeCaseIds.Contains(e.TestCaseId!.Value));
        // 查询参数绑定的 DateTime 其 Kind 为 Unspecified，Npgsql 写入 timestamptz 必须是 UTC；
        // 前端传入的是本地日期，这里按本地时间解释后再转 UTC
        if (from.HasValue)
        {
            var fromUtc = ToUtc(from.Value);
            executionsQuery = executionsQuery.Where(e => e.CreatedAt >= fromUtc);
        }
        if (to.HasValue)
        {
            // to 视为「当天结束」，便于前端按日期选择
            var toUtc = ToUtc(to.Value.Date.AddDays(1));
            executionsQuery = executionsQuery.Where(e => e.CreatedAt < toUtc);
        }

        var executions = await executionsQuery
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        // 每个用例取区间内最新一次执行
        var latestByCase = executions
            .GroupBy(e => e.TestCaseId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<ReportRow>();
        foreach (var testCase in cases)
        {
            latestByCase.TryGetValue(testCase.Id, out var execution);
            rows.Add(new ReportRow(
                testCase.CaseCode,
                testCase.Name,
                testCase.Priority,
                TestCaseStepDescriber.ResolveSourceSteps(testCase),
                TestCaseStepDescriber.ResolveExpectedResult(testCase),
                execution?.Status,
                execution?.DurationMs,
                execution is null ? null : FirstError(execution.Results),
                execution?.AIDiagnosis,
                execution?.AISuggestedFix,
                execution is null ? new List<ShotInfo>() : CollectScreenshots(execution)));
        }

        var modules = rows
            .GroupBy(r => cases.First(c => c.Name == r.Name && c.CaseCode == r.CaseCode).Module ?? "未分模块")
            .Select(g => new ReportModule(g.Key, g.ToList()))
            .OrderBy(m => m.Name)
            .ToList();

        var range = from.HasValue || to.HasValue
            ? $"统计区间：{(from?.ToString("yyyy-MM-dd") ?? "最早")} ~ {(to?.ToString("yyyy-MM-dd") ?? "至今")}"
            : "统计区间：全部";
        var defects = await LoadDefectsAsync(cases.Select(c => c.Id).ToList(), ct);
        // 副标题必须写明「这是计划范围内的报告」：两份文件名相同的报告摆在面前时，
        // 只有这里能看出这份是缩过范围的，否则很容易被当成全项目报告误读
        var scope = plan is null
            ? string.Empty
            : $"｜测试计划：{plan.Name}（范围内用例 {cases.Count} 条）";
        var content = await BuildWorkbook($"{project.Name} — 测试报告",
            $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}｜{range}{scope}", modules, defects);
        var planSuffix = plan is null ? string.Empty : $"_{Sanitize(plan.Name)}";
        return new ReportFile(content,
            $"测试报告_{Sanitize(project.Name)}{planSuffix}_{DateTime.Now:yyyyMMddHHmm}.xlsx");
    }

    // ------------------------------------------------------------------ 生成

    /// <summary>
    /// 报告范围内的真实缺陷：与报告用例存在关联（DefectCase 或首发现用例）的缺陷记录。
    /// 有真实缺陷时「缺陷报告」sheet 用真实数据渲染；没有时退回原来的失败用例清单。
    /// </summary>
    private async Task<List<Defect>> LoadDefectsAsync(IReadOnlyCollection<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return new List<Defect>();
        return await _db.Defects.AsNoTracking()
            .Include(d => d.AssignedTo)
            .Where(d => _db.DefectCases.Any(dc => dc.DefectId == d.Id && caseIds.Contains(dc.TestCaseId))
                        || (d.FoundInTestCaseId != null && caseIds.Contains(d.FoundInTestCaseId.Value)))
            .OrderBy(d => d.Status)
            .ThenByDescending(d => d.Severity)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    private async Task<byte[]> BuildWorkbook(string title, string subtitle, List<ReportModule> modules, List<Defect> defects)
    {
        using var workbook = new XLWorkbook();
        BuildOverviewSheet(workbook, title, subtitle, modules);

        // 先渲染截图证据 sheet（回填每个用例块的锚点行号），模块明细里的链接才能跳对位置
        var entries = CollectShotEntries(modules);
        var shotsWs = await BuildScreenshotsSheet(workbook, entries);
        foreach (var module in modules)
            BuildModuleSheet(workbook, module, entries, shotsWs);
        BuildDefectSheet(workbook, modules, defects);
        // 截图证据作为附录排在最后
        shotsWs.Position = workbook.Worksheets.Count;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>单个用例块的截图条目（截图证据 sheet 用）：模块名 + 行 + 截图（最多 3 张）。AnchorRow 渲染时回填。</summary>
    private sealed class ShotEntry
    {
        public ShotEntry(string module, ReportRow row, IReadOnlyList<ShotInfo> shots)
        {
            Module = module;
            Row = row;
            Shots = shots;
        }

        public string Module { get; }
        public ReportRow Row { get; }
        public IReadOnlyList<ShotInfo> Shots { get; }
        public int AnchorRow { get; set; }
    }

    private static void BuildOverviewSheet(XLWorkbook workbook, string title, string subtitle, List<ReportModule> modules)
    {
        const int cols = 7;
        var ws = workbook.Worksheets.Add("测试概览");
        ws.ShowGridLines = false;

        // ---- 标题区
        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, cols).Merge();
        var titleCell = ws.Cell(1, 1);
        titleCell.Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(ExcelStyling.Primary);
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 34;

        ws.Cell(2, 1).Value = subtitle;
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(2, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(2).Height = 18;
        ws.Row(3).Height = 6;

        // ---- 表头
        var headers = new[] { "模块名称", "用例数", "已测", "通过", "失败", "通过率", "高优先级" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(4, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(4, 1, 4, cols));
        ws.Row(4).Height = 24;

        // ---- 数据行
        var row = 5;
        var totalCases = 0;
        var totalTested = 0;
        var totalPassed = 0;
        var totalFailed = 0;
        var totalHigh = 0;
        var zebra = false;

        foreach (var module in modules)
        {
            var tested = module.Rows.Count(r => r.Status.HasValue);
            var passed = module.Rows.Count(r => r.Status == ExecutionStatus.Passed);
            var failed = module.Rows.Count(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error);
            var high = module.Rows.Count(r => r.Priority is "P0" or "P1" or "高");

            totalCases += module.Rows.Count;
            totalTested += tested;
            totalPassed += passed;
            totalFailed += failed;
            totalHigh += high;

            ws.Cell(row, 1).Value = module.Name;
            ws.Cell(row, 2).Value = module.Rows.Count;
            ws.Cell(row, 3).Value = tested;
            ws.Cell(row, 4).Value = passed;
            ws.Cell(row, 5).Value = failed;

            var rateCell = ws.Cell(row, 6);
            if (tested == 0)
            {
                rateCell.Value = "-";
                rateCell.Style.Font.SetFontColor(ExcelStyling.Muted);
            }
            else
            {
                var rate = passed * 100.0 / tested;
                rateCell.Value = $"{rate:0.#}%";
                rateCell.Style.Font.SetFontColor(rate >= 100 ? ExcelStyling.Success : rate >= 80 ? ExcelStyling.Warn : ExcelStyling.Danger);
                rateCell.Style.Font.SetBold();
            }
            ws.Cell(row, 7).Value = high;

            var line = ws.Range(row, 1, row, cols);
            ExcelStyling.ApplyRowStyle(line, zebra);
            if (failed > 0)
                ws.Cell(row, 5).Style.Font.SetFontColor(ExcelStyling.Danger).Font.SetBold();
            else if (tested > 0)
                ws.Cell(row, 5).Style.Font.SetFontColor(ExcelStyling.Muted);
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Row(row).Height = 22;
            zebra = !zebra;
            row++;
        }

        // ---- 汇总行
        ws.Cell(row, 1).Value = "汇总";
        ws.Cell(row, 2).Value = totalCases;
        ws.Cell(row, 3).Value = totalTested;
        ws.Cell(row, 4).Value = totalPassed;
        ws.Cell(row, 5).Value = totalFailed;
        ws.Cell(row, 6).Value = totalTested == 0 ? "-" : $"{totalPassed * 100.0 / totalTested:0.#}%";
        ws.Cell(row, 7).Value = totalHigh;
        var summaryRange = ws.Range(row, 1, row, cols);
        summaryRange.Style.Font.SetBold().Font.SetFontSize(10);
        summaryRange.Style.Fill.SetBackgroundColor(ExcelStyling.SummaryBg);
        ExcelStyling.ApplyTableBorders(summaryRange);
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        if (totalFailed > 0)
            ws.Cell(row, 5).Style.Font.SetFontColor(ExcelStyling.Danger);
        ws.Row(row).Height = 24;
        var summaryRow = row;

        // ---- 底部摘要
        var noteRow = summaryRow + 2;
        ws.Cell(noteRow, 1).Value = totalTested == 0
            ? $"共 {totalCases} 条用例，尚未执行"
            : $"共 {totalCases} 条用例，已执行 {totalTested} 条，通过 {totalPassed} 条，失败/错误 {totalFailed} 条，" +
              $"通过率 {totalPassed * 100.0 / totalTested:0.#}%";
        ws.Range(noteRow, 1, noteRow, cols).Merge();
        ws.Cell(noteRow, 1).Style.Font.SetBold().Font.SetFontSize(10).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(noteRow, 1).Style.Fill.SetBackgroundColor(ExcelStyling.SummaryBg);
        ws.Cell(noteRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(noteRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ExcelStyling.ApplyTableBorders(ws.Range(noteRow, 1, noteRow, cols));
        ws.Row(noteRow).Height = 26;

        // ---- 布局与打印
        ExcelStyling.NormalizeFont(ws);
        ws.Column(1).Width = 30;
        for (var c = 2; c <= cols; c++)
            ws.Column(c).Width = 11;
        ws.SheetView.FreezeRows(4);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(4, 4);
        ws.PageSetup.PrintAreas.Add(1, 1, noteRow, cols);
    }

    private void BuildModuleSheet(XLWorkbook workbook, ReportModule module, List<ShotEntry> shotEntries, IXLWorksheet shotsWs)
    {
        const int cols = 8;
        var ws = workbook.Worksheets.Add(SafeSheetName(module.Name));
        ws.ShowGridLines = false;

        // ---- 标题：左侧模块名 + 右侧模块统计
        ws.Cell(1, 1).Value = module.Name;
        ws.Range(1, 1, 1, 6).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var passed = module.Rows.Count(r => r.Status == ExecutionStatus.Passed);
        var failed = module.Rows.Count(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error);
        var tested = module.Rows.Count(r => r.Status.HasValue);
        ws.Cell(1, 7).Value = $"共 {module.Rows.Count} 条｜已测 {tested}｜通过 {passed}｜失败 {failed}";
        ws.Range(1, 7, 1, cols).Merge();
        ws.Cell(1, 7).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
        ws.Cell(1, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(1, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 28;

        // ---- 表头
        var headers = new[] { "用例编号", "测试场景", "操作步骤", "预期结果", "优先级", "自动化结果", "备注", "截图" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(2, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(2, 1, 2, cols));
        ws.Row(2).Height = 26;

        // ---- 数据行
        var row = 3;
        var zebra = false;
        foreach (var item in module.Rows)
        {
            ws.Cell(row, 1).Value = item.CaseCode ?? "-";
            ws.Cell(row, 2).Value = item.Name;
            ws.Cell(row, 3).Value = item.SourceSteps ?? string.Empty;
            ws.Cell(row, 4).Value = item.Expected ?? string.Empty;
            ws.Cell(row, 5).Value = item.Priority ?? "-";

            var statusCell = ws.Cell(row, 6);
            statusCell.Value = ExecutionStatusText(item.Status);
            statusCell.Style.Font.SetFontColor(StatusColor(item.Status)).Font.SetBold();
            if (item.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                statusCell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FBE9EA"));
            else if (item.Status == ExecutionStatus.Passed)
                statusCell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8F5EA"));

            ws.Cell(row, 7).Value = BuildNote(item);
            ws.Cell(row, 7).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);

            var line = ws.Range(row, 1, row, cols);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, 2).Style.Font.SetBold();
            ws.Range(row, 3, row, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Range(row, 3, row, 4).Style.Alignment.WrapText = true;
            ws.Range(row, 7, row, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Cell(row, 7).Style.Alignment.WrapText = true;

            // 失败行整行淡红底，便于快速定位
            if (item.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            {
                ws.Range(row, 1, row, 5).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FDF6F6"));
                ws.Cell(row, 8).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FDF6F6"));
            }

            // 截图列：不放缩略图（会撑高行且看不清），放跳转到「截图证据」sheet 的链接
            WriteScreenshotLink(ws, row, item, shotEntries, shotsWs);
            ws.Row(row).Height = EstimateRowHeight(item);
            zebra = !zebra;
            row++;
        }

        // ---- 布局与打印
        ExcelStyling.NormalizeFont(ws);
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 24;
        ws.Column(3).Width = 40;
        ws.Column(4).Width = 34;
        ws.Column(5).Width = 9;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 30;
        ws.Column(8).Width = 14;
        ws.SheetView.FreezeRows(2);
        if (row > 3)
            ws.Range(2, 1, row - 1, cols).SetAutoFilter();
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(1, 2);
        ws.PageSetup.PrintAreas.Add(1, 1, Math.Max(row - 1, 3), cols);
    }

    /// <summary>按文本量估算行高，避免内容被截断（明细行不再嵌图，行高只由文本决定）。</summary>
    private static double EstimateRowHeight(ReportRow item)
    {
        // 中文约按 2 个字符宽度计，列宽单位≈1 个数字字符
        var lines = new[]
        {
            WrappedLines(item.SourceSteps, 40),
            WrappedLines(item.Expected, 34),
            WrappedLines(BuildNote(item), 30),
        }.Max();
        return Math.Clamp(lines * 15 + 8, 26, 200);
    }

    private static int WrappedLines(string? text, double columnWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;
        var perLine = Math.Max(8, (int)(columnWidth / 2));
        var normalized = text.Replace("\r\n", "\n");
        var lines = 0;
        foreach (var part in normalized.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(part.Length / (double)perLine));
        return Math.Max(1, lines);
    }

    /// <summary>
    /// 明细行「截图」列：写「▶ 查看 N 张截图」内部链接，跳到截图证据 sheet 里该用例的大图块。
    /// 没有截图的行留空。链接样式（蓝字下划线）便于发现可点。
    /// </summary>
    private static void WriteScreenshotLink(IXLWorksheet ws, int row, ReportRow item,
        List<ShotEntry> shotEntries, IXLWorksheet shotsWs)
    {
        var entry = shotEntries.FirstOrDefault(e => ReferenceEquals(e.Row, item));
        if (entry is null)
            return;
        var cell = ws.Cell(row, 8);
        cell.Value = $"▶ 查看 {entry.Shots.Count} 张截图";
        // ClosedXML 0.105 API：XLHyperlink(IXLCell) 是 workbook 内跳转，SetHyperlink 挂到单元格
        cell.SetHyperlink(new XLHyperlink(shotsWs.Cell(entry.AnchorRow, 2)));
        cell.Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Primary).Font.SetUnderline();
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>
    /// 「截图证据」sheet：每张截图以 660×400 大图呈现，块头标注模块/用例/步骤/状态。
    /// 顺序：失败用例的截图优先，其余按模块顺序。每用例最多 3 张，全表上限 60 张。
    /// </summary>
    private async Task<IXLWorksheet> BuildScreenshotsSheet(XLWorkbook workbook, List<ShotEntry> entries)
    {
        var ws = workbook.Worksheets.Add(ScreenshotsSheetName);
        ws.ShowGridLines = false;

        ws.Column(1).Width = 3;
        ws.Column(2).Width = 100;

        ws.Cell(1, 2).Value = $"截图证据 — 共 {entries.Sum(e => e.Shots.Count)} 张（每场景最多 {MaxScreenshotsPerCase} 张：失败步骤优先，成功用例取执行末尾终态）";
        ws.Cell(1, 2).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(ExcelStyling.Primary);
        ws.Row(1).Height = 30;

        var row = 3;
        var embedded = 0;
        foreach (var entry in entries)
        {
            entry.AnchorRow = row; // 明细表的超链接锚点指向用例块第一张图的标题行
            foreach (var shot in entry.Shots)
            {
                if (embedded >= MaxScreenshotsTotal)
                {
                    ws.Cell(row, 2).Value = $"…截图已达上限 {MaxScreenshotsTotal} 张，其余未嵌入（可在执行详情页逐条查看）";
                    ws.Cell(row, 2).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
                    row += 2;
                    break;
                }

                var statusText = ExecutionStatusText(shot.Status);
                ws.Cell(row, 2).Value =
                    $"{entry.Row.Name}｜{StepLabel(shot.StepOrder)}｜{statusText}" +
                    (shot.Status is ExecutionStatus.Failed or ExecutionStatus.Error ? "　⚠ 失败步骤" : string.Empty);
                ws.Cell(row, 2).Style.Font.SetBold().Font.SetFontSize(10).Font.SetFontColor(
                    shot.Status is ExecutionStatus.Failed or ExecutionStatus.Error ? ExcelStyling.Danger : ExcelStyling.Primary);
                ws.Row(row).Height = 22;
                row++;

                if (await EmbedPicture(ws, row, shot.Url))
                {
                    embedded++;
                    // 图片锚定后向上浮动覆盖约 ShotRowsPerImage 行，把这些行的行高压到默认以下，
                    // 让图片与标注紧凑衔接
                    for (var i = 0; i < ShotRowsPerImage - 1; i++)
                        ws.Row(row + i).Height = 15;
                    row += ShotRowsPerImage - 1;
                }
                else
                {
                    ws.Cell(row, 2).Value = "（截图已清理或不可读）";
                    ws.Cell(row, 2).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
                    row += 1;
                }
                row++;
            }
            row += 2; // 用例块之间的间隔
        }

        if (entries.Count == 0)
        {
            ws.Cell(3, 2).Value = "本次范围内没有可展示的截图（用例尚未执行或截图已按保留策略清理）";
            ws.Cell(3, 2).Style.Font.SetFontSize(10).Font.SetFontColor(ExcelStyling.Muted);
        }

        ExcelStyling.NormalizeFont(ws);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        return ws;
    }

    /// <summary>
    /// 汇总所有有截图的用例块（每用例取最多 3 张，失败步骤优先）。
    /// AnchorRow 在 BuildScreenshotsSheet 渲染时回填，供明细行超链接跳转。
    /// </summary>
    private static List<ShotEntry> CollectShotEntries(List<ReportModule> modules)
    {
        var entries = new List<ShotEntry>();
        // 失败/错误用例的截图块排最前（看报告的人最关心它们）
        foreach (var module in modules)
        {
            entries.AddRange(module.Rows
                .Where(r => r.Screenshots.Count > 0 && r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                .Select(r => new ShotEntry(module.Name, r, r.Screenshots.Take(MaxScreenshotsPerCase).ToList())));
        }
        foreach (var module in modules)
        {
            entries.AddRange(module.Rows
                .Where(r => r.Screenshots.Count > 0 && !(r.Status is ExecutionStatus.Failed or ExecutionStatus.Error))
                .Select(r => new ShotEntry(module.Name, r, r.Screenshots.Take(MaxScreenshotsPerCase).ToList())));
        }
        return entries;
    }

    /// <summary>从产物存储读取截图并锚定为指定行的大图；读取/插入失败返回 false。</summary>
    private async Task<bool> EmbedPicture(IXLWorksheet ws, int anchorRow, string url)
    {
        try
        {
            var key = ScreenshotStorage.KeyOf(url);
            if (key is null)
                return false;
            var bytes = await _store.ReadAsync(key, ct: default);
            if (bytes is null || bytes.Length == 0)
                return false;
            using var ms = new MemoryStream(bytes);
            var picture = ws.AddPicture(ms, XLPictureFormat.Png);
            // 保持 Move（oneCellAnchor：起点 + 固定宽高）。
            // 之前试过切 MoveAndSize（twoCellAnchor）：ClosedXML 保存时把 to 端点换算到 (0,0)，
            // 产生「终点在起点左上方」的无效锚点，Excel/WPS 会丢弃全部图片（报告截图全不显示）。
            picture.Placement = XLPicturePlacement.Move;
            picture.Width = ShotWidth;
            picture.Height = ShotHeight;
            picture.MoveTo(ws.Cell(anchorRow, 2));
            return true;
        }
        catch (Exception ex)
        {
            // 单张图片插入失败不影响整份报告，但要留下日志便于排查
            _logger.LogWarning(ex, "报告嵌入截图失败：{Url}", url);
            return false;
        }
    }

    private static string StepLabel(int stepOrder) => stepOrder < 0 ? "前置" : $"步骤 {stepOrder + 1}";

    private static string BuildNote(ReportRow item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Error))
            parts.Add($"错误：{item.Error}");
        if (item.ExecutedAt.HasValue)
            parts.Add($"执行 {item.ExecutedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrWhiteSpace(item.EnvironmentName))
            parts.Add($"环境 {item.EnvironmentName}");
        if (item.DurationMs is > 0)
            parts.Add($"耗时 {item.DurationMs / 1000.0:0.#}s");
        if (item.Screenshots.Count > 0)
            parts.Add($"截图 {item.Screenshots.Count} 张");
        return parts.Count == 0 ? string.Empty : string.Join("；", parts);
    }

    private static void BuildDefectSheet(XLWorkbook workbook, List<ReportModule> modules, List<Defect> defects)
    {
        var ws = workbook.Worksheets.Add("缺陷报告");
        ws.ShowGridLines = false;

        // 有真实缺陷记录时优先展示真实数据（缺陷模块 P2）；否则退回旧口径——
        // 把失败用例清单当作缺陷线索展示（BUG-xxx 编号是人造的，仅供阅读）
        if (defects.Count > 0)
        {
            BuildRealDefectTable(ws, defects);
            ExcelStyling.NormalizeFont(ws);
            return;
        }

        const int cols = 7;

        var synthesized = modules
            .SelectMany(m => m.Rows.Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                .Select(r => (Module: m.Name, Row: r)))
            .ToList();

        // ---- 标题
        ws.Cell(1, 1).Value = synthesized.Count == 0 ? "缺陷报告 — 未发现失败用例" : $"缺陷报告 — 共 {synthesized.Count} 项";
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 28;

        ws.Cell(2, 1).Value = "根因分析与修复建议由 AI 诊断生成（执行失败后自动触发）";
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
        ws.Row(2).Height = 18;

        // ---- 表头
        var headers = new[] { "缺陷编号", "模块", "用例编号", "问题描述", "根因分析", "修复建议", "状态" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(3, 1, 3, cols));
        ws.Row(3).Height = 26;

        var row = 4;
        var index = 1;
        var zebra = false;
        foreach (var (module, item) in synthesized)
        {
            ws.Cell(row, 1).Value = $"BUG-{index:D3}";
            ws.Cell(row, 2).Value = module;
            ws.Cell(row, 3).Value = item.CaseCode ?? "-";
            ws.Cell(row, 4).Value = $"{item.Name}：{item.Error ?? "执行失败"}";
            ws.Cell(row, 5).Value = item.Diagnosis ?? "（未生成 AI 诊断）";
            ws.Cell(row, 6).Value = item.SuggestedFix ?? string.Empty;
            ws.Cell(row, 7).Value = "待确认";

            var line = ws.Range(row, 1, row, cols);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(ExcelStyling.Danger);
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, 3).Style.Font.SetFontSize(9);
            ws.Cell(row, 7).Style.Font.SetBold().Font.SetFontColor(ExcelStyling.Warn);
            ws.Range(row, 4, row, 6).Style.Alignment.WrapText = true;
            ws.Range(row, 4, row, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            if (string.IsNullOrWhiteSpace(item.Diagnosis))
                ws.Cell(row, 5).Style.Font.SetFontColor(ExcelStyling.Muted);
            ws.Row(row).Height = Math.Clamp(
                new[] { WrappedLines(ws.Cell(row, 4).GetString(), 40), WrappedLines(item.Diagnosis, 40), WrappedLines(item.SuggestedFix, 40) }
                    .Max() * 15 + 8, 28, 220);
            zebra = !zebra;
            row++;
        }

        if (synthesized.Count == 0)
        {
            ws.Cell(4, 1).Value = "本次范围内没有失败或错误用例 🎉";
            ws.Range(4, 1, 4, cols).Merge();
            ws.Cell(4, 1).Style.Font.SetFontColor(ExcelStyling.Success).Font.SetBold();
            ws.Cell(4, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8F5EA"));
        ws.Cell(4, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(4, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ExcelStyling.ApplyTableBorders(ws.Range(4, 1, 4, cols));
            ws.Row(4).Height = 30;
            row = 5;
        }

        // ---- 布局与打印
        ExcelStyling.NormalizeFont(ws);
        ws.Column(1).Width = 12;
        ws.Column(2).Width = 20;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 40;
        ws.Column(5).Width = 40;
        ws.Column(6).Width = 40;
        ws.Column(7).Width = 10;
        ws.SheetView.FreezeRows(3);
        if (synthesized.Count > 0)
            ws.Range(3, 1, row - 1, cols).SetAutoFilter();
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(3, 3);
        ws.PageSetup.PrintAreas.Add(1, 1, Math.Max(row - 1, 5), cols);
    }

    // ------------------------------ 真实缺陷表（缺陷模块 P2）

    private static void BuildRealDefectTable(IXLWorksheet ws, List<Defect> defects)
    {
        const int cols = 8;
        var openCount = defects.Count(d => d.Status is DefectStatus.New or DefectStatus.Assigned or DefectStatus.Fixed);

        ws.Cell(1, 1).Value = $"缺陷报告 — 共 {defects.Count} 项（未闭环 {openCount}）";
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14)
            .Font.SetFontColor(openCount > 0 ? ExcelStyling.Warn : ExcelStyling.Primary);
        ws.Row(1).Height = 28;

        ws.Cell(2, 1).Value = "数据来自平台缺陷管理模块；未闭环 = 新建 / 已指派 / 已修复待验证";
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
        ws.Row(2).Height = 16;

        var headers = new[] { "编号", "标题", "严重度", "状态", "负责人", "创建时间", "闭环时间", "流转备注" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(3, 1, 3, cols));
        ws.Row(3).Height = 24;

        var row = 3;
        var zebra = false;
        foreach (var defect in defects)
        {
            row++;
            ws.Cell(row, 1).Value = $"DEF-{defect.Id.ToString("N")[..8].ToUpper()}";
            ws.Cell(row, 2).Value = defect.Title;
            ws.Cell(row, 3).Value = SeverityText(defect.Severity);
            ws.Cell(row, 4).Value = StatusText(defect.Status);
            ws.Cell(row, 5).Value = defect.AssignedTo is null
                ? "—"
                : string.IsNullOrWhiteSpace(defect.AssignedTo.DisplayName)
                    ? defect.AssignedTo.Username
                    : defect.AssignedTo.DisplayName;
            ws.Cell(row, 6).Value = defect.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ws.Cell(row, 7).Value = defect.VerifiedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
            ws.Cell(row, 8).Value = defect.ResolutionNote ?? "—";

            var closed = defect.Status is DefectStatus.Verified or DefectStatus.Closed;
            var terminal = defect.Status is DefectStatus.Rejected or DefectStatus.Deferred;
            var line = ws.Range(row, 1, row, cols);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(
                terminal ? ExcelStyling.Muted : closed ? ExcelStyling.Success : ExcelStyling.Danger);
            ws.Cell(row, 4).Style.Font.SetFontColor(
                terminal ? ExcelStyling.Muted : closed ? ExcelStyling.Success : ExcelStyling.Danger);
            ws.Range(row, 2, row, 2).Style.Alignment.WrapText = true;
            ws.Range(row, 8, row, 8).Style.Alignment.WrapText = true;
            ws.Row(row).Height = Math.Clamp(WrappedLines(defect.Title, 44) * 15 + 8, 24, 120);
            zebra = !zebra;
        }

        if (defects.Count > 0)
            ws.Range(3, 1, row, cols).SetAutoFilter();
        ws.SheetView.FreezeRows(3);

        ws.Column(1).Width = 11;
        ws.Column(2).Width = 46;
        ws.Column(3).Width = 9;
        ws.Column(4).Width = 9;
        ws.Column(5).Width = 10;
        ws.Column(6).Width = 17;
        ws.Column(7).Width = 17;
        ws.Column(8).Width = 30;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(3, 3);
        ws.PageSetup.PrintAreas.Add(1, 1, Math.Max(row, 5), cols);
    }

    private static string SeverityText(DefectSeverity severity) => severity switch
    {
        DefectSeverity.Critical => "致命",
        DefectSeverity.Major => "严重",
        DefectSeverity.Normal => "一般",
        _ => "建议性",
    };

    private static string StatusText(DefectStatus status) => status switch
    {
        DefectStatus.New => "新建",
        DefectStatus.Assigned => "已指派",
        DefectStatus.Fixed => "已修复",
        DefectStatus.Verified => "已验证",
        DefectStatus.Closed => "已关闭",
        DefectStatus.Rejected => "已驳回",
        _ => "已挂起",
    };

    private static string ExecutionStatusText(ExecutionStatus? status) => status switch
    {
        ExecutionStatus.Passed => "通过",
        ExecutionStatus.Failed => "失败",
        ExecutionStatus.Error => "错误",
        ExecutionStatus.Skipped => "跳过",
        ExecutionStatus.Running => "执行中",
        ExecutionStatus.Pending => "待执行",
        _ => "未执行",
    };

    private static XLColor StatusColor(ExecutionStatus? status) => status switch
    {
        ExecutionStatus.Passed => ExcelStyling.Success,
        ExecutionStatus.Failed => ExcelStyling.Danger,
        ExecutionStatus.Error => ExcelStyling.Danger,
        ExecutionStatus.Running => ExcelStyling.Primary,
        _ => ExcelStyling.Muted,
    };

    /// <summary>统一全表字体（中文字体，避免默认 Calibri 显示不佳）。</summary>

    private static string? FirstError(IEnumerable<ExecutionResult> results)
    {
        var failed = results
            .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .OrderBy(r => r.StepOrder)
            .FirstOrDefault();
        if (failed is null)
            return null;
        var step = failed.StepOrder < 0 ? "前置" : $"步骤 {failed.StepOrder + 1}";
        return $"{step}：{failed.ErrorMessage ?? failed.Log ?? "失败"}";
    }

    /// <summary>
    /// 收集一次执行里各步骤的截图，并按信息量选取最多 <see cref="MaxScreenshotsPerCase"/> 张：
    /// 失败/错误步骤的截图最优先（问题现场）；名额有剩时从执行末尾往前补——
    /// 很多用例前几步是登录/导航过程（全是登录页），按步骤序取前 N 张看到的都是登录页，
    /// 而执行末尾的截图才是业务页面的最终状态，证明力最强。
    /// URL 经 IArtifactStore 读取（本地磁盘与 MinIO 都适用），不再拼本地文件路径。
    /// </summary>
    private List<ShotInfo> CollectScreenshots(global::AI.TestPlatform.Domain.Entities.Execution execution)
    {
        var all = new List<ShotInfo>();
        foreach (var result in execution.Results.OrderBy(r => r.StepOrder))
        {
            if (string.IsNullOrWhiteSpace(result.ScreenshotUrl))
                continue;
            all.Add(new ShotInfo(result.ScreenshotUrl, result.StepOrder, result.Status));
        }

        if (all.Count <= MaxScreenshotsPerCase)
            return all;

        var picked = all
            .Where(s => s.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .Take(MaxScreenshotsPerCase)
            .ToList();
        // 失败截图不足 3 张时，从末尾往前补终态截图（补进来的按步骤序正序展示）
        var tail = all
            .Where(s => s.Status is not (ExecutionStatus.Failed or ExecutionStatus.Error))
            .OrderByDescending(s => s.StepOrder)
            .Take(MaxScreenshotsPerCase - picked.Count)
            .OrderBy(s => s.StepOrder);
        picked.AddRange(tail);
        return picked;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "模块";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }
}
