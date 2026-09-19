using AI.TestPlatform.Application.Common;

namespace AI.TestPlatform.Application.Requirements;

/// <summary>需求列表项（含覆盖统计，列表页直接渲染）</summary>
public record RequirementListItemDto(
    Guid Id, Guid ProjectId, string Title, string? Description,
    string? ExternalKey, string? Priority, DateTime CreatedAt,
    /// <summary>关联用例数（不含已软删除的用例，由 TestCase 的全局查询过滤器保证）</summary>
    int CaseCount,
    /// <summary>关联用例中「最近一次执行已通过」的用例数——需求的"验证进度"</summary>
    int PassedCaseCount);

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
    string? ExternalKey = null, string? Priority = null);

public record UpdateRequirementRequest(
    string Title, string? Description = null,
    string? ExternalKey = null, string? Priority = null);
