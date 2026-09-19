namespace AI.TestPlatform.Domain.Entities;

/// <summary>评论挂载的实体类型。通用多态挂载（类型 + Id），新增可评论对象只需扩枚举</summary>
public enum CommentTarget { TestCase, Defect, TestPlan }

/// <summary>
/// 通用评论。用例评审、缺陷讨论、计划备注共用一张表——
/// 结构完全同构（作者 + 正文 + 时间），分表只会复制三份一模一样的代码。
/// 审批/评审流转（状态机）不在这里：那是挂靠对象自身的语义，等口径确认后单独做。
/// </summary>
public class Comment
{
    public Guid Id { get; set; }

    public CommentTarget Target { get; set; }

    /// <summary>挂载对象的 Id</summary>
    public Guid TargetId { get; set; }

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    /// <summary>正文（纯文本，≤2000 字；@用户名 由前端解析成提醒链接）</summary>
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
