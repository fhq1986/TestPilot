namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 一次 k6 压测运行（迭代 F·P2-9）。
///
/// 与 <see cref="Execution"/> 同构的部分（ClaimedBy / HeartbeatAt / 抢占队列）刻意保持一致，
/// 这样 <c>LoadTestWorker</c> 可以直接照抄 <c>ExecutionWorker</c> 的抢占与僵尸回收逻辑。
///
/// 指标用**冗余标量列**而不是只存原始 summary：列表页与趋势图不能逐行反序列化 jsonb。
/// 原始 summary（可达上百 KB、无需按字段查询）走对象存储，与 trace/录像同一口径。
/// </summary>
public class LoadTestRun
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public LoadTestScenario? Scenario { get; set; }

    /// <summary>
    /// 冗余的项目 Id。Executions 当初没冗余这一列，导致按项目统计只能 join TestCases
    /// （见 docs/performance-test-plan.md 的吐槽）；这里从一开始就冗余，
    /// 让项目作用域反查与统计都是一次索引命中。
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 复用 <see cref="ExecutionStatus"/>：Pending/Running/Passed/Failed/Error/Canceled。
    /// 语义完全吻合——Passed=阈值全过，Failed=阈值未过，Error=k6 崩溃或超时。
    /// </summary>
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;

    public TriggerType TriggerType { get; set; } = TriggerType.Manual;
    public Guid? TriggeredById { get; set; }
    public User? TriggeredBy { get; set; }

    /// <summary>执行器实例标识（"{NodeName}:{Pid}"），多实例并行时判断任务归属</summary>
    public string? ClaimedBy { get; set; }
    public DateTime? HeartbeatAt { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationMs { get; set; }

    /// <summary>本次运行的靶站地址快照（环境/场景后续被改也不影响历史解读）</summary>
    public string TargetBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 本次运行的超时（秒）= 场景时长 + 余量，**入队时就快照**。
    /// 不快照的话，场景时长被改大后，一条早已入队的运行会突然获得更长的超时（反之则被提前杀掉）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>本次运行的脚本哈希与对象存储 key（脚本可能已被重新生成，历史运行要可复现）</summary>
    public string? ScriptHash { get; set; }
    public string? ScriptArtifactKey { get; set; }
    /// <summary>k6 summary JSON 的对象存储 key</summary>
    public string? SummaryArtifactKey { get; set; }
    /// <summary>k6 stdout/stderr 的对象存储 key</summary>
    public string? LogArtifactKey { get; set; }

    /// <summary>k6 版本。summary 结构在版本间会漂移，出问题时靠它定位</summary>
    public string? K6Version { get; set; }
    /// <summary>k6 退出码：0=全过，99=阈值未过，其他=运行错误</summary>
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }

    // ------------------------------ 指标冗余列（来源见 K6SummaryParser）
    public long TotalRequests { get; set; }
    /// <summary>每秒请求数（http_reqs.rate）</summary>
    public double? Rps { get; set; }
    public double? AvgMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    /// <summary>p99。仅当脚本的 summaryTrendStats 声明了 p(99) 才有值，否则为 null（前端显示"未采集"）</summary>
    public double? P99Ms { get; set; }
    public double? MaxMs { get; set; }
    /// <summary>失败率（http_req_failed.rate）：非 2xx + 网络错误</summary>
    public double? ErrorRate { get; set; }
    /// <summary>检查通过率（checks.rate）</summary>
    public double? ChecksRate { get; set; }
    public long Iterations { get; set; }
    public int? VusMax { get; set; }

    /// <summary>阈值结论。null 表示本次没配阈值</summary>
    public bool? ThresholdsPassed { get; set; }
    public int ThresholdTotal { get; set; }
    public int ThresholdFailed { get; set; }
    /// <summary>每条阈值的通过情况（条目少、只整体展示，故 jsonb 即可）</summary>
    public List<LoadTestThresholdResult> ThresholdResults { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>单条阈值的实测结论（来自 k6 summary 里各 metric 的 thresholds 字段）</summary>
public class LoadTestThresholdResult
{
    /// <summary>k6 指标名</summary>
    public string Metric { get; set; } = string.Empty;
    /// <summary>渲染回 k6 原生表达式，如 <c>p(95)&lt;500</c></summary>
    public string Expression { get; set; } = string.Empty;
    public bool Ok { get; set; }
}
