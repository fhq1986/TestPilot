namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 执行引擎可调参数（配置节：appsettings.json 的 Execution）
/// </summary>
public class ExecutionOptions
{
    /// <summary>
    /// 是否对「非 AI 选择器 + 有元素描述」的步骤启用快速探测。
    /// 开启后先用较短超时探测选择器是否存在，未命中立即转 AI 自愈，
    /// 避免用整步超时（默认 30s）去等一个注定命不中的选择器。
    /// </summary>
    public bool SelectorProbeEnabled { get; set; } = true;

    /// <summary>快速探测超时（毫秒），仅在 SelectorProbeEnabled 为 true 时生效。</summary>
    public int SelectorProbeTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// 自愈定位（缓存/LLM）成功后执行动作的最小超时（毫秒）。
    /// 用于给元素渲染留出余量，避免刚定位到就因瞬时状态失败。
    /// </summary>
    public int HealedActionMinTimeoutMs { get; set; } = 3000;

    // ------------------------------ 并行执行 / 水平扩展
    /// <summary>同一实例内并行执行的最大数量（0 表示按 CPU 自动）</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// 本执行节点名。分布式部署时给每个节点起不同名字（如 node-sh-1），
    /// 节点看板与 Execution.ClaimedBy 前缀都依赖它；留空则用机器名。
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>从数据库抢占待执行记录的轮询间隔（秒），多实例共享队列时靠它发现新任务</summary>
    public int PollSeconds { get; set; } = 5;

    /// <summary>心跳写入间隔（秒）</summary>
    public int HeartbeatSeconds { get; set; } = 20;

    /// <summary>心跳超过该分钟数仍为 Running 的执行视为僵死（进程崩溃），启动时标记为 Error</summary>
    public int StaleRunningMinutes { get; set; } = 5;

    /// <summary>实际并发度：配置为 0/负数时按 CPU 核数的一半（至少 2）</summary>
    public int EffectiveConcurrency =>
        MaxConcurrency > 0 ? MaxConcurrency : Math.Max(2, Environment.ProcessorCount / 2);

    // ------------------------------ 浏览器池（迭代 D）
    /// <summary>
    /// 浏览器实例空闲多少分钟后关闭释放内存（0 表示不回收，常驻到进程退出）。
    /// 浏览器进程是常驻的，一个 Chromium 约 200MB；夜间回归跑完后不该一直占着。
    /// </summary>
    public int BrowserIdleTimeoutMinutes { get; set; } = 15;

    // ------------------------------ 视口
    /// <summary>
    /// Web 用例执行的浏览器视口宽度/高度。默认 1920×1080（主流桌面分辨率）：
    /// 1280 下多列表格（如项目管理的操作列）会被横向滚动藏住，行内按钮不可点。
    /// </summary>
    public int ViewportWidth { get; set; } = 1920;
    public int ViewportHeight { get; set; } = 1080;

    // ------------------------------ 执行 trace（迭代 D）
    /// <summary>是否记录 Playwright trace（失败时保留 zip，可回放每步 DOM 与网络快照）</summary>
    public bool TraceEnabled { get; set; } = true;

    /// <summary>trace 仅在这些截图模式下记录（"on-failure" 只留失败；"always" 全留，占空间大）</summary>
    public string TraceMode { get; set; } = "on-failure";

    /// <summary>trace 文件保留天数，由维护任务清理</summary>
    public int TraceRetentionDays { get; set; } = 7;

    /// <summary>单个 trace 的落盘目录（相对 ContentRoot；仅本地存储实现使用）</summary>
    public string TracePath { get; set; } = "traces";

    // ------------------------------ 产物保留策略
    /// <summary>步骤截图保留天数（对象存储/本地同样生效；0 表示永久保留）</summary>
    public int ScreenshotRetentionDays { get; set; } = 90;

    /// <summary>
    /// 失败录像保留天数（0 表示永久保留）。
    ///
    /// 之前没有独立配置：录像 key 挂在截图清理的根级扫描范围里，被动跟着
    /// ScreenshotRetentionDays（90 天）走。但录像比截图大两个数量级，90 天太长；
    /// 独立成 30 天，并且从截图清理的排除列表里摘出来——两个清理任务管同一批文件
    /// 只会互相打架（保留期口径不一致时尤其如此）。
    /// </summary>
    public int VideoRetentionDays { get; set; } = 30;

    // ------------------------------ 视觉回归
    /// <summary>逐像素差值小于该值视为渲染噪声（0-255，默认 16）</summary>
    public int VisualPixelTolerance { get; set; } = 16;

    /// <summary>差异图上报的变化区域上限</summary>
    public int VisualMaxRegions { get; set; } = 5;

    /// <summary>判定为「变化」时是否调用视觉模型生成语义化差异说明（关闭可省一次 LLM 调用）</summary>
    public bool VisualAiDescribe { get; set; } = true;

    // ------------------------------ 视频录制
    /// <summary>是否录制执行视频（失败时保留）。默认开：失败复盘时"看得见过程"比"只有一张终态截图"强得多</summary>
    public bool VideoEnabled { get; set; } = true;

    /// <summary>
    /// 录像保留模式：<c>on-failure</c>（默认，成功即删）/ <c>always</c> / <c>off</c>。
    /// 与 trace 保持同一套语义，运维不需要记两套开关。
    /// </summary>
    public string? VideoMode { get; set; } = "on-failure";

    /// <summary>录像临时目录（Playwright 先写这里，失败才搬到产物存储）。留空则用系统临时目录</summary>
    public string? VideoTempPath { get; set; }

    /// <summary>
    /// 单个录像大小上限（字节，默认 200MB）。超过则丢弃并告警。
    /// 卡住到超时的执行会录出很大的文件，不能让它把内存与存储一起拖垮。
    /// </summary>
    public long VideoMaxBytes { get; set; } = 200L * 1024 * 1024;

    // ------------------------------ 登录态复用
    /// <summary>
    /// 自动登录产生的 storageState 缓存时长（分钟），0 = 不缓存。
    ///
    /// 缓存的是"环境 + 浏览器 + 登录账号"维度的登录态：同一次批量执行里成百条用例
    /// 只需要真正登录一次，省掉每条的登录墙钟时间，也少一个抖动源。
    /// 时长不宜过长——建站 cookie 通常会过期，登录态失效会让所有用例一起莫名失败。
    /// </summary>
    public int AuthStateTtlMinutes { get; set; } = 30;
}
