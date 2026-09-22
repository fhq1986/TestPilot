/** 压测场景（k6）—— 对应后端 Application/LoadTests/LoadTestDtos.cs */
import type { ExecutionStatus } from './execution'

/** 场景用例来源：从已有接口用例拼装 / 导入 OpenAPI 规范 */
export enum LoadTestSource {
  Cases = 0,
  OpenApi = 1,
}

export const LOAD_TEST_SOURCE_LABELS: Record<number, string> = {
  [LoadTestSource.Cases]: '接口用例',
  [LoadTestSource.OpenApi]: 'OpenAPI',
}

/** 来源标签配色：OpenApi 用 info 与「从已有用例拼装」区分开，一眼能看出脚本是导入来的 */
export const loadTestSourceTagType = (source: number): 'primary' | 'info' =>
  source === LoadTestSource.OpenApi ? 'info' : 'primary'

/**
 * k6 执行器类型（profile.kind）。
 * 与后端字面量一一对应，**不能改大小写或连字符**——后端按字符串反序列化。
 */
export type LoadTestProfileKind = 'ramping-vus' | 'constant-vus' | 'constant-arrival-rate'

export const LOAD_TEST_EXECUTOR_LABELS: Record<string, string> = {
  'ramping-vus': '梯度加压（ramping-vus）',
  'constant-vus': '恒定并发（constant-vus）',
  'constant-arrival-rate': '恒定到达率（constant-arrival-rate）',
}

/** 梯度加压的一段：目标 VU 数 + 爬升时长（k6 duration 字面量，如 30s / 1m） */
export interface LoadTestStage {
  duration: string
  target: number
}

export interface LoadTestProfile {
  kind: LoadTestProfileKind
  vus: number
  rate: number
  /** 到达率的单位时间（s / m），仅 constant-arrival-rate 有意义 */
  timeUnit: string
  preAllocatedVUs: number
  maxVUs: number
  stages: LoadTestStage[]
  /** constant-* 执行器的总时长（k6 duration 字面量） */
  duration: string
  gracefulRampDown: string
  thinkTimeSeconds: number
}

export type ThresholdMetric =
  | 'http_req_duration' | 'http_req_failed' | 'checks' | 'http_reqs' | 'iterations'
export type ThresholdAggregator =
  | 'p(90)' | 'p(95)' | 'p(99)' | 'rate' | 'avg' | 'med' | 'max' | 'min' | 'count'
export type ThresholdOperator = '<' | '<=' | '>' | '>='

export interface LoadTestThreshold {
  metric: ThresholdMetric | string
  aggregator: ThresholdAggregator | string
  operator: ThresholdOperator | string
  value: number
}

/** 指标 → 中文名。阈值表与运行结果表共用，避免同一指标两处两种说法 */
export const THRESHOLD_METRIC_LABELS: Record<string, string> = {
  http_req_duration: '请求耗时',
  http_req_failed: '请求失败率',
  checks: '检查通过率',
  http_reqs: '请求数',
  iterations: '迭代数',
}

export const THRESHOLD_AGGREGATOR_OPTIONS: ReadonlyArray<{ value: ThresholdAggregator; label: string }> = [
  { value: 'p(90)', label: 'P90' },
  { value: 'p(95)', label: 'P95' },
  { value: 'p(99)', label: 'P99' },
  { value: 'rate', label: '比率 rate' },
  { value: 'avg', label: '平均 avg' },
  { value: 'med', label: '中位 med' },
  { value: 'max', label: '最大 max' },
  { value: 'min', label: '最小 min' },
  { value: 'count', label: '计数 count' },
]

export const THRESHOLD_OPERATOR_OPTIONS: ReadonlyArray<{ value: ThresholdOperator; label: string }> = [
  { value: '<', label: '< 小于' },
  { value: '<=', label: '≤ 不大于' },
  { value: '>', label: '> 大于' },
  { value: '>=', label: '≥ 不小于' },
]

/** 阈值一行拼成人话，如「请求耗时 p(95) < 500」（列表/表格 tooltip 用） */
export function formatThresholdText(threshold: LoadTestThreshold): string {
  const metric = THRESHOLD_METRIC_LABELS[threshold.metric] ?? threshold.metric
  return `${metric} ${threshold.aggregator} ${threshold.operator} ${threshold.value}`
}

// ------------------------------------------------------------ DTO

export interface LoadTestScenarioSummary {
  id: string
  projectId: string
  projectName?: string | null
  name: string
  description?: string | null
  source: number
  virtualUsers: number
  durationSeconds: number
  caseCount: number
  scriptHash?: string | null
  scriptGeneratedAt?: string | null
  lastRunStatus?: ExecutionStatus | null
  lastRunAt?: string | null
  lastP95Ms?: number | null
  /** 错误率（0~1 的比率，非百分数） */
  lastErrorRate?: number | null
  createdById?: string | null
  createdByName?: string | null
  createdAt: string
  updatedAt?: string | null
}

export interface LoadTestScenarioDetail {
  id: string
  projectId: string
  projectName?: string | null
  name: string
  description?: string | null
  source: number
  environmentId?: string | null
  targetBaseUrl?: string | null
  apiDefinitionId?: string | null
  /** 已选接口操作标识（导入 OpenAPI 后勾选的结果，形如「GET /pets」） */
  operations: string[]
  profile: LoadTestProfile
  thresholds: LoadTestThreshold[]
  variables: Record<string, string>
  virtualUsers: number
  durationSeconds: number
  scriptText?: string | null
  scriptHash?: string | null
  scriptGeneratedAt?: string | null
  caseIds: string[]
  createdById?: string | null
  createdByName?: string | null
  createdAt: string
  updatedAt?: string | null
}

