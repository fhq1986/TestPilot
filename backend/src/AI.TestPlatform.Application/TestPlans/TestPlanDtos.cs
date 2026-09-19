using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.TestPlans;

// ------------------------------ 列表与详情

public record TestPlanSummaryDto(
    Guid Id, Guid ProjectId,
    /// <summary>所属项目名。计划列表与详情都要显示它——只给 ProjectId 的话前端得再查一次项目，
    /// 跨项目计划混排时也看不出这条计划属于谁</summary>
    string? ProjectName,
    string Name, string? Description,
    string? ReleaseName, TestPlanStatus Status,
    DateTime? StartsAt, DateTime? EndsAt,
    Guid? OwnerId, string? OwnerName,
    double TargetPassRate, bool AllowErrors, bool ExcludeFlakyFromFailure, PlanGateMode GateMode,
    /// <summary>缺陷验收门槛：项目存在未闭环致命/严重缺陷时不达标（P2）</summary>
    bool DefectGateEnabled,
    /// <summary>默认环境。列表页「开新一轮」要用它判断是否弹环境选择框（相对地址/自动登录依赖 BaseUrl）</summary>
    Guid? EnvironmentId,
    int CaseCount,
    // 进度：已完成执行数 / 总执行数（进行中轮次用；无进行中轮次时为空）
    int? RunningRoundNo, int? RunningCaseCount, int? RunningPassedCount,
    DateTime? LastRoundAt, int? LastRoundNo, double? LastPassRate,
    string? LastError,
    DateTime CreatedAt, DateTime? UpdatedAt);

