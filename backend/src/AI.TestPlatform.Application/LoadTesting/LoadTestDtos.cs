using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.LoadTesting;

/// <summary>压测场景列表项</summary>
public sealed record LoadTestScenarioSummaryDto(
    Guid Id,
    Guid ProjectId,
    string? ProjectName,
    string Name,
    string? Description,
    int Source,
    int VirtualUsers,
    int DurationSeconds,
    int CaseCount,
    string? ScriptHash,
    DateTime? ScriptGeneratedAt,
    /// <summary>最近一次运行的状态（没跑过为 null）</summary>
    int? LastRunStatus,
    DateTime? LastRunAt,
    double? LastP95Ms,
    double? LastErrorRate,
    Guid? CreatedById,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>压测场景详情</summary>
public sealed record LoadTestScenarioDetailDto(
    Guid Id,
    Guid ProjectId,
    string? ProjectName,
    string Name,
    string? Description,
    int Source,
    Guid? EnvironmentId,
    string? TargetBaseUrl,
    Guid? ApiDefinitionId,
    List<string> Operations,
    LoadTestProfile Profile,
    List<LoadTestThreshold> Thresholds,
    Dictionary<string, string> Variables,
    int VirtualUsers,
    int DurationSeconds,
    string? ScriptText,
    string? ScriptHash,
    DateTime? ScriptGeneratedAt,
    List<Guid> CaseIds,
    Guid? CreatedById,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>创建压测场景</summary>
public sealed record CreateLoadTestScenarioRequest(
    Guid ProjectId,
    string Name,
    string? Description,
    LoadTestSource Source,
    Guid? EnvironmentId,
    string? TargetBaseUrl,
    Guid? ApiDefinitionId,
    List<string>? Operations,
    LoadTestProfile? Profile,
    List<LoadTestThreshold>? Thresholds,
    Dictionary<string, string>? Variables,
    List<Guid>? CaseIds);

/// <summary>更新压测场景（整体覆盖，与创建同构）</summary>
public sealed record UpdateLoadTestScenarioRequest(
    string Name,
    string? Description,
    Guid? EnvironmentId,
    string? TargetBaseUrl,
    Guid? ApiDefinitionId,
    List<string>? Operations,
    LoadTestProfile? Profile,
    List<LoadTestThreshold>? Thresholds,
    Dictionary<string, string>? Variables,
    List<Guid>? CaseIds);

/// <summary>生成脚本的结果。<see cref="Warnings"/> 要透出到界面，不能静默丢</summary>
public sealed record GenerateScriptResultDto(string Script, string Hash, List<string> Warnings);

/// <summary>压测运行列表项</summary>
public sealed record LoadTestRunSummaryDto(
    Guid Id,
    Guid ScenarioId,
    string ScenarioName,
    int Status,
    string TargetBaseUrl,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int? DurationMs,
    long TotalRequests,
    double? Rps,
    double? P95Ms,
    double? P99Ms,
    double? ErrorRate,
    bool? ThresholdsPassed,
    int ThresholdTotal,
    int ThresholdFailed,
    string? ErrorMessage,
    DateTime CreatedAt);

/// <summary>压测运行详情（指标 + 阈值明细）</summary>
public sealed record LoadTestRunDetailDto(
    Guid Id,
    Guid ScenarioId,
    string ScenarioName,
    Guid ProjectId,
    int Status,
    string TargetBaseUrl,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int? DurationMs,
    string? K6Version,
    int? ExitCode,
    string? ErrorMessage,
    long TotalRequests,
    double? Rps,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    double? MaxMs,
    double? ErrorRate,
    double? ChecksRate,
    long Iterations,
    int? VusMax,
    bool? ThresholdsPassed,
    int ThresholdTotal,
    int ThresholdFailed,
    List<LoadTestThresholdResult> ThresholdResults,
    bool HasSummary,
    bool HasLog,
    DateTime CreatedAt);

/// <summary>OpenAPI 导入后可选的操作</summary>
public sealed record OpenApiOperationDto(string Method, string Path, string Label);

/// <summary>导入 OpenAPI 文档（复用平台既有的 SwaggerImporter）</summary>
public sealed record ImportOpenApiRequest(Guid ProjectId, string Name, string Spec);

/// <summary>导入结果：落一条 ApiDefinition + 返回可勾选的操作</summary>
public sealed record ImportOpenApiResultDto(Guid ApiDefinitionId, string ApiName, string BaseUrl,
    List<OpenApiOperationDto> Operations);
