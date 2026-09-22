using AI.TestPlatform.Domain.Auth;

namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 项目成员（迭代 E·① 引入）：把「用户 ↔ 项目」的归属与项目内角色落库。
///
/// 设计要点：
/// - 与全局 <see cref="UserRole"/> 正交——全局角色是平台级，项目角色是项目级。
/// - <see cref="ProjectId"/> + <see cref="UserId"/> 唯一索引：同一用户在同一个项目只能有一个角色。
/// - 删除项目级联删除其成员；删除用户级联删除其成员关系。
/// - 授权为「成员激活式」：项目**没有任何成员**时退化为全局角色（兼容历史项目与既有行为），
///   一旦有人被加入成员，该项目即进入「仅成员可见/可操作」模式。
/// </summary>
public class ProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ProjectRole Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>把该用户加为成员的操作人（审计用，可空）</summary>
    public Guid? CreatedById { get; set; }
}
