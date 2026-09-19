namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 审计日志（迭代 C）：记「谁在什么时候做了什么」，以及**这次操作的请求内容与响应结果**。
///
/// 请求/响应都只留**摘要**（各自有长度上限，超长截断并标记），不存完整业务数据快照；
/// 落库前统一脱敏，密码、令牌等敏感字段替换为 ***。
///
/// 落库时机刻意放在**业务操作之后**，且使用独立 DI 作用域：
/// 业务失败时也要留痕（例如「删除了一个不存在的项目」这类探测行为同样有价值），
/// 因此绝不能让审计写入与业务事务同生共死。
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ---- 操作者（未登录操作者仅在登录接口出现，其余均为已登录用户）
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    /// <summary>操作时的角色快照（用户后续改角色不影响历史记录的可读性）</summary>
    public string? UserRole { get; set; }

    // ---- 动作
    /// <summary>动作类型，如 Create / Update / Delete / Execute / Login / ResetPassword</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>资源类型，如 Project / TestCase / Execution / User</summary>
    public string ResourceType { get; set; } = string.Empty;
    /// <summary>资源主键（字符串，兼容非 Guid 标识）</summary>
    public string? ResourceId { get; set; }
    /// <summary>资源可读名称（如项目名、用例名），便于不查业务表就能读懂日志</summary>
    public string? ResourceName { get; set; }

    /// <summary>HTTP 方法与路径</summary>
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool Succeeded { get; set; }

    /// <summary>变更摘要（脱敏后，敏感字段替换为 ***）</summary>
    public string? Detail { get; set; }

    /// <summary>
    /// 响应结果摘要（脱敏后）。
    ///
    /// 与 <see cref="Detail"/> 相对：Detail 是"传进去什么"，这里是"返回了什么"。
    /// 排查"用户说保存失败但日志显示 200"这类问题时，只有请求内容是不够的——
    /// 必须能看到当时返回的报文。超长会截断并追加标记。
    ///
    /// 不采集的情形：非 JSON 响应（文件下载、导出）、以及端点上显式关掉的。
    /// </summary>
    public string? ResponseBody { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
