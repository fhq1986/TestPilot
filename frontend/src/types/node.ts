/** 执行节点看板（GET /api/nodes） */
export interface NodeView {
  id: string
  /** 节点名（Node:Name，缺省机器名），与执行 ClaimedBy 前缀对应 */
  name: string
  machineName: string
  instanceId: string
  /** 程序集版本（确认节点跑的哪版代码） */
  version: string
  /** 并发上限（Execution:MaxConcurrency，0=按 CPU 自动） */
  maxConcurrency: number
  /** 最近心跳时的运行中执行数 */
  runningCount: number
  /** 动态存活判定：心跳在阈值内 */
  online: boolean
  lastHeartbeatAt: string
  startedAt: string
  /** 今日该节点完成的执行数 */
  todayCompleted: number
}

export interface NodeListResponse {
  nodes: NodeView[]
  onlineCount: number
  offlineCount: number
}
