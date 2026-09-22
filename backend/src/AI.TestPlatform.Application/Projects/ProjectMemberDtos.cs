using AI.TestPlatform.Domain.Auth;

namespace AI.TestPlatform.Application.Projects;

/// <summary>项目成员视图（带用户名/显示名，避免前端逐行再查用户）。</summary>
public record ProjectMemberDto(
    Guid Id, Guid UserId, string Username, string? DisplayName,
    ProjectRole Role, DateTime CreatedAt);

/// <summary>加成员请求。</summary>
public record AddProjectMemberRequest(Guid UserId, ProjectRole Role);

/// <summary>改成员角色请求。</summary>
public record UpdateProjectMemberRoleRequest(ProjectRole Role);

/// <summary>成员候选（供项目 Owner 搜人加成员；只暴露最小字段，不经过用户管理接口）。</summary>
public record UserCandidateDto(Guid Id, string Username, string? DisplayName);
