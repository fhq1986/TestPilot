import request from './request'
import type { PagedResult } from '@/types/project'
import type {
  PlanGateResult,
  PlanRoundCaseResult,
  PlanRoundSummary,
  TestPlanDetail,
  TestPlanItem,
  TestPlanPayload,
  TestPlanReport,
  TestPlanStatusValue,
  TestPlanSummary,
  PlanScopeIssue,
} from '@/types/testPlan'

// ------------------------------ 计划

export const listTestPlansApi = (params?: {
  projectId?: string
  status?: TestPlanStatusValue
  ownerId?: string
  releaseName?: string
  search?: string
  requirementId?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<TestPlanSummary>>('/test-plans', { params })

export const testPlanStatusCountsApi = (projectId?: string) =>
  request.get<unknown, Record<string, number>>('/test-plans/summary', { params: { projectId } })

export const testPlanReleasesApi = (projectId?: string) =>
  request.get<unknown, string[]>('/test-plans/releases', { params: { projectId } })

export const getTestPlanApi = (id: string) =>
  request.get<unknown, TestPlanDetail>(`/test-plans/${id}`)


export const createTestPlanApi = (data: TestPlanPayload & { projectId: string }) =>
  request.post<unknown, string>('/test-plans', data)

export const updateTestPlanApi = (id: string, data: TestPlanPayload) =>
  request.put<unknown, string>(`/test-plans/${id}`, data)

export const setTestPlanStatusApi = (id: string, status: TestPlanStatusValue) =>
  request.post<unknown, { message: string }>(`/test-plans/${id}/status`, { status })

export const copyTestPlanApi = (id: string) =>
  request.post<unknown, string>(`/test-plans/${id}/copy`)

export const batchDeleteTestPlansApi = (ids: string[]) =>
  request.post<unknown, { deleted: number; skipped: { id: string; name?: string; reason: string }[] }>(
    '/test-plans/batch-delete',
    { ids },
  )

// ------------------------------ 范围

export const listTestPlanItemsApi = (id: string) =>
  request.get<unknown, TestPlanItem[]>(`/test-plans/${id}/items`)

export const setTestPlanItemsApi = (id: string, testCaseIds: string[]) =>
  request.put<unknown, { count: number }>(`/test-plans/${id}/items`, { testCaseIds })

export const importPlanItemsFromSuiteApi = (id: string, suiteId: string, mode: 'append' | 'replace') =>
  request.post<unknown, { added: number; skipped: number; total: number; message: string }>(
    `/test-plans/${id}/items/from-suite`,
    { suiteId, mode },
  )

export const validatePlanScopeApi = (id: string) =>
  request.get<unknown, PlanScopeIssue[]>(`/test-plans/${id}/items/validate`)

// ------------------------------ 轮次

export const startPlanRoundApi = (
  id: string,
  data?: {
    environmentId?: string | null
    browsers?: string[] | null
    expandDataSets?: boolean | null
    variables?: Record<string, string> | null
  },
) => request.post<unknown, { roundId: string; roundNo: number; created: number }>(
  `/test-plans/${id}/rounds`, data ?? {}, { timeout: 60000 })

export const listPlanRoundsApi = (id: string, take = 30) =>
  request.get<unknown, PlanRoundSummary[]>(`/test-plans/${id}/rounds`, { params: { take } })

export const getPlanRoundApi = (roundId: string) =>
  request.get<unknown, { round: PlanRoundSummary; cases: PlanRoundCaseResult[] }>(
    `/test-plans/rounds/${roundId}`,
  )

export const abortPlanRoundApi = (roundId: string) =>
  request.post<unknown, { message: string; skipped: number }>(
    `/test-plans/rounds/${roundId}/abort`,
  )

// ------------------------------ 达标与报告

/** 供 CI 与前端共用。CI 用法见 docs/ci-integration.md */
export const planGateApi = (id: string) =>
  request.get<unknown, PlanGateResult>(`/test-plans/${id}/gate`)

export const planReportApi = (id: string) =>
  request.get<unknown, TestPlanReport>(`/test-plans/${id}/report`)

/**
 * 导出验收报告 xlsx。
 *
 * 走 axios 拿 blob 再本地触发下载——导出接口需要 Authorization 头，`<a href>` 带不上 JWT。
 * 沿用审计导出的同一套写法。
 */
export const exportPlanReportApi = async (id: string, planName: string) => {
  const blob = await request.get<unknown, Blob>(`/test-plans/${id}/export`, {
    responseType: 'blob',
    timeout: 120000,
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  const stamp = new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-')
  link.href = url
  link.download = `${planName}-验收报告-${stamp}.xlsx`
  document.body.appendChild(link)
  link.click()
  link.remove()
  // 释放对象 URL，否则 blob 会一直留在内存里
  URL.revokeObjectURL(url)
}
