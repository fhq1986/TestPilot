namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 定时任务：按 Cron 表达式周期性执行一批用例（夜间回归、每日冒烟等）。
/// 执行范围依次收敛：指定用例 &gt; 模块 + 优先级；都为空表示项目下全部用例。
/// </summary>
public class Schedule
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>标准 5 段 Cron（分 时 日 月 周），如「0 2 * * *」表示每天 02:00</summary>
    public string CronExpression { get; set; } = "0 2 * * *";

    public bool Enabled { get; set; } = true;

    /// <summary>执行范围类型。默认按用例（老数据行为不变）</summary>
    public ScheduleScopeKind ScopeKind { get; set; } = ScheduleScopeKind.Cases;

    /// <summary>
    /// 范围 = 测试计划时的计划 ID 列表。
    ///
    /// 用列表而不是单个外键：与 <see cref="TestCaseIds"/> 同构，且一次解决两个方向的多样性——
    /// 「一个定时任务驱动多个计划」与「一个计划被多个定时任务驱动」都能表达。
    /// （对比：原来的 TestPlan.ScheduleId 是单个外键，一个计划只能绑一个定时任务，
    /// 「每晚跑一遍 + 发版前再跑一遍」就表达不了。）
    /// </summary>
    public List<Guid>? TestPlanIds { get; set; }

    // ------------------------------ 执行范围
    public string? Module { get; set; }
    public string? Priority { get; set; }
    public List<Guid>? TestCaseIds { get; set; }

    public Guid? EnvironmentId { get; set; }
    public Environment? Environment { get; set; }

    /// <summary>浏览器矩阵（chromium / firefox / webkit）；为空表示按用例/环境配置解析</summary>
    public List<string>? Browsers { get; set; }

    /// <summary>用例绑定数据集时是否按数据行展开执行</summary>
    public bool ExpandDataSets { get; set; } = true;

    // ------------------------------ 运行状态
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int LastCreatedCount { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
