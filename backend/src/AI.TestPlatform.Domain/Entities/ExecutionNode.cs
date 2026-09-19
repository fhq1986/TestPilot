namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 执行节点（分布式执行可观测层）。
///
/// 平台所有运行执行器的进程（主 API 实例、远程 worker 实例、容器副本）都在这里登记：
/// 执行器本体的抢占语义（FOR UPDATE SKIP LOCKED）早已是多实例安全的，
/// 这张表解决的是「有哪些节点、各自负载与存活状态」的可视化与管理问题。
/// 存活判定是动态的：LastHeartbeatAt 距今超过阈值（心跳间隔约 3 倍）即视为离线，
/// 不需要额外的心跳超时回写任务。
/// </summary>
public class ExecutionNode
{
    public Guid Id { get; set; }

    /// <summary>节点名（唯一）。默认取机器名，容器部署时用 Node:Name 覆盖；同机多进程会自然带出 pid 区分。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>机器名（登记时刻的 Environment.MachineName，仅供展示）</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>进程实例标识（name:pid），与 Execution.ClaimedBy 前缀对应</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>程序集版本（排障时确认节点跑的是哪版代码）</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>节点声明并发上限（Execution:MaxConcurrency，0 = 自动按 CPU 推导）</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>最近一次心跳时的运行中执行数</summary>
    public int RunningCount { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;
}
