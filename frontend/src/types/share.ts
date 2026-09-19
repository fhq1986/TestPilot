/** 在线报告分享与报告负载 —— 对应后端 Application/Reports/ShareDtos.cs */

export enum ReportShareKind {
  Execution = 0,
  SuiteRun = 1,
  Project = 2,
  TestPlan = 3,
}

export interface ShareLink {
  id: string
  token: string
  kind: ReportShareKind
  refId: string
  projectId?: string | null
  title: string
  expiresAt?: string | null
  revoked: boolean
  viewCount: number
  lastViewedAt?: string | null
  createdAt: string
  /** 完整分享地址（/share/{token}） */
  url: string
}

export interface ReportOverview {
  total: number
  passed: number
  failed: number
  error: number
  skipped: number
  pending: number
  passRate: number
  durationMs: number
  startedAt?: string | null
  endedAt?: string | null
  browsers: string[]
}

export interface ReportModuleRow {
  module: string
  total: number
  passed: number
  failed: number
  passRate: number
}

export interface ReportStep {
  stepOrder: number
  actionType: string
  status: number
  durationMs?: number | null
  errorMessage?: string | null
  screenshotUrl?: string | null
  visualStatus: number
  visualDiffRatio?: number | null
  baselineImageUrl?: string | null
  diffImageUrl?: string | null
  visualNote?: string | null
}

export interface CaseHistoryPoint {
  at: string
  status: number
  durationMs?: number | null
  browserName?: string | null
}

export interface ReportCase {
  executionId?: string | null
  testCaseId?: string | null
  name: string
  caseCode?: string | null
  module?: string | null
  priority?: string | null
  status: number
  browserName?: string | null
  browserVersion?: string | null
  dataSetRowLabel?: string | null
  durationMs?: number | null
  startedAt?: string | null
  errorMessage?: string | null
  aiDiagnosis?: string | null
  aiSuggestedFix?: string | null
  environmentName?: string | null
  triggerSource?: string | null
  sourceSteps?: string | null
  expectedResult?: string | null
  steps?: ReportStep[] | null
  history?: CaseHistoryPoint[] | null
}

export interface ReportTrendPoint {
  date: string
  passed: number
  failed: number
  total: number
}

export interface FlakeRankItem {
  testCaseId: string
  name: string
  flakeRate: number
  executions: number
}

export interface PublicReport {
  title: string
  subtitle: string
  kind: string
  generatedAt: string
  expiresAt?: string | null
  missing: boolean
  overview: ReportOverview
  modules: ReportModuleRow[]
  cases: ReportCase[]
  trend: ReportTrendPoint[]
  flakeRank: FlakeRankItem[]
  suiteId?: string | null
  suiteName?: string | null
  suiteRunId?: string | null
}

export interface CreateSharePayload {
  kind: ReportShareKind
  refId: string
  projectId?: string | null
  title?: string | null
  from?: string | null
  to?: string | null
  /** 有效期天数；0 表示不过期 */
  expiresInDays?: number | null
}
