namespace AI.TestPlatform.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱。用于接收「测试计划轮次完成」的验收结果邮件。
    /// 刻意**不做唯一约束**：同一个人完全可能用同一个邮箱开两个账号（如 test01/test02 共用组邮箱），
    /// 强制唯一会把这种合理用法挡在门外，而对发信本身没有任何好处。
    /// </summary>
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ------------------------------ 迭代 C：用户管理
    /// <summary>角色（默认 Viewer；迁移回填把既有用户置为 Admin，与升级前行为等价）</summary>
    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>是否启用：停用后无法登录，且已签发的 token 立即失效</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 会话版本号。登录时写入 JWT，之后每次请求与库中值比对；
    /// 改角色 / 重置密码 / 启停用时自增，使旧 token 立刻失效（无需维护黑名单）。
    /// </summary>
    public int TokenVersion { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    /// <summary>创建人（种子管理员为 null）</summary>
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    // ------------------------------ SSO 扫码登录
    /// <summary>
    /// 绑定的企业身份源标识（wecom / dingtalk / mock）。null 表示未绑定，只能密码登录。
    /// 与 <see cref="SsoSubject"/> 组成唯一索引；都是可空列，不影响纯密码账号。
    /// </summary>
    public string? SsoProvider { get; set; }

    /// <summary>企业侧的稳定唯一标识（企微 userid / 钉钉 unionId）</summary>
    public string? SsoSubject { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
