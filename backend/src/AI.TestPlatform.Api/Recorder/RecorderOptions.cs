namespace AI.TestPlatform.Api.Recorder;

/// <summary>录制器配置（appsettings.json 的 Recorder 节）</summary>
public class RecorderOptions
{
    /// <summary>总开关。关闭后录制端点直接返回 503，便于在不允许拉起浏览器的环境禁用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>录制脚本根目录（相对 ContentRoot；不建议放在 wwwroot，脚本不是静态资源）</summary>
    public string Path { get; set; } = "recordings";

    /// <summary>单个实例允许同时进行的录制会话数（每个会话会拉起一个真实浏览器窗口）</summary>
    public int MaxConcurrentSessions { get; set; } = 2;

    /// <summary>
    /// 无人轮询多久后判定会话已废弃并回收。前端每 2 秒轮询一次，
    /// 页面关闭/网络断开后进程需要被主动清掉，否则浏览器窗口会一直挂着。
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 15;

    /// <summary>会话记录保留天数（含脚本临时文件），由维护任务清理</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>录制窗口的视口尺寸，透传给 codegen 的 --viewport-size（格式 "宽,高"）</summary>
    public string ViewportSize { get; set; } = "1280,720";
}
