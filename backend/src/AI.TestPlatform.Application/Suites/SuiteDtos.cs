using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Suites;

public record SuiteSummaryDto(
    Guid Id, Guid ProjectId, string Name, string? Description, SuiteKind Kind,
    int CaseCount,
    Guid? EnvironmentId, string? EnvironmentName,
    DateTime? LastRunAt, Guid? LastSuiteRunId, int LastCreatedCount, string? LastError,
    DateTime CreatedAt, DateTime UpdatedAt,
    // 执行编排：失败策略（Continue / StopOnFailure）
    SuiteFailurePolicy FailurePolicy = SuiteFailurePolicy.Continue,
    /// <summary>创建人显示名（M8 审计字段）</summary>
    string? CreatedByName = null);

public record SuiteDetailDto(
    Guid Id, Guid ProjectId, string Name, string? Description, SuiteKind Kind,
    Guid? EnvironmentId, string? EnvironmentName,
    List<SuiteCaseItemDto> Cases,
    DateTime? LastRunAt, Guid? LastSuiteRunId, int LastCreatedCount, string? LastError,
    DateTime CreatedAt, DateTime UpdatedAt,
    SuiteFailurePolicy FailurePolicy = SuiteFailurePolicy.Continue);

/// <summary>套件内的一条用例（带顺序、前置依赖与可执行性提示）</summary>
public record SuiteCaseItemDto(
    Guid TestCaseId, string Name, string? Module, string? Priority,
    TestType Type, bool IsFlaky, bool VisualEnabled, Guid? DataSetId, int DataRowCount, int Order,
    /// <summary>前置用例（同一套件内）；为空表示没有前置</summary>
    Guid? DependsOnTestCaseId = null,
    /// <summary>前置用例名称（已删除时给占位名），省掉界面为了显示一个名字再查一次</summary>
    string? DependsOnName = null);

/// <summary>
/// 套件成员条目：用例 + 前置用例（执行编排）。
/// 依赖与成员一起提交——「顺序 + 前置」本来就是同一份编排配置，
/// 拆成第二个接口必然出现「用例加上了、依赖没保存」的半成品状态。
/// </summary>
public record SuiteCaseSpec(Guid TestCaseId, Guid? DependsOnTestCaseId = null);

public record CreateSuiteRequest(
    Guid ProjectId, string Name, string? Description, SuiteKind Kind,
    Guid? EnvironmentId, List<SuiteCaseSpec>? Cases,
    SuiteFailurePolicy FailurePolicy = SuiteFailurePolicy.Continue);

public record UpdateSuiteRequest(
    string Name, string? Description, SuiteKind Kind,
    Guid? EnvironmentId, List<SuiteCaseSpec>? Cases,
    SuiteFailurePolicy FailurePolicy = SuiteFailurePolicy.Continue);

/// <summary>套件运行参数：可临时换环境、按浏览器矩阵展开、传变量覆盖</summary>
public record RunSuiteRequest(
    Guid? EnvironmentId = null,
    List<string>? Browsers = null,
    Dictionary<string, string>? Variables = null,
    bool ExpandDataSets = true);

public record SuiteRunResult(
    Guid SuiteId, Guid SuiteRunId, int CreatedCount,
    List<Guid> ExecutionIds, int CasesWithoutData, string? Error);

/// <summary>套件的历史运行（用于趋势与门禁追踪）</summary>
public record SuiteRunSummaryDto(
    Guid SuiteRunId, Guid SuiteId, DateTime StartedAt,
    int Total, int Passed, int Failed, int Error, int Skipped, int Pending,
    double PassRate, int DurationMs, string? TriggerSource,
    /// <summary>本批次里因编排（前置未通过 / 快停）被跳过的条数，与「其它原因跳过」区分开</summary>
    int OrchestrationSkipped = 0);

/// <summary>套件成员变更（全量替换，含前置依赖）</summary>
public record SetSuiteCasesRequest(List<SuiteCaseSpec>? Cases);
