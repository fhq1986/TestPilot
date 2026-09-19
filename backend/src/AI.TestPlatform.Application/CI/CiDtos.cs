using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.CI;

/// <summary>
/// CI 触发请求（POST /api/webhooks/executions，X-Webhook-Token 鉴权）。
///
/// 用例范围二选一：
/// - 直接给 TestCaseIds；
/// - 给 ProjectId（可叠加 Module / Priority）按条件筛选项目内的 Web/API 用例。
/// CI 上下文既可放请求体，也可通过请求头传入（X-Commit-Sha / X-Branch / X-Build-Number / X-CI-Source）。
/// </summary>
public record WebhookTriggerRequest(
    List<Guid>? TestCaseIds,
    Guid? ProjectId,
    string? Module,
    string? Priority,
    Guid? EnvironmentId,
    string? CommitSha,
    string? Branch,
    string? BuildNumber,
    string? Source,
    /// <summary>排除被标记为不稳定的用例（门禁流水线建议开启，避免 flake 误伤构建）</summary>
    bool ExcludeFlaky = false,
    /// <summary>浏览器矩阵：传多个时同一条用例会在每个浏览器上各跑一次</summary>
    List<string>? Browsers = null,
    /// <summary>变量覆盖（优先于数据集行），例如用 CI 提供的账号跑同一用例</summary>
    Dictionary<string, string>? Variables = null);

/// <summary>CI 单条执行结果摘要</summary>
public record CiExecutionResult(
    Guid ExecutionId, Guid? TestCaseId, string TestCaseName,
    ExecutionStatus Status, int? DurationMs, string? ErrorSummary,
    bool IsFlaky = false,
    string? BrowserName = null, string? DataSetRowLabel = null);

/// <summary>
/// CI 触发响应。同步等待模式（?wait=true）下 Completed=true、Success 表示本次回归是否全绿，
/// CI 脚本可直接用 Success 决定构建成败。
/// </summary>
public record CiTriggerResponse(
    bool Success, bool Completed, int Created,
    int Passed, int Failed, int Error, int Skipped, int Pending,
    int DurationMs, IReadOnlyList<CiExecutionResult> Executions);
