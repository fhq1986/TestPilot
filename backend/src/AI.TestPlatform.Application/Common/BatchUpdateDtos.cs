using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Common;

/// <summary>
/// 批量编辑请求：字段白名单（模块/优先级/状态），null 表示该项不改。
/// 白名单刻意收窄——批量改 BaseUrl/浏览器这类执行要素影响面大，逐条改更稳妥。
/// </summary>
public record BatchUpdateRequest(
    List<Guid> Ids,
    string? Module = null,
    string? Priority = null,
    TestCaseStatus? Status = null,
    /// <summary>迁移目标项目。单条编辑不支持改项目，批量场景（用例建错项目）才需要</summary>
    Guid? ProjectId = null,
    /// <summary>关联需求。必须属于目标项目（显式传入的 ProjectId 或用例当前项目）</summary>
    Guid? RequirementId = null);

public record BatchUpdateSkippedItem(Guid Id, string? Name, string Reason);

public record BatchUpdateResult(int Updated, IReadOnlyList<BatchUpdateSkippedItem> Skipped);
