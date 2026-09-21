/** 计划状态（与后端 TestPlanStatus 数值一致） */
export const TestPlanStatus = {
  Draft: 0,
  Active: 1,
  Completed: 2,
  Archived: 3,
} as const

export type TestPlanStatusValue = (typeof TestPlanStatus)[keyof typeof TestPlanStatus]

/** 计划状态 → 中文标签（列表/详情/项目详情/定时任务四处曾各自复制，收敛于此） */
export const TEST_PLAN_STATUS_LABELS: Record<number, string> = {
  [TestPlanStatus.Draft]: '草稿',
  [TestPlanStatus.Active]: '进行中',
  [TestPlanStatus.Completed]: '已完成',
  [TestPlanStatus.Archived]: '已归档',
}

export const PlanRoundStatus = {
  Running: 0,
  Completed: 1,
  Aborted: 2,
} as const

export const PlanGateMode = {
  /** 最后一轮达标即可 */
  LastRound: 0,
  /** 任意一轮达标即算通过（持续回归场景） */
  AnyRound: 1,
} as const

/** 执行结果的计数与通过率。单独成对象是为了避免与布尔 passed / 字符串 error 撞名 */
export interface PlanStats {
  /** 参与判定的样本数 = 总数 − 跳过 − 被排除的 flaky，恒有 Total = Passed + Failed + Error */
  total: number
  /** 计入达标的通过数：passedNative + (项目 TreatAgentHealedAsPass ? passedViaAgent : 0) */
  passed: number
  failed: number
  error: number
  skipped: number
  pending: number
  passRate: number
  /** 其中「原生通过」（非 Agent 自愈）的条数 */
  passedNative?: number
  /** 其中「Agent 自愈通过」的条数（默认不计入达标） */
  passedViaAgent?: number
}

export interface TestPlanSummary {
  id: string
  projectId: string
  /** 所属项目名（后端已 JOIN 带出，前端不必再查一次项目） */
  projectName?: string | null
  name: string
  description?: string | null
  releaseName?: string | null
  status: TestPlanStatusValue
  startsAt?: string | null
  endsAt?: string | null
  ownerId?: string | null
  ownerName?: string | null
  targetPassRate: number
  allowErrors: boolean
  excludeFlakyFromFailure: boolean
  gateMode: number
  /** 缺陷验收门槛：项目存在未闭环致命/严重缺陷时不达标（P2） */
  defectGateEnabled: boolean
  /** 默认环境。列表页「开新一轮」据此判断是否弹环境选择框 */
  environmentId?: string | null
  caseCount: number
  /** 进行中轮次的进度（无进行中轮次时为 null） */
  runningRoundNo?: number | null
  runningCaseCount?: number | null
  runningPassedCount?: number | null
  lastRoundAt?: string | null
  lastRoundNo?: number | null
  lastPassRate?: number | null
  lastError?: string | null
  createdAt: string
  updatedAt?: string | null
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null
  // ------------------------------ 关联需求
  requirementId?: string | null
  requirementTitle?: string | null
}

/** 引用本计划的定时任务（只读） */
export interface PlanScheduleRef {
  id: string
  name: string
  cronExpression: string
  enabled: boolean
}

export interface PlanScopeIssue {
  level: 'Warning' | 'Error'
  kind: string
  testCaseId?: string | null
  name: string
  message: string
}

export interface TestPlanDetail {
  id: string
  projectId: string
  /** 所属项目名，详情页「概览」展示 */
  projectName?: string | null
  name: string
  description?: string | null
  releaseName?: string | null
  status: TestPlanStatusValue
  startsAt?: string | null
  endsAt?: string | null
  ownerId?: string | null
  ownerName?: string | null
  targetPassRate: number
  allowErrors: boolean
  excludeFlakyFromFailure: boolean
  gateMode: number
  /** 缺陷验收门槛：项目存在未闭环致命/严重缺陷时不达标（P2） */
  defectGateEnabled: boolean
  environmentId?: string | null
  environmentName?: string | null
  browsers: string[]
  expandDataSets: boolean
  caseCount: number
  roundCount: number
  /** 哪些定时任务把本计划纳入了执行范围（只读展示，配置入口在定时任务页） */
  schedules?: PlanScheduleRef[]
  scopeIssues: PlanScopeIssue[]
  createdAt: string
  updatedAt?: string | null
  requirementId?: string | null
  requirementTitle?: string | null
}

export interface TestPlanItem {
  testCaseId: string
  name: string
  module?: string | null
  priority?: string | null
  type: number
  /** 用例状态；**为 null 表示用例已删除**（不再拿「废弃」当哨兵值） */
  status: number | null
  isFlaky: boolean
  order: number
  deleted: boolean
}

export interface PlanRoundSummary {
  id: string
  roundNo: number
  status: number
  triggerType: number
  triggerSource?: string | null
  startedAt: string
  completedAt?: string | null
  createdCount: number
  /** 轮次级错误（如「范围内没有可执行的用例」），与 stats.error 不是一回事 */
  error?: string | null
  stats: PlanStats
  /** 本轮是否达到计划当前的目标；还在跑时为 null（判定无意义） */
  gatePassed?: boolean | null
}

export interface PlanRoundExecution {
  executionId: string
  browserName?: string | null
  dataSetRowLabel?: string | null
  status: number
  durationMs?: number | null
  errorMessage?: string | null
  isFlaky: boolean
  screenshotUrl?: string | null
  traceUrl?: string | null
}

export interface PlanRoundCaseResult {
  testCaseId: string
  testCaseName: string
  module?: string | null
  order: number
  executions: PlanRoundExecution[]
}

export interface PlanBlockingCase {
  testCaseId: string
  name: string
  module?: string | null
  status: number
  errorMessage?: string | null
  isFlaky: boolean
}

export interface PlanGateResult {
  passed: boolean
  planName: string
  releaseName?: string | null
  targetPassRate: number
  evaluatedRoundNo?: number | null
  stats: PlanStats
  /** 未达标的原因，逐条人话 */
  reasons: string[]
  blockingCases: PlanBlockingCase[]
}

export interface PlanModuleStat {
  module: string
  total: number
  passed: number
  failed: number
  error: number
  skipped: number
  passRate: number
}

export interface PlanRoundTrend {
  roundNo: number
  startedAt: string
  completedAt?: string | null
  total: number
  passed: number
  failed: number
  error: number
  skipped: number
  passRate: number
  gatePassed?: boolean | null
}

export interface TestPlanReport {
  plan: TestPlanSummary
  gate: PlanGateResult
  trends: PlanRoundTrend[]
  modules: PlanModuleStat[]
  blockingCases: PlanBlockingCase[]
}

export interface TestPlanPayload {
  name: string
  description?: string | null
  releaseName?: string | null
  startsAt?: string | null
  endsAt?: string | null
  ownerId?: string | null
  targetPassRate?: number
  allowErrors?: boolean
  excludeFlakyFromFailure?: boolean
  gateMode?: number
  /** 缺陷验收门槛：项目存在未闭环致命/严重缺陷时不达标（P2） */
  defectGateEnabled?: boolean
  environmentId?: string | null
  browsers?: string[] | null
  expandDataSets?: boolean
  requirementId?: string | null
}

