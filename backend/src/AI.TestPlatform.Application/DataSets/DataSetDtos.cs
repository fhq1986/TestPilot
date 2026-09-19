namespace AI.TestPlatform.Application.DataSets;

public record DataSetSummaryDto(
    Guid Id, Guid ProjectId, string? ProjectName, string Name, string? Description,
    int ColumnCount, int RowCount, int UsedByCaseCount,
    DateTime CreatedAt, DateTime UpdatedAt);

public record DataSetDto(
    Guid Id, Guid ProjectId, string Name, string? Description,
    List<string> Columns, List<Dictionary<string, string>> Rows,
    bool FirstRowIsSample, int UsedByCaseCount,
    List<DataSetUsageDto> UsedBy,
    DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>数据集被哪些用例引用（改数据集前的影响面提示）</summary>
public record DataSetUsageDto(Guid TestCaseId, string Name, string? Module);

public record CreateDataSetRequest(
    Guid ProjectId, string Name, string? Description,
    List<string> Columns, List<Dictionary<string, string>> Rows,
    bool FirstRowIsSample = false);

public record UpdateDataSetRequest(
    string Name, string? Description,
    List<string> Columns, List<Dictionary<string, string>> Rows,
    bool FirstRowIsSample = false);

/// <summary>Excel/CSV 导入结果：首行作为列名，其余为数据行</summary>
public record DataSetImportResult(
    List<string> Columns, List<Dictionary<string, string>> Rows,
    int SkippedEmptyRows, List<string> Warnings);

/// <summary>用例步骤中引用的变量与数据集的匹配情况（绑定数据集时的校验提示）</summary>
public record CaseVariableCheckDto(
    Guid TestCaseId, string CaseName,
    Guid? DataSetId, string? DataSetName,
    List<string> UsedVariables,
    List<string> AvailableColumns,
    List<string> MissingVariables,
    List<string> UnusedColumns,
    Dictionary<string, string> SampleRow);

public record AttachDataSetRequest(Guid? DataSetId);