public record TestPlanDetailDto(
    Guid Id, Guid ProjectId,
    /// <summary>所属项目名，详情页「概述」里展示（项目是计划的硬边界，不该只显示一个 GUID）</summary>
    string? ProjectName,
    string Name, string? Description,
    string? ReleaseName, TestPlanStatus Status,
    DateTime? StartsAt, DateTime? EndsAt,
    Guid? OwnerId, string? OwnerName,
    double TargetPassRate, bool AllowErrors, bool ExcludeFlakyFromFailure, PlanGateMode GateMode,
    bool DefectGateEnabled,
    Guid? EnvironmentId, string? EnvironmentName,
    IReadOnlyList<string> Browsers, bool ExpandDataSets,
    int CaseCount, int RoundCount,
    /// <summary>哪些定时任务把这个计划纳入了执行范围（只读展示，配置入口在定时任务页）</summary>
    IReadOnlyList<PlanScheduleRefDto>? Schedules,
    /// <summary>范围体检结果：不可执行的用例（已删除 / 移动端 / 无步骤）</summary>
    IReadOnlyList<PlanScopeIssueDto> ScopeIssues,
    DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>范围体检发现的问题。提测前突击编排时最需要它</summary>
public record PlanScopeIssueDto(string Level, string Kind, Guid? TestCaseId, string Name, string Message);

/** 引用该计划的定时任务 */
public record PlanScheduleRefDto(Guid Id, string Name, string CronExpression, bool Enabled);

public record TestPlanItemDto(
    Guid TestCaseId, string Name, string? Module, string? Priority,
    TestType Type,
    /// <summary>用例状态；**为 null 表示用例已删除**（范围里还留着，执行时会被跳过）。
    /// 原来这里是拿 `TestCaseStatus.Deprecated` 当"已删除"的哨兵值——把删除混进了状态语义，
    /// 拆出软删除后改用可空表示。</summary>
    TestCaseStatus? Status,
    bool IsFlaky, int Order,
    /// <summary>用例已软删除（范围里还留着，执行时会被跳过）</summary>
    bool Deleted);

// ------------------------------ 请求

public record CreateTestPlanRequest(
    Guid ProjectId, string Name, string? Description = null,
    string? ReleaseName = null,
    DateTime? StartsAt = null, DateTime? EndsAt = null,
    Guid? OwnerId = null,
    double? TargetPassRate = null, bool? AllowErrors = null,
    bool? ExcludeFlakyFromFailure = null, PlanGateMode? GateMode = null,
    bool? DefectGateEnabled = null,
    Guid? EnvironmentId = null, List<string>? Browsers = null, bool? ExpandDataSets = null,
    List<Guid>? TestCaseIds = null);

public record UpdateTestPlanRequest(
    string Name, string? Description = null,
    string? ReleaseName = null,
    DateTime? StartsAt = null, DateTime? EndsAt = null,
    Guid? OwnerId = null,
    double? TargetPassRate = null, bool? AllowErrors = null,
    bool? ExcludeFlakyFromFailure = null, PlanGateMode? GateMode = null,
    bool? DefectGateEnabled = null,
    Guid? EnvironmentId = null, List<string>? Browsers = null, bool? ExpandDataSets = null);

public record SetPlanItemsRequest(List<Guid> TestCaseIds);

/// <summary>从套件导入：append 追加去重，replace 先清空再导入</summary>
public record ImportPlanItemsRequest(Guid SuiteId, string Mode = "append");

public record SetPlanStatusRequest(TestPlanStatus Status);

// ------------------------------ 轮次

public record StartRoundRequest(
    Guid? EnvironmentId = null, List<string>? Browsers = null,
    bool? ExpandDataSets = null, Dictionary<string, string>? Variables = null);

/// <summary>
/// 执行结果的计数与通过率。
///
/// 单独成对象而不是把数字平铺进上层：上层已经有同名的布尔判定（<c>Passed</c>）与
/// 字符串错误（<c>Error</c>），平铺会撞名——序列化成 JSON 后更糟，
/// 会出现 <c>"passed": false</c> 与 <c>"passed": 43</c> 这样的**重复键**，解析方拿到的值不确定。
/// </summary>
public record PlanStatsDto(
    /// <summary>参与判定的样本数 = Total − Skipped（被排除的 flaky 也已扣除），
    /// 因此恒有 <c>Total == Passed + Failed + Error</c></summary>
    int Total,
    int Passed, int Failed, int Error, int Skipped, int Pending,
    double PassRate);

public record PlanRoundSummaryDto(
    Guid Id, int RoundNo, PlanRoundStatus Status,
    TriggerType TriggerType, string? TriggerSource, DateTime StartedAt, DateTime? CompletedAt,
    int CreatedCount,
    /// <summary>轮次级错误（如"范围内没有可执行的用例"），与 stats.Error 不是一回事</summary>
    string? Error,
    PlanStatsDto Stats,
    /// <summary>本轮是否达到计划当前的目标（还在跑则为 null，判定无意义）</summary>
    bool? GatePassed);

public record PlanRoundCaseResultDto(
    Guid TestCaseId, string TestCaseName, string? Module, int Order,
    /// <summary>本轮该用例的执行结果（同一用例可能因浏览器/数据行有多条）</summary>
    IReadOnlyList<PlanRoundExecutionDto> Executions);

public record PlanRoundExecutionDto(
    Guid ExecutionId, string? BrowserName, string? DataSetRowLabel,
    ExecutionStatus Status, int? DurationMs, string? ErrorMessage,
    bool IsFlaky, string? ScreenshotUrl, string? TraceUrl);

// ------------------------------ 达标判定与报告

public record PlanGateResult(
    /// <summary>达标结论</summary>
    bool Passed,
    string PlanName,
    string? ReleaseName,
    double TargetPassRate,
    /// <summary>参与判定的轮次号（GateMode 决定取哪一轮）</summary>
    int? EvaluatedRoundNo,
    PlanStatsDto Stats,
    /// <summary>未达标的原因，逐条人话。CI 日志里靠它定位，不能只给一个 passed:false</summary>
    IReadOnlyList<string> Reasons,
    /// <summary>阻断达标的用例（已排除被认定不可信的 flaky 样本）</summary>
    IReadOnlyList<PlanBlockingCaseDto> BlockingCases);

public record PlanBlockingCaseDto(
    Guid TestCaseId, string Name, string? Module, ExecutionStatus Status,
    string? ErrorMessage,
    /// <summary>标注 flaky，让人一眼看出是代码问题还是抖动</summary>
    bool IsFlaky);

public record PlanModuleStatDto(
    string Module, int Total, int Passed, int Failed, int Error, int Skipped, double PassRate);

public record PlanRoundTrendDto(int RoundNo, DateTime StartedAt, DateTime? CompletedAt,
    int Total, int Passed, int Failed, int Error, int Skipped, double PassRate, bool? GatePassed);

public record TestPlanReportDto(
    TestPlanSummaryDto Plan,
    PlanGateResult Gate,
    IReadOnlyList<PlanRoundTrendDto> Trends,
    IReadOnlyList<PlanModuleStatDto> Modules,
    IReadOnlyList<PlanBlockingCaseDto> BlockingCases);

