namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 共享步骤组（迭代 C）。
///
/// 解决的问题：登录、初始化测试数据这类步骤在几十个用例里重复出现，
/// 站点一改就要把所有用例改一遍。抽成共享步骤组后，用例通过
/// <see cref="TestStep.SharedGroupId"/> 引用它，一处修改全部生效。
///
/// 展开时机放在**运行时**（TestRunner 解析步骤时）而不是保存时：
/// 保存时展开会把副本固化进每个用例，等于没复用；运行时展开才能做到改一处全生效。
/// </summary>
public class SharedStepGroup
{
    /// <summary>显式生成主键（同 RecorderSession：创建后立刻要用于拼装，不依赖 EF 的值生成器）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>组内步骤（按 StepOrder 排序）</summary>
    public List<SharedStepItem> Items { get; set; } = new();

    /// <summary>
    /// 组内变量默认值。引用方可以逐项覆盖；未被覆盖时用这里的默认值，
    /// 于是「登录账号」这类参数可以有一个合理的默认，同时保留按用例定制的余地。
    /// </summary>
    public List<SharedVariableEntry> Variables { get; set; } = new();

    public Guid? CreatedById { get; set; }

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>最后修改人（创建人见 CreatedById）</summary>
    public Guid? UpdatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>共享步骤组内的一条步骤（结构与 TestStep 对齐，展开后可直接变成 TestStep）</summary>
public class SharedStepItem
{
    /// <summary>
    /// 刻意**不**预设主键。
    ///
    /// 编辑已有共享步骤组时，这些 item 是挂到**已跟踪的** group 的集合上的
    /// （<c>group.Items.Clear()</c> 后再 Add）。若预置主键，EF 会以为「这行已存在」
    /// 而发出 UPDATE，影响 0 行后抛 DbUpdateConcurrencyException——
    /// 这个坑在计划模块的范围编辑里已经踩过一次（见 TestPlanItem 的注释）。
    /// </summary>
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }
    public SharedStepGroup Group { get; set; } = null!;

    public int StepOrder { get; set; }
    public ActionType ActionType { get; set; }
    public StepConfig Config { get; set; } = new();
    public string? AIInstruction { get; set; }
    public string? AIElementDescription { get; set; }
}

/// <summary>
/// 共享步骤变量。用 List 而不是 Dictionary：
/// EF Core 的 jsonb 自有类型映射不支持 Dictionary 属性（写入时 NRE），
/// 与 <see cref="StepConfig.Headers"/> 采用 List&lt;HeaderEntry&gt; 是同一个原因。
/// </summary>
public class SharedVariableEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
