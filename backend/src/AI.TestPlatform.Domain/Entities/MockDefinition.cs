namespace AI.TestPlatform.Domain.Entities;

public enum MockStatus { Stopped, Running }

// Mock 服务定义（WireMock 进程内实例，Spec 为 WireMock JSON 映射）
public class MockDefinition
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public int? Port { get; set; }
    public MockStatus Status { get; set; } = MockStatus.Stopped;
    public string Spec { get; set; } = string.Empty;
    /// <summary>
    /// 启动该 WireMock 实例的节点名（ExecutionNodeRegistry.NodeName）。
    /// 多实例就绪（审查发现）：WireMock 随进程存活且只在所在主机的回环监听，
    /// 其它实例启动时不能靠探活判断它的死活——按归属节点的心跳判断；
    /// 停止时清空，历史数据（null）走探活兜底。
    /// </summary>
    public string? OwnerNode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
