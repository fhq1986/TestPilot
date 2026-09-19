namespace AI.TestPlatform.Api.Modules.TestCases;

public record ImportModuleStatDto(string Module, int Total, int Imported, int Skipped, int Failed);

public record ImportRowErrorDto(string Module, int RowNumber, string? CaseCode, string Message);

public record TestCaseImportResultDto(
    int TotalRows, int Imported, int Updated, int Skipped, int Failed, int AiParsed, int AiCaseCount,
    List<ImportModuleStatDto> Modules, List<ImportRowErrorDto> Errors, List<string> Warnings,
    // 可选的测试计划关联：导入的用例自动加入该计划范围（去重后的新增/已在范围内条数）
    int PlanLinked = 0, int PlanSkipped = 0);
