using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Requirements;

/// <summary>需求列表项（含覆盖统计，列表页直接渲染）</summary>
public record RequirementListItemDto(
    Guid Id, Guid ProjectId, string Title, string? Description,
    string? ExternalKey, string? Priority, DateTime CreatedAt,
    /// <summary>关联用例数（不含已软删除的用例，由 TestCase 的全局查询过滤器保证）</summary>
    int CaseCount,
    /// <summary>关联用例中「最近一次执行已通过」的用例数——需求的"验证进度"</summary>
    int PassedCaseCount,
    /// <summary>创建人显示名（M8 审计字段）</summary>
    string? CreatedByName = null,
    // ------------------------------ 进度字段
    DateTime? PlanStartDate = null,
    DateTime? PlanEndDate = null,
    DateTime? ActualStartDate = null,
    DateTime? ActualEndDate = null,
    /// <summary>状态：未开始 / 进行中 / 已完成</summary>
    RequirementStatus Status = RequirementStatus.NotStarted,
    /// <summary>关联的测试计划数量（右则点击可展开查看）</summary>
    int LinkedPlanCount = 0);

/// <summary>需求覆盖统计（页面顶部统计卡）</summary>
public record RequirementCoverageDto(
    Guid ProjectId,
    int TotalRequirements,
    /// <summary>至少挂了一个用例的需求数</summary>
    int CoveredRequirements,
    /// <summary>没有任何用例关联的需求（覆盖缺口）</summary>
    int UncoveredRequirements,
    /// <summary>覆盖率 = Covered / Total（百分数，保留 1 位）</summary>
    double CoverageRate,
    /// <summary>未覆盖需求明细（最多带 20 条，缺口一目了然）</summary>
    List<RequirementListItemDto> UncoveredList);

public record CreateRequirementRequest(
    Guid ProjectId, string Title, string? Description = null,
    string? ExternalKey = null, string? Priority = null,
    DateTime? PlanStartDate = null, DateTime? PlanEndDate = null,
    DateTime? ActualStartDate = null, DateTime? ActualEndDate = null,
    RequirementStatus? Status = null);

public record UpdateRequirementRequest(
    string Title, string? Description = null,
    string? ExternalKey = null, string? Priority = null,
    DateTime? PlanStartDate = null, DateTime? PlanEndDate = null,
    DateTime? ActualStartDate = null, DateTime? ActualEndDate = null,
    RequirementStatus? Status = null);

/// <summary>查询需求关联的测试计划（供需求列表页"查看关联计划"弹窗用）</summary>
public record RequirementPlanRefDto(
    Guid PlanId, string PlanName, string? ReleaseName, TestPlanStatus Status, DateTime? LastRoundAt);
