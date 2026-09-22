namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 压测场景（迭代 F·P2-9，k6）。
///
/// 刻意**独立于功能执行流水线**：压测的语义（虚拟用户、时长、阶段爬坡、阈值）
/// 与功能用例（步骤、断言、失败诊断、录像）没有交集，硬塞进 Execution/ExecutionResult
/// 会让两边都变形——功能执行的报告要算通过率，压测要算 p95/RPS，口径根本不同。
///
/// 脚本是「选择集 + 负载配置」的纯函数产物，因此**不单独建版本表**：
/// 场景表存当前脚本，每次运行时把脚本冻结一份到对象存储，保证历史运行可复现。
/// </summary>
public class LoadTestScenario
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>用例来源：已有接口用例 / 导入的 OpenAPI 操作</summary>
    public LoadTestSource Source { get; set; } = LoadTestSource.Cases;

    /// <summary>目标环境（取 BaseUrl）。与 TargetBaseUrl 二者取其一，后者优先</summary>
    public Guid? EnvironmentId { get; set; }
    public Environment? Environment { get; set; }

    /// <summary>显式覆盖靶站地址；为空则用环境的 BaseUrl</summary>
    public string? TargetBaseUrl { get; set; }

    /// <summary>Source=OpenApi 时指向已导入的 API 文档（Spec 原文在这里取）</summary>
    public Guid? ApiDefinitionId { get; set; }
    public ApiDefinition? ApiDefinition { get; set; }

    /// <summary>
    /// Source=OpenApi 时选中的操作，形如 <c>"GET /pets"</c>。
    /// 存 jsonb 而不是子表：这些路径是**文档里的字符串**，没有实体身份、也不需要反向引用，
    /// 建表只会多一个 join 和一套级联规则。
    /// </summary>
    public List<string> Operations { get; set; } = new();

    /// <summary>负载配置（k6 executor / VUs / 阶段爬坡），jsonb</summary>
    public LoadTestProfile Profile { get; set; } = new();

    /// <summary>阈值（如 p(95)&lt;500），jsonb。结构化存储以便 UI 用下拉框编辑、后端做白名单校验</summary>
    public List<LoadTestThreshold> Thresholds { get; set; } = new();

    /// <summary>脚本里 {{变量}} 的取值（jsonb），与 Execution.Variables 同一套写法</summary>
    public Dictionary<string, string>? Variables { get; set; }

    // 冗余列：列表页要显示「20 VU / 2 分钟」，且 POST /run 要做上限校验。
    // 只靠 jsonb 的话这两件事都得把每行反序列化一遍。
    /// <summary>constant-vus 下的并发数（ramping 时取 stages 的峰值，仅供展示）</summary>
    public int VirtualUsers { get; set; }
    /// <summary>预计运行时长（秒），用于超时上限校验</summary>
    public int DurationSeconds { get; set; }

    /// <summary>最近一次生成的 k6 脚本（详情页直接展示）</summary>
    public string? ScriptText { get; set; }
    /// <summary>脚本内容哈希：选择集/负载改了但脚本没重生成时，用它提示用户</summary>
    public string? ScriptHash { get; set; }
    public DateTime? ScriptGeneratedAt { get; set; }

    // 审计字段（AuditStampInterceptor 按属性名约定自动盖章）
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<LoadTestScenarioCase> Cases { get; set; } = new();
    public List<LoadTestRun> Runs { get; set; } = new();
}

/// <summary>
/// k6 负载配置。
///
/// 三种 executor 的参数互斥（constant-vus 用 Vus、constant-arrival-rate 用 Rate、
/// ramping-vus 用 Stages），列化会产出一片恒为 NULL 的列，故整体存 jsonb；
/// 真正需要 SQL 的两个值（并发数、时长）在上面的实体上冗余了。
/// </summary>
public class LoadTestProfile
{
    /// <summary>k6 executor：ramping-vus / constant-vus / constant-arrival-rate</summary>
    public string Kind { get; set; } = "ramping-vus";

    /// <summary>constant-vus：并发虚拟用户数</summary>
    public int Vus { get; set; } = 10;

    /// <summary>constant-arrival-rate：每 TimeUnit 发起的迭代数</summary>
    public int Rate { get; set; } = 10;
    /// <summary>constant-arrival-rate：Rate 的时间单位（如 1s / 1m）</summary>
    public string TimeUnit { get; set; } = "1s";
    /// <summary>constant-arrival-rate：预分配的 VU 数</summary>
    public int PreAllocatedVUs { get; set; } = 20;
    /// <summary>constant-arrival-rate：VU 上限</summary>
    public int MaxVUs { get; set; } = 50;

    /// <summary>ramping-vus：阶段爬坡（每段一个目标 VU 数）</summary>
    public List<LoadTestStage> Stages { get; set; } = new();

    /// <summary>constant-vus：运行时长（k6 duration 字面量，如 1m / 30s）</summary>
    public string Duration { get; set; } = "1m";

    /// <summary>ramping-vus：结束时优雅降档时长</summary>
    public string GracefulRampDown { get; set; } = "10s";

    /// <summary>每轮迭代末尾的思考时间（秒）。0 表示不加 sleep</summary>
    public decimal ThinkTimeSeconds { get; set; }
}

/// <summary>ramping-vus 的一个阶段</summary>
public class LoadTestStage
{
    /// <summary>本段时长（k6 duration 字面量）</summary>
    public string Duration { get; set; } = "30s";
    /// <summary>本段结束时的目标 VU 数</summary>
    public int Target { get; set; }
}

/// <summary>
/// 阈值条目。
///
/// 刻意**结构化**而不是存 k6 原生表达式（<c>rate&lt;0.01 &amp;&amp; p(95)&lt;500</c>）：
/// 原生表达式是任意 JS，无法安全校验，前端也只能给个文本框让人手写；
/// 结构化后前端可以用下拉框、后端可以对 metric 名做白名单，再渲染回表达式字符串。
/// </summary>
public class LoadTestThreshold
{
    /// <summary>k6 指标名（白名单校验，如 http_req_duration / http_req_failed / checks）</summary>
    public string Metric { get; set; } = "http_req_duration";
    /// <summary>聚合方式：p(90) / p(95) / p(99) / rate / avg / max / count</summary>
    public string Aggregator { get; set; } = "p(95)";
    /// <summary>比较符：&lt; / &lt;= / &gt; / &gt;=</summary>
    public string Operator { get; set; } = "<";
    public decimal Value { get; set; } = 500;
}