export interface LoadTestRunSummary {
  id: string
  scenarioId: string
  scenarioName: string
  status: ExecutionStatus
  targetBaseUrl: string
  startedAt?: string | null
  endedAt?: string | null
  durationMs?: number | null
  totalRequests: number
  rps?: number | null
  p95Ms?: number | null
  p99Ms?: number | null
  /** 错误率（0~1 的比率） */
  errorRate?: number | null
  thresholdsPassed?: boolean | null
  thresholdTotal: number
  thresholdFailed: number
  errorMessage?: string | null
  createdAt: string
}

export interface LoadTestThresholdResult {
  metric: string
  /** 后端回传的原始阈值表达式（k6 语法），直接展示最不易失真 */
  expression: string
  ok: boolean
}

export interface LoadTestRunDetail extends LoadTestRunSummary {
  projectId: string
  k6Version?: string | null
  exitCode?: number | null
  avgMs?: number | null
  p50Ms?: number | null
  maxMs?: number | null
  /** 检查通过率（0~1 的比率） */
  checksRate?: number | null
  iterations: number
  vusMax?: number | null
  thresholdResults: LoadTestThresholdResult[]
  hasSummary: boolean
  hasLog: boolean
}

export interface GenerateScriptResult {
  script: string
  hash: string
  /** 未能翻译的步骤说明——非空表示脚本并不覆盖全部步骤，必须显式提示用户 */
  warnings: string[]
}

export interface OpenApiOperation {
  method: string
  path: string
  label: string
}

export interface ImportOpenApiResult {
  apiDefinitionId: string
  apiName: string
  baseUrl: string
  operations: OpenApiOperation[]
}

/** 运行接口的 202 响应体 */
export interface LoadTestRunStarted {
  runId: string
}

// ------------------------------------------------------------ 请求负载

export interface CreateLoadTestScenarioRequest {
  projectId: string
  name: string
  description?: string | null
  source: number
  environmentId?: string | null
  targetBaseUrl?: string | null
}

export interface UpdateLoadTestScenarioRequest {
  name: string
  description?: string | null
  environmentId?: string | null
  targetBaseUrl?: string | null
  apiDefinitionId?: string | null
  operations: string[]
  profile: LoadTestProfile
  thresholds: LoadTestThreshold[]
  variables: Record<string, string>
  /** 场景头部展示用的并发数；梯度加压时由 stages 推导，取峰值 */
  virtualUsers: number
  durationSeconds: number
  caseIds: string[]
}

// ------------------------------------------------------------ 小工具

/** 新建场景的默认负载：恒定并发 10 VU 跑 1 分钟——最小可跑通的配置 */
export function defaultLoadTestProfile(): LoadTestProfile {
  return {
    kind: 'constant-vus',
    vus: 10,
    rate: 10,
    timeUnit: 's',
    preAllocatedVUs: 10,
    maxVUs: 50,
    stages: [{ duration: '30s', target: 10 }, { duration: '1m', target: 10 }],
    duration: '1m',
    gracefulRampDown: '10s',
    thinkTimeSeconds: 1,
  }
}

/**
 * 把 k6 duration 字面量（如 30s / 1m30s / 500ms）换算成秒。
 *
 * 为什么不直接用后端算：场景头部的 virtualUsers / durationSeconds 是列表页的摘要列，
 * 必须和 profile 保持一致，而梯度加压的总时长只能由前端把 stages 各段加起来。
 * 解析不出单位时返回 0，宁可摘要显示 0 也不抛错把页面打挂。
 */
export function parseK6Duration(text?: string | null): number {
  if (!text) return 0
  const re = /(\d+(?:\.\d+)?)(ms|s|m|h)/g
  let total = 0
  let matched = false
  let m: RegExpExecArray | null
  while ((m = re.exec(text)) !== null) {
    matched = true
    const value = Number(m[1])
    const unit = m[2]
    total += unit === 'ms' ? value / 1000 : unit === 's' ? value : unit === 'm' ? value * 60 : value * 3600
  }
  return matched ? Math.round(total) : 0
}

/** 秒 → k6 duration 字面量（恒定执行器的总时长） */
export function secondsToK6Duration(seconds: number): string {
  return `${Math.max(0, Math.round(seconds))}s`
}

/** 列表「负载」列：并发数 + 时长的紧凑写法 */
export function formatLoadText(virtualUsers: number, durationSeconds: number): string {
  const duration = durationSeconds >= 60 && durationSeconds % 60 === 0
    ? `${durationSeconds / 60}min`
    : `${durationSeconds}s`
  return `${virtualUsers} VUs · ${duration}`
}

/** 接口操作标识：导入 OpenAPI 后落库的字符串形态（后端按「方法 + 路径」识别操作） */
export function operationKey(operation: Pick<OpenApiOperation, 'method' | 'path'>): string {
  return `${operation.method.toUpperCase()} ${operation.path}`
}

/** 把落库的操作标识还原成可展示的三段（历史场景没有 label，用路径兜底） */
export function parseOperationKey(key: string): OpenApiOperation {
  const index = key.indexOf(' ')
  const method = index > 0 ? key.slice(0, index) : key
  const path = index > 0 ? key.slice(index + 1) : ''
  return { method, path, label: path || key }
}

/** 比率（0~1）→ 百分比文案；null 表示未采集，与 0 区分开 */
export function formatRate(value?: number | null, digits = 2): string {
  if (value === null || value === undefined) return '未采集'
  return `${(value * 100).toFixed(digits)}%`
}

/** 毫秒指标文案；null 表示脚本未在 summaryTrendStats 里声明该分位，显示「未采集」而不是 0 */
export function formatMs(value?: number | null): string {
  if (value === null || value === undefined) return '未采集'
  return `${Math.round(value)}ms`
}
