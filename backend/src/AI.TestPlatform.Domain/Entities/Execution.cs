namespace AI.TestPlatform.Domain.Entities;

public class Execution
{
    public Guid Id { get; set; }
    public Guid? TestCaseId { get; set; }
    public TestCase? TestCase { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public Guid? TriggeredById { get; set; }
    public User? TriggeredBy { get; set; }
    public TriggerType TriggerType { get; set; }

    // CI 上下文（来自 CI/CD 触发，便于把结果与代码变更关联）
    public string? CommitSha { get; set; }
    public string? Branch { get; set; }
    public string? BuildNumber { get; set; }
    // 触发来源描述（如 jenkins / gitlab / 手动 / 定时任务名）
    public string? TriggerSource { get; set; }

    // 执行器实例标识与心跳：多实例并行时用于判断任务是否仍被某个实例持有
    public string? ClaimedBy { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    /// <summary>实际使用的浏览器引擎（chromium / firefox / webkit）</summary>
    public string? BrowserName { get; set; }
    public string? BrowserVersion { get; set; }

    // 数据驱动：本次执行取用的数据行（用例绑定数据集后，一行数据一条执行记录）
    /// <summary>数据集行号（从 0 开始）；为空表示未使用数据集</summary>
    public int? DataSetRowIndex { get; set; }
    /// <summary>数据行的可读标签（如「username=alice, password=bad」），便于列表区分</summary>
    public string? DataSetRowLabel { get; set; }
    /// <summary>本次执行的变量覆盖（优先于数据集行），来自手动/CI 传入</summary>
    public Dictionary<string, string>? Variables { get; set; }

    // 套件运行归集
    public Guid? SuiteId { get; set; }
    /// <summary>一次套件运行的批次 ID，用于把本次运行的多个执行聚合成一份报告</summary>
    public Guid? SuiteRunId { get; set; }

    // 执行编排（套件内依赖与失败策略）
    /// <summary>
    /// 前置用例：本条执行要等「同一次套件运行内」的这条用例全部通过后才可被抢占。
    /// 计划期从 TestSuiteCase 复制过来——抢占门禁是一条 SQL，按 (SuiteRunId, TestCaseId)
    /// 直接查比每次去 join 套件成员更省事，也让执行详情能显示「它在等谁」。
    /// </summary>
    public Guid? DependsOnTestCaseId { get; set; }

    /// <summary>
    /// 编排导致的跳过原因（前置未通过 / 套件失败快停）。为空表示不是编排跳过的
    /// （例如用例在计划期就被判为不支持执行）。前端在状态标签上直接给出原因，
    /// 否则「跳过」没有解释，看起来像脏数据。
    /// </summary>
    public string? SkipReason { get; set; }

    // 测试计划归集（与套件是**并列**关系而非嵌套：同一个执行可能既属于某次套件运行，
    // 又被某个计划轮次纳入——计划范围从套件导入时就是这种情况）
    public Guid? PlanId { get; set; }
    /// <summary>计划轮次 ID，轮次的通过率由该批次下的执行实时聚合得出</summary>
    public Guid? PlanRoundId { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationMs { get; set; }
    /// <summary>
    /// Playwright trace 压缩包的对外 URL（迭代 D）。仅在失败时保留时非空——
    /// 可用 Playwright trace viewer 回放每步 DOM 快照、网络请求与截图，
    /// 是「失败能不能查得动」的关键，接口类失败尤其依赖它。
    /// </summary>
    public string? TraceUrl { get; set; }
    /// <summary>trace 文件大小（字节），列表页可提示下载体积</summary>
    public long? TraceSizeBytes { get; set; }

    /// <summary>
    /// 执行录像（webm）。与 trace 同样**默认只在失败时保留**。
    ///
    /// 与 trace 的分工：trace 适合查元素与网络（结构化回放），
    /// 而"动画未完就点了""焦点跳走了""滚动把元素挡住了"这类**时序问题**，
    /// 截图看不出来、trace 要一步步点着看，录像一眼就明白。
    /// </summary>
    public string? VideoUrl { get; set; }
    /// <summary>录像大小（字节）</summary>
    public long? VideoSizeBytes { get; set; }
    // AI 诊断结果
    public string? AIDiagnosis { get; set; }
    public string? AISuggestedFix { get; set; }
    public float? DiagnosisConfidence { get; set; }

    // ---------------- M8 Agent 自愈闭环（见 docs/m8-agent-design.md）
    /// <summary>本次执行触发的修复合计尝试次数</summary>
    public int AgentLoopCount { get; set; }
    /// <summary>是否由 Agent 自愈后通过（用于统计口径区分「原生通过」与「自愈通过」）</summary>
    public bool AgentHealed { get; set; }
    /// <summary>本次执行消耗的 LLM 调用数</summary>
    public int AgentBudgetUsed { get; set; }
    /// <summary>整轮 Agent Loop 的执行级最终结论（尝试级结论在 AgentAttempt.Result）</summary>
    public AgentAttemptResult? AgentFinalVerdict { get; set; }

    /// <summary>本次执行中步骤内部重试消耗的总次数（RetryCount/flaky 隔离产生）；>0 说明有靠重试才稳住的步骤</summary>
    public int StepRetryCount { get; set; }

    /// <summary>
    /// 本次执行要跑的步骤总数（共享步骤组展开后、不含自动登录前置）。
    /// 抢占后解析步骤时快照——执行详情页的「共 x 步，当前正执行第 x 步」进度指示以此为分母，
    /// 用例步骤在执行期间被编辑也不影响本次执行的口径。
    /// </summary>
    public int? TotalSteps { get; set; }

    public Guid? EnvironmentId { get; set; }
    public Environment? Environment { get; set; }
    public EnvironmentSnapshot? EnvironmentSnapshot { get; set; }
    public List<ExecutionResult> Results { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
