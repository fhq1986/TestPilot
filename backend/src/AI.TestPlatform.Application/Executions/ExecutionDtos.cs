using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Executions;

/// <summary>
/// 单条执行请求。Browser 为空时按「环境 → 用例 → chromium」顺序解析；
/// DataSetRowIndex 指定用数据集的哪一行（用例绑定数据集时有效）；
/// Variables 为临时变量覆盖（优先于数据集行），便于 CI 用不同账号跑同一用例。
/// </summary>
public record CreateExecutionRequest(
    Guid TestCaseId, Guid? EnvironmentId,
    string? Browser = null,
    int? DataSetRowIndex = null,
    Dictionary<string, string>? Variables = null);

/// <summary>
/// 批量执行请求。Browsers 传多个时按「用例 × 浏览器」矩阵展开；
/// ExpandDataSets 为 true 且用例绑定了数据集时，按数据行逐行展开。
/// </summary>
public record BatchExecuteRequest(
    List<Guid> TestCaseIds, Guid? EnvironmentId,
    List<string>? Browsers = null,
    bool ExpandDataSets = true,
    Dictionary<string, string>? Variables = null);

public record BatchExecuteResult(int Created, List<Guid> ExecutionIds, List<Guid> SkippedCaseIds,
    /// <summary>因数据集为空而未展开的用例数（仅提示，不算失败）</summary>
    int CasesWithoutData = 0);

public record ExecutionSummaryDto(
    Guid Id, Guid? TestCaseId, string TestCaseName, ExecutionStatus Status,
    TriggerType TriggerType, string? BrowserVersion, DateTime? StartedAt,
    DateTime? EndedAt, int? DurationMs, int ResultCount, DateTime CreatedAt,
    string? AIDiagnosis, string? EnvironmentName, Guid? EnvironmentId = null,
    // CI / 定时任务上下文：便于列表区分「谁触发的这次执行」
    string? TriggerSource = null, string? CommitSha = null, string? Branch = null, string? BuildNumber = null,
    // 迭代 B：浏览器与数据行
    string? BrowserName = null, string? DataSetRowLabel = null,
    Guid? SuiteId = null, Guid? SuiteRunId = null,
    // 迭代 D：执行 trace（仅失败时保留，可在 trace viewer 里逐步回放）
    string? TraceUrl = null, long? TraceSizeBytes = null,
    // 执行编排：前置用例与编排跳过原因（列表上要能解释「为什么这条没跑」）
    Guid? DependsOnTestCaseId = null, string? SkipReason = null,
    // 迭代 F：执行录像（仅失败时保留，可在 Playwright trace viewer 里逐步回放 DOM 与网络）
    string? VideoUrl = null, long? VideoSizeBytes = null,
    // 迭代 G：所属项目名（列表跨项目展示时不用再 JOIN）
    string? ProjectName = null,
    // M8 Agent 自愈：是否由 Agent 自愈后通过（列表打「自愈通过」标记）
    bool AgentHealed = false);

public record ExecutionResultDto(
    Guid Id, int StepOrder, ExecutionStatus Status, int? DurationMs,
    string? ScreenshotUrl, string? Log, string? ErrorMessage, string? StackTrace,
    StepConfig? StepSnapshot, Guid? TestStepId,
    // 步骤动作类型（历史行为 null）：详情页步骤摘要以「动作 + 定位方式 + 配置摘要」展示
    int? StepActionType = null,
    // 视觉回归
    VisualStatus VisualStatus = VisualStatus.Skipped, double? VisualDiffRatio = null,
    string? BaselineImageUrl = null, string? DiffImageUrl = null, string? VisualNote = null);

/// <summary>
/// 步骤开始执行的实时推送载荷（仅 SignalR，不落库）。
/// 前端据此先显示「执行中」占位行，StepCompleted 到达后用真实结果替换。
/// </summary>
public record StepStartedDto(int StepOrder, StepConfig? StepSnapshot);

public record ExecutionDetailDto(
    Guid Id, Guid? TestCaseId, string TestCaseName, ExecutionStatus Status,
    TriggerType TriggerType, string? BrowserVersion, DateTime? StartedAt,
    DateTime? EndedAt, int? DurationMs, DateTime CreatedAt,
    string? AIDiagnosis, string? AISuggestedFix, float? DiagnosisConfidence,
    IReadOnlyList<ExecutionResultDto> Results, string? EnvironmentName, Guid? EnvironmentId = null,
    // 迭代 B
    string? BrowserName = null, string? DataSetRowLabel = null, int? DataSetRowIndex = null,
    Guid? SuiteId = null, Guid? SuiteRunId = null,
    // 迭代 D：执行 trace
    string? TraceUrl = null, long? TraceSizeBytes = null,
    // 重试可观测性：步骤内部重试消耗次数，>0 说明执行有靠重试稳住的成分
    int StepRetryCount = 0,
    // 执行编排：前置用例与编排跳过原因
    Guid? DependsOnTestCaseId = null, string? SkipReason = null,
    // 本次执行的步骤总数快照（共享步骤组展开后、不含自动登录前置），进度指示「共 x 步」用
    int? TotalSteps = null,
    // 所属项目名（详情页展示用；透过用例的 Project 导航取）
    string? ProjectName = null,
    // 迭代 F：执行录像（仅失败时保留）
    string? VideoUrl = null, long? VideoSizeBytes = null,
    // M8 Agent 自愈闭环（详见 docs/m8-agent-design.md §4.3）
    bool AgentHealed = false,
    /// <summary>执行级最终结论（AgentAttemptResult? 的 int 值）：0=Fixed 2=Failed 3=BudgetExhausted …</summary>
    int? AgentFinalVerdict = null,
    int AgentLoopCount = 0,
    int AgentBudgetUsed = 0);

