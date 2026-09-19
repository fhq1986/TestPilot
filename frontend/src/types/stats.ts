export interface DashboardOverview {
  totalProjects: number
  totalCases: number
  totalExecutions: number
  executions7d: number
  passRate7d: number
  runningCount: number
  /** 被标记为不稳定的用例数（flaky） */
  flakyCount: number
  /** 测试计划总数 */
  totalPlans: number
  /** 进行中的计划数（验收期内） */
  activePlans: number
  /** 近 7 天执行时长 P95（毫秒）——长尾指标，性能回归最先在这里体现 */
  durationP95Ms: number
  /** 近 7 天执行时长均值（毫秒），与 P95 并排看长尾有多长 */
  durationAvgMs: number
  /** 未闭环缺陷数（New/Assigned/Fixed）——"还欠着多少债"的直接度量 */
  openDefects: number
  /** 未闭环中致命/严重的数量，达标门槛盯的就是它 */
  openCriticalDefects: number
  /** 近 7 天新增缺陷数 */
  newDefects7d: number
  /** 近 7 天闭环缺陷数（时点 = VerifiedAt） */
  closedDefects7d: number
}

export interface TrendPoint {
  date: string
  passed: number
  failed: number
  total: number
  /** 当日新增缺陷数（质量趋势：缺陷才是测试的产出） */
  defectsCreated: number
  /** 当日闭环缺陷数（时点 = VerifiedAt） */
  defectsClosed: number
}

/**
 * 稳定性榜条目。
 * 替代了原来的「失败次数 Top5」：按绝对次数排会让高频用例霸榜，
 * 而「跑 100 次失败 20 次」和「跑 5 次失败 5 次」是两回事。
 */
export interface UnstableCaseItem {
  testCaseId: string
  testCaseName: string
  module?: string | null
  /** 统计区间内的执行次数（样本量，决定失败率可不可信） */
  totalRuns: number
  failCount: number
  failRate: number
  lastFailedAt: string
  /** flaky：便于区分"用例坏了"还是"环境抖动" */
  isFlaky: boolean
}

/** 进行中计划的达标态势（判定由后端 PlanGateEvaluator 给出，前端不重算） */
export interface PlanGatingItem {
  planId: string
  projectId: string
  planName: string
  releaseName?: string | null
  projectName: string
  targetPassRate: number
  caseCount: number
  /** 为 null 表示还没跑过轮次，此时不要显示「未达标」 */
  evaluatedRoundNo?: number | null
  gatePassed: boolean
  evaluatedPassRate: number
  gateReasons: string[]
  endsAt?: string | null
  /** 距截止天数：已过期为负数，未设截止为 null */
  daysToDeadline?: number | null
}

/** 定时任务健康度——最容易「悄悄坏掉」的一环 */
export interface ScheduleHealth {
  total: number
  enabled: number
  withError: number
  lastRunAt?: string | null
  nextRunAt?: string | null
  lastErrorScheduleName?: string | null
}

export interface DashboardResponse {
  overview: DashboardOverview
  trend: TrendPoint[]
  unstableTop: UnstableCaseItem[]
  activePlans: PlanGatingItem[]
  scheduleHealth: ScheduleHealth
}
