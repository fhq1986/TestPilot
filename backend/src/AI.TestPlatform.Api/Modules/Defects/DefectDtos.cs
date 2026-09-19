using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Modules.Defects;

// ------------------------------ 请求

public record CreateDefectRequest(
    Guid ProjectId,
    string Title,
    string? Description,
    DefectSeverity Severity,
    Guid? AssignedToId,
    string? ExternalRef,
    /// <summary>一键转缺陷：从哪个失败步骤来（提供时服务端自动快照证据并关联用例）</summary>
    Guid? FoundInExecutionId = null,
    int? FoundInStepOrder = null,
    /// <summary>
    /// 关联用例（可选，非必填）。必须与缺陷同项目。
    /// 与「来源用例」（一键转缺陷带出来的那条）会**合并去重**，不会互相覆盖。
    /// </summary>
    IReadOnlyList<Guid>? TestCaseIds = null);

public record UpdateDefectRequest(
    string Title,
    string? Description,
    DefectSeverity Severity,
    Guid? AssignedToId,
    string? ExternalRef,
    /// <summary>
    /// 关联用例的**目标全集**（不是增量）。
    /// null = 本次不改动关联；空数组 = 解除全部关联。
    ///
    /// 用"全集"而不是增删两个列表，是因为调用方（表单）手里本来就只有最终勾选结果；
    /// 让服务端做差集，既省一次往返，也避免"先删后加"中途失败留下半套关联。
    /// </summary>
    IReadOnlyList<Guid>? TestCaseIds = null);

/// <summary>
/// 状态流转。action: assign / fix / verify / close / reject / defer / reopen。
/// note 用于修复说明 / 驳回原因 / 挂起原因（reject 与 defer 必填）。
/// </summary>
public record DefectTransitionRequest(string Action, Guid? AssignedToId, string? Note);

public record LinkDefectCaseRequest(Guid TestCaseId);

public record DefectOccurrenceRequest(Guid ExecutionId, int StepOrder);

// ------------------------------ 响应

public record DefectListItemDto(
    Guid Id, Guid ProjectId, string ProjectName, string Title,
    DefectSeverity Severity, DefectStatus Status,
    string? AssignedToName, string? CreatedByName,
    Guid? FoundInExecutionId, int? FoundInStepOrder, string? FoundInTestCaseName,
    string? ExternalRef,
    DateTime CreatedAt, DateTime? FixedAt, DateTime? VerifiedAt);

public record DefectDetailDto(
    Guid Id, Guid ProjectId, string ProjectName, string Title, string? Description,
    DefectSeverity Severity, DefectStatus Status,
    Guid? AssignedToId, string? AssignedToName,
    Guid? CreatedById, string? CreatedByName,
    Guid? FoundInExecutionId, int? FoundInStepOrder, Guid? FoundInTestCaseId, string? FoundInTestCaseName,
    string? ExternalRef, string? ResolutionNote,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? FixedAt, DateTime? VerifiedAt,
    string? VerifiedByName,
    IReadOnlyList<DefectCaseLinkDto> Cases,
    IReadOnlyList<DefectOccurrenceDto> Occurrences);

public record DefectCaseLinkDto(Guid TestCaseId, string TestCaseName, string? Module);

public record DefectOccurrenceDto(Guid Id, Guid? ExecutionId, int StepOrder, DateTime OccurredAt);

/// <summary>仪表盘统计卡 + 趋势（P1 最小集；reopening 率等需要事件流水，见 P2）</summary>
public record DefectStatsDto(
    int OpenTotal,
    int OpenCritical, int OpenMajor, int OpenNormal, int OpenSuggestion,
    int CreatedLast7Days, int ClosedLast7Days,
    double? AvgFixHours, double? AvgVerifyHours,
    IReadOnlyList<DefectTrendPoint> Trend);

public record DefectTrendPoint(string Date, int Created, int Closed);