/// <summary>
/// M8 Agent 修复轨迹（执行详情页「Agent 修复轨迹」展示）。
/// FixCategory / Result 以 int 返回（与项目其他枚举一致，前端做文案映射）。
/// </summary>
public record AgentAttemptDto(
    Guid Id, int AttemptNumber, int TargetStepOrder,
    int FixCategory, float Confidence, string? FixSummary,
    bool AppliedSuccessfully, string? AppliedActions,
    int Result, string? FailureAfterFix,
    bool NeedsApproval, bool? Approved,
    int LlmInputTokens, int LlmOutputTokens, string? LlmModel,
    DateTime CreatedAt, DateTime? CompletedAt);

public static class ExecutionMappingExtensions
{
    /// <summary>
    /// 对外 trace URL 统一为受权端点（安全审查 S1）：
    /// 库里 TraceUrl 落的是历史静态路径（/traces/...），这里按执行 ID 重新生成，存量数据免迁移。
    /// </summary>
    public static string? TraceApiUrl(Execution e) =>
        e.TraceUrl is null ? null : $"/api/executions/{e.Id:N}/trace";

    /// <summary>录像的对外 URL（与 trace 同样是受权端点）</summary>
    public static string? VideoApiUrl(Execution e) =>
        e.VideoUrl is null ? null : $"/api/executions/{e.Id:N}/video";

    public static ExecutionSummaryDto ToSummaryDto(this Execution e, string testCaseName) => new(
        e.Id, e.TestCaseId, testCaseName, e.Status, e.TriggerType, e.BrowserVersion,
        e.StartedAt, e.EndedAt, e.DurationMs, e.Results.Count, e.CreatedAt,
        e.AIDiagnosis, e.EnvironmentSnapshot?.Name ?? e.Environment?.Name, e.EnvironmentId,
        e.TriggerSource, e.CommitSha, e.Branch, e.BuildNumber,
        e.BrowserName, e.DataSetRowLabel, e.SuiteId, e.SuiteRunId,
        TraceApiUrl(e), e.TraceSizeBytes, e.DependsOnTestCaseId, e.SkipReason,
        VideoApiUrl(e), e.VideoSizeBytes, null, e.AgentHealed);

    public static ExecutionDetailDto ToDetailDto(this Execution e, string testCaseName) => new(
        e.Id, e.TestCaseId, testCaseName, e.Status, e.TriggerType, e.BrowserVersion,
        e.StartedAt, e.EndedAt, e.DurationMs, e.CreatedAt,
        e.AIDiagnosis, e.AISuggestedFix, e.DiagnosisConfidence,
        e.Results.OrderBy(r => r.StepOrder).Select(r => r.ToDto()).ToList(),
        e.EnvironmentSnapshot?.Name ?? e.Environment?.Name, e.EnvironmentId,
        e.BrowserName, e.DataSetRowLabel, e.DataSetRowIndex, e.SuiteId, e.SuiteRunId,
        TraceApiUrl(e), e.TraceSizeBytes, e.StepRetryCount, e.DependsOnTestCaseId, e.SkipReason,
        e.TotalSteps,
        e.TestCase?.Project?.Name,
        VideoApiUrl(e), e.VideoSizeBytes,
        e.AgentHealed, e.AgentFinalVerdict is null ? null : (int)e.AgentFinalVerdict,
        e.AgentLoopCount, e.AgentBudgetUsed);

    public static ExecutionResultDto ToDto(this ExecutionResult r) => new(
        r.Id, r.StepOrder, r.Status, r.DurationMs, r.ScreenshotUrl, r.Log,
        r.ErrorMessage, r.StackTrace, r.StepSnapshot, r.TestStepId,
        r.StepActionType,
        r.VisualStatus, r.VisualDiffRatio, r.BaselineImageUrl, r.DiffImageUrl, r.VisualNote);

    /// <summary>M8 Agent 修复轨迹 DTO</summary>
    public static AgentAttemptDto ToDto(this AgentAttempt a) => new(
        a.Id, a.AttemptNumber, a.TargetStepOrder,
        (int)a.FixCategory, a.Confidence, a.FixSummary,
        a.AppliedSuccessfully, a.AppliedActions,
        (int)a.Result, a.FailureAfterFix,
        a.NeedsApproval, a.Approved,
        a.LlmInputTokens, a.LlmOutputTokens, a.LlmModel,
        a.CreatedAt, a.CompletedAt);
}

    /// <summary>M8 Agent 审批工作台列表项（跨执行，带用例/执行上下文）</summary>
    public record AgentApprovalItemDto(
        Guid AttemptId, Guid ExecutionId, string TestCaseName,
        int TargetStepOrder, int FixCategory, float Confidence, string? FixSummary,
        bool? Approved, Guid? ApprovedBy, DateTime? ApprovedAt, int Result, DateTime CreatedAt);

    /// <summary>
    /// M8 自愈度量（迭代 D2）：时间窗口内自愈尝试的聚合指标 + 误判检测。
    /// SuccessRate = (Fixed + Partial) / Total；MisjudgedCount = 被标记「自愈通过」的步骤，
    /// 在**同一用例后续执行**中于相同 StepOrder 再次失败的次数（疑似掩盖了真实缺陷）。
    /// </summary>
    public record AgentHealMetricsDto(
        int TotalAttempts,
        int Fixed,
        int Partial,
        int Failed,
        int Skipped,
        int Rejected,
        int BudgetExhausted,
        int Other,
        double SuccessRate,
        double? AvgFixMinutes,
        Dictionary<int, int> ByCategory,
        int MisjudgedCount);
