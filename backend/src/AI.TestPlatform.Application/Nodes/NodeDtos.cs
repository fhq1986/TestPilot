namespace AI.TestPlatform.Application.Nodes;

/// <summary>执行节点看板行（GET /nodes）</summary>
public sealed record NodeViewDto(
    Guid Id, string Name, string MachineName, string InstanceId, string Version,
    int MaxConcurrency, int RunningCount, bool Online,
    string? LastHeartbeatAt, string StartedAt, int TodayCompleted);

/// <summary>执行节点看板（GET /nodes）：节点列表 + 在线/离线计数</summary>
public sealed record NodeListResponseDto(
    IReadOnlyList<NodeViewDto> Nodes, int OnlineCount, int OfflineCount);
