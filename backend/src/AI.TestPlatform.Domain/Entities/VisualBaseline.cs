namespace AI.TestPlatform.Domain.Entities;

/// <summary>视觉比对结论</summary>
public enum VisualStatus
{
    /// <summary>基线不存在，本次截图自动成为基线</summary>
    BaselineCreated = 0,
    /// <summary>与基线一致（差异在阈值内）</summary>
    Unchanged = 1,
    /// <summary>与基线不一致（差异超阈值）</summary>
    Changed = 2,
    /// <summary>未能比对（缺图 / 解码失败 / 未启用）</summary>
    Skipped = 3,
}

/// <summary>
/// 视觉基线：按「用例 + 步骤」保存一张基准截图，后续执行与之比对。
/// 只有通过状态的步骤截图才会被采纳为新基线，避免把失败界面固化成基准。
/// </summary>
public class VisualBaseline
{
    public Guid Id { get; set; }
    public Guid TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;

    /// <summary>步骤顺序号，与 ExecutionResult.StepOrder 对应</summary>
    public int StepOrder { get; set; }

    /// <summary>
    /// 建立基线时的浏览器（chromium/firefox/webkit）。
    /// 不同浏览器的字体渲染差异足以把"无变化"误判成"有变化"——基线必须按浏览器分开存。
    /// 可空：历史基线（分浏览器之前建立的），任何浏览器都可用它兜底比对。
    /// </summary>
    public string? Browser { get; set; }

    /// <summary>基线图片相对 URL（/screenshots/baselines/...）</summary>
    public string ImagePath { get; set; } = string.Empty;
    /// <summary>图片绝对路径（便于直接读文件比对）</summary>
    public string FilePath { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>建立基线时的执行 ID（便于追溯）</summary>
    public Guid? SourceExecutionId { get; set; }
    /// <summary>已比对次数（便于识别长期未使用的基线）</summary>
    public int CompareCount { get; set; }
    public DateTime? LastComparedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
