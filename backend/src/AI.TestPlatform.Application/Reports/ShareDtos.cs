using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Reports;

/// <summary>创建报告分享链接</summary>
public record CreateShareRequest(
    ReportShareKind Kind,
    Guid RefId,
    Guid? ProjectId = null,
    string? Title = null,
    DateTime? From = null,
    DateTime? To = null,
    /// <summary>有效期天数；为空表示长期有效</summary>
    int? ExpiresInDays = 7);

public record ShareDto(
    Guid Id, string Token, ReportShareKind Kind, Guid RefId, Guid? ProjectId,
    string Title, DateTime? ExpiresAt, bool Revoked, int ViewCount, DateTime? LastViewedAt,
    DateTime CreatedAt, string Url);

// ---------------------------------------------------------------- 在线报告负载

public record ReportOverviewDto(
    int Total, int Passed, int Failed, int Error, int Skipped, int Pending,
    double PassRate, long DurationMs, DateTime? StartedAt, DateTime? EndedAt,
    /// <summary>涉及的浏览器集合（多浏览器矩阵时用于展示）</summary>
    IReadOnlyList<string> Browsers);

public record ReportModuleDto(string Module, int Total, int Passed, int Failed, double PassRate);

public record ReportStepDto(
    int StepOrder, string ActionType, int? Status, int? DurationMs,
    string? ErrorMessage, string? ScreenshotUrl,
    VisualStatus VisualStatus, double? VisualDiffRatio,
    string? BaselineImageUrl, string? DiffImageUrl, string? VisualNote);

public record CaseHistoryPointDto(DateTime At, int Status, int? DurationMs, string? BrowserName);

public record ReportCaseDto(
    Guid? ExecutionId, Guid? TestCaseId, string Name, string? CaseCode, string? Module, string? Priority,
    int Status, string? BrowserName, string? BrowserVersion, string? DataSetRowLabel,
    int? DurationMs, DateTime? StartedAt, string? ErrorMessage, string? AiDiagnosis,
    string? AISuggestedFix, string? EnvironmentName, string? TriggerSource,
    string? SourceSteps, string? ExpectedResult,
    List<ReportStepDto>? Steps, List<CaseHistoryPointDto>? History);

public record TrendPointDto(string Date, int Passed, int Failed, int Total);

public record FlakeRankItemDto(Guid TestCaseId, string Name, double FlakeRate, int Executions);

public record PublicReportDto(
    string Title, string Subtitle, string Kind,
    DateTime GeneratedAt, DateTime? ExpiresAt,
    /// <summary>被分享对象已被删除时为 true（仍返回结构化说明，避免链接变成 404）</summary>
    bool Missing,
    ReportOverviewDto Overview,
    IReadOnlyList<ReportModuleDto> Modules,
    IReadOnlyList<ReportCaseDto> Cases,
    IReadOnlyList<TrendPointDto> Trend,
    IReadOnlyList<FlakeRankItemDto> FlakeRank,
    Guid? SuiteId, string? SuiteName, Guid? SuiteRunId);
