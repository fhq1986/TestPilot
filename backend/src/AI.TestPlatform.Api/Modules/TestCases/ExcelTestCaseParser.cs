using ClosedXML.Excel;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>Excel 中解析出的一行用例（尚未落库）。</summary>
public record ParsedCaseRow(
    int RowNumber,
    string CaseCode,
    string Scenario,
    string SourceSteps,
    string Expected,
    string Priority,
    string Category,
    TestType Type);

/// <summary>一个模块（对应模板中的一个工作表）。</summary>
public record ParsedModule(string Name, string? Title, List<ParsedCaseRow> Rows);

public record ParsedWorkbook(List<ParsedModule> Modules, List<string> Warnings)
{
    public int TotalRows => Modules.Sum(m => m.Rows.Count);
}

/// <summary>
/// 测试用例 Excel 模板解析器（兼容 MES_测试用例_v1.0.xlsx 结构）：
/// 每个模块一个工作表，首行为模块标题、次行起为「用例编号/测试场景/操作步骤/预期结果/优先级/类别」表格。
/// 纯函数实现，便于单元测试。
/// </summary>
public static class ExcelTestCaseParser
{
    // 非用例工作表（概览/统计/缺陷等）直接跳过
    private static readonly string[] SkipSheetKeywords =
        { "概览", "执行记录", "缺陷", "统计", "说明", "索引", "目录" };

    private const int MaxHeaderScanRows = 6;

