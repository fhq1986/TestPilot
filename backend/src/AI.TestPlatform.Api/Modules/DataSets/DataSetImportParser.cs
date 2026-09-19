using System.Text;
using AI.TestPlatform.Application.DataSets;
using ClosedXML.Excel;

namespace AI.TestPlatform.Api.Modules.DataSets;

/// <summary>
/// 数据集导入解析：首行作为列名，其余行作为数据。
/// 支持 .xlsx（取第一个工作表）与 .csv/.txt（逗号或制表符分隔，支持双引号包裹）。
/// </summary>
public static class DataSetImportParser
{
    private const int MaxColumns = 50;
    private const int MaxRows = 500;

    public static async Task<DataSetImportResult> ParseAsync(Stream stream, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" or ".xlsm" => ParseExcel(stream),
            ".csv" or ".txt" => ParseDelimited(await ReadTextAsync(stream, ct)),
            _ => throw new InvalidOperationException("仅支持 .xlsx 或 .csv 文件"),
        };
    }

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>解析 Excel：第一个非空工作表，首行作为列名</summary>
    public static DataSetImportResult ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.RowsUsed().Any())
                    ?? throw new InvalidOperationException("Excel 中没有可用的工作表");

        var usedRows = sheet.RowsUsed().ToList();
        if (usedRows.Count == 0)
            throw new InvalidOperationException("工作表为空，至少需要一行列名");

        var headerRow = usedRows[0];
        // 列名与列号的对应关系（跳过空列名）
        var columnIndexes = headerRow.CellsUsed()
            .Select(cell => (Name: cell.GetString().Trim(), Column: cell.Address.ColumnNumber))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .Take(MaxColumns)
            .ToList();
        var columns = columnIndexes.Select(x => x.Name).ToList();
        if (columns.Count == 0)
            throw new InvalidOperationException("首行没有可用的列名");

        var rows = new List<Dictionary<string, string>>();
        var skipped = 0;
        var warnings = new List<string>();

        foreach (var row in usedRows.Skip(1))
        {
            if (rows.Count >= MaxRows)
            {
                warnings.Add($"数据行超过 {MaxRows} 行，已截断（多余行未导入）");
                break;
            }
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            var hasValue = false;
            foreach (var (name, columnNumber) in columnIndexes)
            {
                var value = row.Cell(columnNumber).GetFormattedString().Trim();
                if (value.Length > 0) hasValue = true;
                data[name] = value;
            }
            if (!hasValue)
            {
                skipped++;
                continue;
            }
            rows.Add(data);
        }

        return new DataSetImportResult(columns, rows, skipped, warnings);
    }

    /// <summary>解析 CSV / TSV：首行作为列名</summary>
    public static DataSetImportResult ParseDelimited(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (lines.Count == 0) throw new InvalidOperationException("文件内容为空");

        // 制表符分隔的文件按制表符切分（Excel 复制粘贴常见）
        var separator = lines[0].Contains('\t') && !lines[0].Contains(',') ? '\t' : ',';
        var header = SplitLine(lines[0], separator);
        var columns = header.Select(h => h.Trim()).Where(h => !string.IsNullOrEmpty(h)).Take(MaxColumns).ToList();
        if (columns.Count == 0) throw new InvalidOperationException("首行没有可用的列名");

        var rows = new List<Dictionary<string, string>>();
        var skipped = 0;
        var warnings = new List<string>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (rows.Count >= MaxRows)
            {
                warnings.Add($"数据行超过 {MaxRows} 行，已截断（多余行未导入）");
                break;
            }
            var cells = SplitLine(lines[i], separator);
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            var hasValue = false;
            for (var c = 0; c < columns.Count; c++)
            {
                var value = c < cells.Count ? cells[c].Trim() : string.Empty;
                if (value.Length > 0) hasValue = true;
                data[columns[c]] = value;
            }
            if (!hasValue)
            {
                skipped++;
                continue;
            }
            rows.Add(data);
        }

        return new DataSetImportResult(columns, rows, skipped, warnings);
    }

    /// <summary>按分隔符切分一行，支持双引号包裹与 "a""b" 转义</summary>
    internal static List<string> SplitLine(string line, char separator)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == separator)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
