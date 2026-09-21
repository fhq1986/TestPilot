namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 站内消息分类。用于消息中心的筛选与「不想收某类」的偏好开关，
/// 与业务模块一一对应，新增事件时先归到已有分类，确实放不下再加。
/// </summary>
public enum NotificationCategory
{
    /// <summary>系统类兜底（维护任务、平台公告等）</summary>
    System = 0,
    /// <summary>执行完成 / 失败 / 被终止</summary>
    Execution = 1,
    /// <summary>缺陷指派与状态流转</summary>
    Defect = 2,
    /// <summary>用例评审提交与结论</summary>
    Review = 3,
    /// <summary>测试计划轮次结束</summary>
    Plan = 4,
    /// <summary>定时任务执行失败</summary>
    Schedule = 5,
    /// <summary>评论 / @提及</summary>
    Comment = 6,
    /// <summary>批量导入结果</summary>
    Import = 7,
    /// <summary>视觉基线变更待确认</summary>
    Visual = 8,
}

/// <summary>消息级别：只影响图标与配色，不参与权限判断</summary>
public enum NotificationLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// 站内消息（消息中心）。
///
/// **与 NotificationService 的分工**：
/// 那套是「对外推送」（企微/钉钉/飞书/邮件），目的是把人叫走；
/// 这套是「站内记录」，目的是人回到平台时能看到期间发生过什么。
/// 因此站内消息**不受 NotifyEnabled 总开关约束**——总开关是防打扰的闸门，
/// 而记录本身不该被关掉；两者各自独立，同一条业务事件可以只落站内不发外部渠道。
///
/// **一条消息一人一行**：本项目是单团队规模，已读状态天然随行隔离，
/// 标记已读就是一次 UPDATE，不必 join 收件人表。代价是广播型消息要写多行，
/// 量级完全可接受；将来真要广播给几百人，再拆「消息表 + 收件人表」。
///
/// 只增不改，没有软删：消息没有「恢复」语义，过期由维护任务硬删（见 NotificationRetentionService）。
/// </summary>
public class InAppNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>接收人。消息只能发给本来就有权看该资源的人，见 InAppNotificationService 的接收人解析</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public NotificationCategory Category { get; set; }
    public NotificationLevel Level { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>一行摘要，放关键数字（通过率、失败数、流转结论等）</summary>
    public string? Body { get; set; }

    /// <summary>前端路由（如 /defects/{id}）。点消息直达，权限由目标页面自己的守卫兜底</summary>
    public string? LinkUrl { get; set; }
    /// <summary>跳转按钮文案（如「查看缺陷」）；为空时前端用「查看详情」</summary>
    public string? LinkLabel { get; set; }

    /// <summary>关联实体类型（Defect / TestCase / Execution / TestPlan / Schedule），用于排查与将来的聚合</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }

    /// <summary>
    /// 反规范化的项目 Id。消息是高频查询对象（每次打开消息中心都拉），
    /// 按 SourceType/SourceId 去各表 JOIN 反查项目代价太高，因此在写入时直接带上。
    /// 系统类消息（没有具体项目归属）可为 null。
    /// </summary>
    public Guid? ProjectId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
