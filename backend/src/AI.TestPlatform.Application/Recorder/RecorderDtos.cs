using AI.TestPlatform.Application.Executions;

namespace AI.TestPlatform.Application.Recorder;

/// <summary>录制能力探测（GET /recorder/capabilities）：前端据此决定是否显示录制入口</summary>
public sealed record RecorderCapabilitiesDto(
    bool Available, string? Reason, IReadOnlyList<BrowserOption> Browsers);
