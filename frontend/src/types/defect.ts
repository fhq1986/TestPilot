import type { PagedResult } from '@/types/project'

/** 严重度：建议性 < 一般 < 严重 < 致命（数值与后端 DefectSeverity 枚举逐位一致） */
export const DefectSeverity = { Suggestion: 0, Normal: 1, Major: 2, Critical: 3 } as const

/** 状态：与后端 DefectStatus 枚举逐位一致；未闭环 = New/Assigned/Fixed */
export const DefectStatus = {
  New: 0,
  Assigned: 1,
  Fixed: 2,
  Verified: 3,
  Closed: 4,
  Rejected: 5,
  Deferred: 6,
} as const

export const DEFECT_SEVERITY_LABELS: Record<number, string> = {
  0: '建议性',
  1: '一般',
  2: '严重',
  3: '致命',
}

export const DEFECT_STATUS_LABELS: Record<number, string> = {
  0: '新建',
  1: '已指派',
  2: '已修复',
  3: '已验证',
  4: '已关闭',
  5: '已驳回',
  6: '已挂起',
}

/** 执行里某一步骤已关联的缺陷（GET /executions/{id}/defect-links） */
export interface ExecutionDefectLink {
  stepOrder: number
  defectId: string
  defectTitle: string
  status: number
}

export interface DefectListItem {
  id: string
  projectId: string
  projectName: string
  title: string
  severity: number
  status: number
  assignedToName?: string | null
  createdByName?: string | null
  foundInExecutionId?: string | null
  foundInStepOrder?: number | null
  foundInTestCaseId?: string | null
  foundInTestCaseName?: string | null
  externalRef?: string | null
  createdAt: string
  fixedAt?: string | null
  verifiedAt?: string | null
}

/** 已启用的外部缺陷系统（GET /defects/external-providers） */
export interface ExternalDefectProvider {
  id: string
  name: string
  /** 浏览地址前缀：Jira 为 {base}/browse/，禅道为 {base}/bug-view-（拼单号） */
  browseBaseUrl: string
}

/** externalRef（"jira:QA-1" / "zentao:88"）→ 可跳转的浏览地址；未启用/格式不符返回 null */
export function buildExternalUrl(
  providers: ExternalDefectProvider[],
  externalRef?: string | null,
): string | null {
  if (!externalRef) return null
  const idx = externalRef.indexOf(':')
  if (idx <= 0) return null
  const providerId = externalRef.slice(0, idx)
  const key = externalRef.slice(idx + 1)
  const provider = providers.find((p) => p.id === providerId)
  if (!provider || !key) return null
  // 禅道的浏览页以 -{id}.html 结尾
  return provider.id === 'zentao' ? `${provider.browseBaseUrl}${key}.html` : `${provider.browseBaseUrl}${key}`
}

export interface DefectCaseLink {
  testCaseId: string
  testCaseName: string
  module?: string | null
}

export interface DefectOccurrence {
  id: string
  executionId?: string | null
  stepOrder: number
  occurredAt: string
}

export interface DefectDetail {
  id: string
  projectId: string
  projectName: string
  title: string
  description?: string | null
  severity: number
  status: number
  assignedToId?: string | null
  assignedToName?: string | null
  createdById?: string | null
  createdByName?: string | null
  foundInExecutionId?: string | null
  foundInStepOrder?: number | null
  foundInTestCaseId?: string | null
  foundInTestCaseName?: string | null
  externalRef?: string | null
  resolutionNote?: string | null
  createdAt: string
  updatedAt: string
  fixedAt?: string | null
  verifiedAt?: string | null
  verifiedByName?: string | null
  cases: DefectCaseLink[]
  occurrences: DefectOccurrence[]
}

export interface DefectTrendPoint {
  date: string
  created: number
  closed: number
}

export interface DefectStats {
  openTotal: number
  openCritical: number
  openMajor: number
  openNormal: number
  openSuggestion: number
  createdLast7Days: number
  closedLast7Days: number
  avgFixHours?: number | null
  avgVerifyHours?: number | null
  trend: DefectTrendPoint[]
}

export type DefectListResult = PagedResult<DefectListItem>
