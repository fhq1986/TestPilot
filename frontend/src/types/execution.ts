import type { StepConfig } from './testcase'

export enum ExecutionStatus {
  Pending = 0,
  Running = 1,
  Passed = 2,
  Failed = 3,
  Error = 4,
  Skipped = 5,
  /** 用户手动终止 */
  Canceled = 6,
}

/**
 * 执行状态的中文文案与标签配色。
 *
 * 放在这里（enum 旁边）而不是各自页面内部：执行记录列表、用例列表的「最近执行结果」
 * 都要用同一套措辞——两处各写一份，迟早出现"同一个状态两个说法"。
 */
export const EXECUTION_STATUS_LABELS: Record<number, string> = {
  [ExecutionStatus.Pending]: '等待中',
  [ExecutionStatus.Running]: '执行中',
  [ExecutionStatus.Passed]: '通过',
  [ExecutionStatus.Failed]: '失败',
  [ExecutionStatus.Error]: '错误',
  [ExecutionStatus.Skipped]: '跳过',
  [ExecutionStatus.Canceled]: '已终止',
}

export const executionStatusTagType = (
  value: number,
): 'success' | 'danger' | 'primary' | 'info' =>
  (({
    [ExecutionStatus.Passed]: 'success',
    [ExecutionStatus.Failed]: 'danger',
    [ExecutionStatus.Error]: 'danger',
    [ExecutionStatus.Running]: 'primary',
    [ExecutionStatus.Pending]: 'info',
    [ExecutionStatus.Skipped]: 'info',
    [ExecutionStatus.Canceled]: 'info',
  } as Record<number, string>)[value] ?? 'info') as 'success' | 'danger' | 'primary' | 'info'

export enum TriggerType {
  Manual = 0,
  Scheduled = 1,
  CIWebhook = 2,
  AIRegression = 3,
}

export interface ExecutionSummary {
  id: string
  testCaseId?: string | null
  testCaseName: string
  status: ExecutionStatus
  triggerType: TriggerType
  browserVersion?: string | null
  startedAt?: string | null
  endedAt?: string | null
  durationMs?: number | null
  resultCount: number
  createdAt: string
  aiDiagnosis?: string | null
  aiSuggestedFix?: string | null
  diagnosisConfidence?: number | null
  environmentName?: string | null
  environmentId?: string | null
  // CI / 定时任务上下文
  triggerSource?: string | null
  commitSha?: string | null
  branch?: string | null
  buildNumber?: string | null
  // 迭代 B：浏览器与数据行、套件归集
  browserName?: string | null
  dataSetRowLabel?: string | null
  suiteId?: string | null
  suiteRunId?: string | null
  // 迭代 D：执行 trace（仅失败时保留，可在 Playwright trace viewer 里逐步回放 DOM 与网络）
  traceUrl?: string | null
  traceSizeBytes?: number | null
  /** 执行录像（仅失败时保留）。受权端点 URL，需带 token 请求，不能直接丢给 <video src> */
  videoUrl?: string | null
  videoSizeBytes?: number | null
  // 执行编排：前置用例与编排跳过原因（前置未通过 / 套件失败快停）
  dependsOnTestCaseId?: string | null
  skipReason?: string | null
  /** 所属项目名（列表跨项目展示用，后端 JOIN 带出） */
  projectName?: string | null
}

export interface ExecutionResultItem {
  id: string
  stepOrder: number
  status: ExecutionStatus
  durationMs?: number | null
  screenshotUrl?: string | null
  log?: string | null
  errorMessage?: string | null
  stackTrace?: string | null
  stepSnapshot?: StepConfig | null
  /** 步骤动作类型（历史行为 null）：摘要用它还原「动作」词 */
  stepActionType?: number | null
  testStepId?: string | null
  // 视觉回归（0 新建基线 / 1 无变化 / 2 有变化 / 3 未比对）
  visualStatus?: number
  visualDiffRatio?: number | null
  baselineImageUrl?: string | null
  diffImageUrl?: string | null
  visualNote?: string | null
}

export interface ExecutionDetail extends ExecutionSummary {
  results: ExecutionResultItem[]
  dataSetRowIndex?: number | null
  /** 步骤内部重试消耗次数（>0 说明执行有靠重试稳住的成分） */
  stepRetryCount?: number
  /** 本次执行的步骤总数快照（共享步骤组展开后、不含自动登录前置） */
  totalSteps?: number | null
  /** 所属项目名称（透过用例取） */
  projectName?: string | null
}
