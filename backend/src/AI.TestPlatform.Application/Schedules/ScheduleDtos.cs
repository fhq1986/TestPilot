using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Schedules;

public record ScheduleSummaryDto(
    Guid Id, Guid ProjectId, string Name, string CronExpression, bool Enabled,
    string? Module, string? Priority, int TestCaseCount, Guid? EnvironmentId, string? EnvironmentName,
    DateTime? LastRunAt, DateTime? NextRunAt, int LastCreatedCount, string? LastError,
    // 便于前端展示「每天 02:00」这类可读描述
    string CronDescription,
    DateTime CreatedAt, DateTime UpdatedAt,
    // 迭代 B：浏览器矩阵与数据展开
    IReadOnlyList<string>? Browsers = null, bool ExpandDataSets = true,
    // 迭代 E：执行范围类型与计划范围（TestPlan 时 TestCaseCount 无意义）
    ScheduleScopeKind ScopeKind = ScheduleScopeKind.Cases,
    IReadOnlyList<SchedulePlanRefDto>? TestPlans = null);

/// <summary>范围里引用的计划（列表页要显示计划名而不是 ID）</summary>
public record SchedulePlanRefDto(Guid Id, string Name, string? ReleaseName, TestPlanStatus Status);

public record ScheduleDetailDto(
    Guid Id, Guid ProjectId, string Name, string CronExpression, bool Enabled,
    string? Module, string? Priority, List<Guid> TestCaseIds, Guid? EnvironmentId,
    DateTime? LastRunAt, DateTime? NextRunAt, int LastCreatedCount, string? LastError,
    string CronDescription, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<string>? Browsers = null, bool ExpandDataSets = true,
    ScheduleScopeKind ScopeKind = ScheduleScopeKind.Cases, List<Guid>? TestPlanIds = null);

public record CreateScheduleRequest(
    Guid ProjectId, string Name, string CronExpression, bool Enabled,
    string? Module, string? Priority, List<Guid>? TestCaseIds, Guid? EnvironmentId,
    List<string>? Browsers = null, bool ExpandDataSets = true,
    // 迭代 E：范围可选「测试计划」。ScopeKind=TestPlan 时 TestPlanIds 必填
    ScheduleScopeKind ScopeKind = ScheduleScopeKind.Cases,
    List<Guid>? TestPlanIds = null);

public record UpdateScheduleRequest(
    string Name, string CronExpression, bool Enabled,
    string? Module, string? Priority, List<Guid>? TestCaseIds, Guid? EnvironmentId,
    List<string>? Browsers = null, bool ExpandDataSets = true,
    ScheduleScopeKind ScopeKind = ScheduleScopeKind.Cases,
    List<Guid>? TestPlanIds = null);

/// <summary>定时任务立即试跑的结果</summary>
public record ScheduleRunResult(Guid ScheduleId, int CreatedCount, List<Guid> ExecutionIds,
    DateTime? NextRunAt, string? Error,
    /// <summary>
    /// 信息性提示（不是失败）。例如「计划已有进行中的轮次，本次跳过」——
    /// 这类情况写进 <see cref="Error"/> 会让前端弹红，夜间无人值守时也毫无意义。
    /// </summary>
    string? Message = null);

public record CronPreviewRequest(string CronExpression, int Count = 5);

public record CronPreviewResult(bool Valid, string? Error, string CronDescription, List<DateTime> Occurrences);
