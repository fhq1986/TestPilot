namespace AI.TestPlatform.Application.Projects;

public record CreateProjectRequest(
    string Name, string? Description,
    Guid? ManagerId = null, Guid? TestOwnerId = null, Guid? DeveloperOwnerId = null);

public record UpdateProjectRequest(
    string Name, string? Description,
    Guid? ManagerId = null, Guid? TestOwnerId = null, Guid? DeveloperOwnerId = null,
    // M8 Agent 自愈（项目级开关；系统级总开关在系统配置）
    bool? AgentLoopEnabled = null, bool? TreatAgentHealedAsPass = null);

/// <summary>
/// 项目视图。
///
/// 负责人同时回带**姓名**而不是只给 Id：列表页要直接显示，
/// 只给 Id 的话前端得为每一行再查一次用户，一页 20 行就是 20 次请求（典型的 N+1）。
/// TestOwnerEmail 一并带出来，是为了让界面能看出「验收邮件到底能不能发出去」——
/// 定了测试负责人却没填邮箱，是最容易踩的静默失效。
/// </summary>
public record ProjectDto(
    Guid Id, string Name, string? Description, Guid CreatedById,
    DateTime CreatedAt, DateTime UpdatedAt, int TestCaseCount,
    Guid? ManagerId = null, string? ManagerName = null,
    Guid? TestOwnerId = null, string? TestOwnerName = null,
    string? TestOwnerEmail = null,
    /// <summary>开发负责人：对缺陷修复负责（测试负责人对用例质量负责）</summary>
    Guid? DeveloperOwnerId = null, string? DeveloperOwnerName = null,
    /// <summary>M8 Agent 自愈：项目级开关（须与系统级总开关同时开启才生效）</summary>
    bool AgentLoopEnabled = false,
    /// <summary>M8 Agent 自愈：自愈"通过"是否计入达标判定（默认否）</summary>
    bool TreatAgentHealedAsPass = false,
    /// <summary>创建人显示名（M8 审计字段）</summary>
    string? CreatedByName = null);
