namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 压测执行参数（配置节：appsettings.json 的 LoadTest，迭代 F·P2-9）。
///
/// 这里的上限**不是建议而是硬约束**：部署机只有 4 vCPU / 3.7GB，与
/// postgres/minio/redis/aiworker 同机。压测是唯一一个"能把整台机器打满"的功能，
/// 所以默认关闭（<see cref="Enabled"/> 在 compose 里强制 false），
/// 且 VUs / 时长 / 并发三处都在入队前就拦。
/// </summary>
public class LoadTestOptions
{
    /// <summary>
    /// 是否启用压测执行。appsettings 默认 true（本地开发即用），
    /// compose 里用 <c>LoadTest__Enabled=false</c> 强制关闭，避免默认多吃资源。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>k6 可执行文件路径（容器内是 /usr/local/bin/k6）</summary>
    public string K6Path { get; set; } = "k6";

    /// <summary>工作目录（每次运行在这里生成独立子目录放脚本与输出），相对 ContentRoot</summary>
    public string WorkPath { get; set; } = "loadtests";

    /// <summary>从数据库抢占待运行记录的轮询间隔（秒）</summary>
    public int PollSeconds { get; set; } = 3;

    /// <summary>心跳写入间隔（秒）</summary>
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>心跳超过该分钟数仍为 Running 的运行视为僵死（执行器重启会带走 k6 子进程）</summary>
    public int StaleRunningMinutes { get; set; } = 5;

    /// <summary>同时运行的压测数。默认 1——压测并发跑两个会让两者的数字都不可解释</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>单次运行允许的最大虚拟用户数（入队校验）</summary>
    public int MaxVus { get; set; } = 50;

    /// <summary>单次运行允许的最大时长（秒，入队校验）</summary>
    public int MaxDurationSeconds { get; set; } = 600;

    /// <summary>超时余量（秒）：实际超时 = 场景时长 + 本值（留出 k6 启动/收尾时间）</summary>
    public int OverheadSeconds { get; set; } = 60;

    /// <summary>k6 日志保留上限（字节，超出丢头部保尾部）</summary>
    public int MaxLogBytes { get; set; } = 262_144;

    /// <summary>运行产物（脚本/summary/日志）保留天数，由维护任务清理</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 是否用 nice 降优先级（Linux）。压测与平台同机抢 CPU，降优先级能少影响正常请求；
    /// Windows 本地开发没有 nice，需置 false。
    /// </summary>
    public bool UseNice { get; set; } = true;

    /// <summary>实际并发度（至少 1，硬上限 2）</summary>
    public int EffectiveConcurrency => Math.Clamp(MaxConcurrency, 1, 2);
}