    public static ParsedWorkbook Parse(Stream stream, int maxRowsPerSheet = 1000)
    {
        var warnings = new List<string>();
        var modules = new List<ParsedModule>();

        using var workbook = new XLWorkbook(stream);
        foreach (var sheet in workbook.Worksheets)
        {
            var sheetName = sheet.Name.Trim();
            if (SkipSheetKeywords.Any(k => sheetName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;

            var used = sheet.RangeUsed();
            if (used is null)
                continue;

            var headerRow = FindHeaderRow(sheet, used);
            if (headerRow < 0)
            {
                warnings.Add($"工作表「{sheetName}」未识别到用例表头，已跳过");
                continue;
            }

            var columns = MapColumns(sheet, headerRow);
            if (columns.ScenarioCol <= 0)
            {
                warnings.Add($"工作表「{sheetName}」缺少「测试场景/用例名称」列，已跳过");
                continue;
            }

            var title = ReadTitle(sheet);
            var rows = new List<ParsedCaseRow>();
            for (var r = headerRow + 1; r <= used.LastRow().RowNumber(); r++)
            {
                var scenario = CellText(sheet, r, columns.ScenarioCol);
                var code = columns.CaseCodeCol > 0 ? CellText(sheet, r, columns.CaseCodeCol) : string.Empty;
                if (string.IsNullOrWhiteSpace(scenario) && string.IsNullOrWhiteSpace(code))
                    continue;
                if (string.IsNullOrWhiteSpace(scenario))
                {
                    warnings.Add($"「{sheetName}」第 {r} 行缺少测试场景，已跳过");
                    continue;
                }

                var sourceSteps = columns.StepsCol > 0 ? CellText(sheet, r, columns.StepsCol) : string.Empty;
                var expected = columns.ExpectedCol > 0 ? CellText(sheet, r, columns.ExpectedCol) : string.Empty;
                var priorityRaw = columns.PriorityCol > 0 ? CellText(sheet, r, columns.PriorityCol) : string.Empty;
                var category = columns.CategoryCol > 0 ? CellText(sheet, r, columns.CategoryCol) : string.Empty;

                rows.Add(new ParsedCaseRow(
                    r,
                    string.IsNullOrWhiteSpace(code) ? BuildFallbackCode(sheetName, rows.Count + 1) : code,
                    scenario,
                    sourceSteps,
                    expected,
                    MapPriority(priorityRaw),
                    category,
                    MapType(category, scenario, sourceSteps)));

                if (rows.Count >= maxRowsPerSheet)
                {
                    warnings.Add($"「{sheetName}」超过 {maxRowsPerSheet} 行，其余已忽略");
                    break;
                }
            }

            if (rows.Count > 0)
                modules.Add(new ParsedModule(sheetName, title, rows));
            else
                warnings.Add($"工作表「{sheetName}」没有可导入的用例行");
        }

        if (modules.Count == 0)
            throw new InvalidOperationException("未从文件中解析到任何测试用例，请确认使用模板格式（表头需包含「测试场景」等列）");

        return new ParsedWorkbook(modules, warnings);
    }

    private static int FindHeaderRow(IXLWorksheet sheet, IXLRange used)
    {
        var lastRow = Math.Min(used.LastRow().RowNumber(), used.FirstRow().RowNumber() + MaxHeaderScanRows);
        for (var r = used.FirstRow().RowNumber(); r <= lastRow; r++)
        {
            var cells = Enumerable.Range(used.FirstColumn().ColumnNumber(), used.LastColumn().ColumnNumber())
                .Select(c => Normalize(sheet.Cell(r, c).GetString()));
            if (cells.Any(c => c.Contains("测试场景") || c.Contains("用例名称")) &&
                cells.Any(c => c.Contains("用例编号") || c.Contains("预期") || c.Contains("步骤")))
                return r;
        }
        return -1;
    }

    private static (int CaseCodeCol, int ScenarioCol, int StepsCol, int ExpectedCol, int PriorityCol, int CategoryCol)
        MapColumns(IXLWorksheet sheet, int headerRow)
    {
        int code = 0, scenario = 0, steps = 0, expected = 0, priority = 0, category = 0;
        var lastCol = sheet.RangeUsed()!.LastColumn().ColumnNumber();
        for (var c = 1; c <= lastCol; c++)
        {
            var h = Normalize(sheet.Cell(headerRow, c).GetString());
            if (h.Length == 0)
                continue;
            if (code == 0 && (h.Contains("用例编号") || h.Contains("用例编码") || h == "编号" || h.Contains("casecode")))
                code = c;
            else if (scenario == 0 && (h.Contains("测试场景") || h.Contains("用例名称") || h.Contains("场景") || h == "名称"))
                scenario = c;
            else if (steps == 0 && (h.Contains("操作步骤") || h.Contains("前置条件") || h.Contains("测试步骤") || h.Contains("步骤")))
                steps = c;
            else if (expected == 0 && (h.Contains("预期结果") || h.Contains("期望结果") || h.Contains("预期")))
                expected = c;
            else if (priority == 0 && (h.Contains("优先级") || h.Contains("级别")))
                priority = c;
            else if (category == 0 && (h.Contains("类别") || h.Contains("测试类型") || h.Contains("类型")))
                category = c;
        }
        return (code, scenario, steps, expected, priority, category);
    }

    private static string? ReadTitle(IXLWorksheet sheet)
    {
        var first = sheet.Cell(1, 1).GetString().Trim();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static string CellText(IXLWorksheet sheet, int row, int col)
        => sheet.Cell(row, col).GetString().Trim();

    private static string BuildFallbackCode(string sheetName, int index)
    {
        var prefix = new string(sheetName.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "TC";
        return $"{prefix}-{index:D3}";
    }

    /// <summary>优先级映射：高/中/低 → P0/P1/P2，已符合 P0-P3 的原样保留。</summary>
    public static string MapPriority(string? raw)
    {
        var v = (raw ?? string.Empty).Trim();
        if (v.Length == 0)
            return "P2";
        var upper = v.ToUpperInvariant();
        if (upper is "P0" or "P1" or "P2" or "P3")
            return upper;
        if (upper.StartsWith("P") && int.TryParse(upper[1..], out var n) && n is >= 0 and <= 3)
            return $"P{n}";
        if (v.Contains("高") || upper.Contains("HIGH") || v.Contains("严重") || v.Contains("紧急"))
            return "P0";
        if (v.Contains("中") || upper.Contains("MEDIUM") || upper.Contains("NORMAL"))
            return "P1";
        if (v.Contains("低") || upper.Contains("LOW"))
            return "P2";
        return "P2";
    }

    /// <summary>用例类型推断：接口/API → Api；移动端 → Mobile；其余 Web。</summary>
    public static TestType MapType(string? category, string? scenario, string? steps)
    {
        var text = $"{category} {scenario} {steps}";
        if (text.Contains("接口", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("API", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("api", StringComparison.Ordinal))
            return TestType.Api;
        if (text.Contains("移动端", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("APP", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("手机", StringComparison.Ordinal))
            return TestType.Mobile;
        return TestType.Web;
    }

    /// <summary>表头/关键字归一化：去掉空白与常见分隔符，转小写。</summary>
    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var chars = raw.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('/' or '\\' or '、' or '，' or ',' or '（' or '）' or '(' or ')' or ':' or '：' or '*'))
            .Select(char.ToLowerInvariant);
        return new string(chars.ToArray());
    }
}
