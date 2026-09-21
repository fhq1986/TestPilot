namespace AI.TestPlatform.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    /// <summary>项目负责人（业务负责人）</summary>
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }

    /// <summary>
    /// 测试负责人。测试计划轮次完成后的验收结果邮件默认发到这个人，
    /// 因此这个字段是「谁对这批用例的质量负责」的落点，而不是又一个联系人字段。
    /// </summary>
    public Guid? TestOwnerId { get; set; }
    public User? TestOwner { get; set; }

    /// <summary>
    /// 开发负责人。与测试负责人相对：**测试负责人对"用例质量"负责（验收结果发给谁），
    /// 开发负责人对"缺陷修复"负责**（谁来回这批缺陷）。
    ///
    /// 说明：目前它只是**登记 + 展示**，还没有任何自动化行为挂在上面
    /// （比如"新缺陷默认指派给他"）。加字段时先不假装它有行为，
    /// 免得又变成一个"声明了但没人用"的字段——本项目已经有过三个。
    /// </summary>
    public Guid? DeveloperOwnerId { get; set; }
    public User? DeveloperOwner { get; set; }

    // ---------------- M8 Agent 自愈闭环（项目级开关，系统级总开关在 SystemConfig）
    /// <summary>是否对该项目启用 Agent 失败自愈闭环。默认关；须与系统级总开关同时开启才生效</summary>
    public bool AgentLoopEnabled { get; set; }

    /// <summary>Agent 自愈熔断冷却时间：连续多次自愈仍失败后由 AgentHealCircuitBreaker 置位，冷却期内不再自愈</summary>
    public DateTime? AgentLoopSuspendedAt { get; set; }

    /// <summary>
    /// Agent 自愈"通过"是否计入达标判定。默认 false——避免"改松断言/删步骤即通过"污染验收质量数据。
    /// 为 true 时才把 AgentHealed 的执行算作达标通过。
    /// </summary>
    public bool TreatAgentHealedAsPass { get; set; }

    // ------------------------------ 审计字段（由 TestDbContext 统一盖章）
    /// <summary>最后修改人（创建人见 CreatedById）</summary>
    public Guid? UpdatedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<TestCase> TestCases { get; set; } = new();
    public List<Environment> Environments { get; set; } = new();
}
