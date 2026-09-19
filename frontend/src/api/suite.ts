import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type { RunSuitePayload, SuiteCaseSpec, SuiteDetail, SuiteKind, SuitePayload, SuiteRunResult, SuiteRunSummary, SuiteSummary } from '@/types/suite'

export const getSuites = (params: {
  projectId?: string
  kind?: SuiteKind
  keyword?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<SuiteSummary>>('/suites', { params })

export const getSuite = (id: string) => request.get<unknown, SuiteDetail>(`/suites/${id}`)

export const createSuite = (data: SuitePayload & { projectId: string }) =>
  request.post<unknown, SuiteDetail>('/suites', data)

export const updateSuite = (id: string, data: SuitePayload) =>
  request.put<unknown, SuiteDetail>(`/suites/${id}`, data)

export const deleteSuite = (id: string) => request.delete<unknown, void>(`/suites/${id}`)

export const batchDeleteSuites = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/suites/batch-delete', { ids })

/** 运行套件：创建一批执行（可选浏览器矩阵 / 数据展开 / 变量覆盖） */
export const runSuite = (id: string, data: RunSuitePayload) =>
  request.post<unknown, SuiteRunResult>(`/suites/${id}/run`, data, { timeout: 120000 })

export const getSuiteRuns = (id: string, take = 20) =>
  request.get<unknown, SuiteRunSummary[]>(`/suites/${id}/runs`, { params: { take } })

export const setSuiteCases = (id: string, cases: SuiteCaseSpec[]) =>
  request.put<unknown, { suiteId: string; caseCount: number }>(`/suites/${id}/cases`, { cases })
