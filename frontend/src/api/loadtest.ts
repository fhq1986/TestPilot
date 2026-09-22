import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type {
  CreateLoadTestScenarioRequest,
  GenerateScriptResult,
  ImportOpenApiResult,
  LoadTestRunDetail,
  LoadTestRunStarted,
  LoadTestRunSummary,
  LoadTestScenarioDetail,
  LoadTestScenarioSummary,
  UpdateLoadTestScenarioRequest,
} from '@/types/loadtest'

export const getLoadTests = (params: {
  projectId?: string
  keyword?: string
  source?: number
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<LoadTestScenarioSummary>>('/loadtests', { params })

export const getLoadTest = (id: string) =>
  request.get<unknown, LoadTestScenarioDetail>(`/loadtests/${id}`)

export const createLoadTest = (data: CreateLoadTestScenarioRequest) =>
  request.post<unknown, LoadTestScenarioDetail>('/loadtests', data)

export const updateLoadTest = (id: string, data: UpdateLoadTestScenarioRequest) =>
  request.put<unknown, LoadTestScenarioDetail>(`/loadtests/${id}`, data)

export const deleteLoadTest = (id: string) => request.delete<unknown, void>(`/loadtests/${id}`)

export const batchDeleteLoadTests = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/loadtests/batch-delete', { ids })

/** 生成（或重新生成）k6 脚本；warnings 表示有步骤没被翻译，需在界面上显式提示 */
export const generateLoadTestScript = (id: string) =>
  request.post<unknown, GenerateScriptResult>(`/loadtests/${id}/generate`, undefined, { timeout: 120000 })

/** 取脚本原文（text/javascript）。已生成的场景详情里也带 scriptText，这里是按需重取 */
export const getLoadTestScript = (id: string) =>
  request.get<unknown, string>(`/loadtests/${id}/script`, { responseType: 'text' })

export const runLoadTest = (id: string) =>
  request.post<unknown, LoadTestRunStarted>(`/loadtests/${id}/run`, undefined, { timeout: 60000 })

/** 该场景的运行历史。返回**纯数组**（非分页），与套件历史同一套约定 */
export const getLoadTestRuns = (id: string, take = 20) =>
  request.get<unknown, LoadTestRunSummary[]>(`/loadtests/${id}/runs`, { params: { take } })

export const getLoadTestRun = (runId: string) =>
  request.get<unknown, LoadTestRunDetail>(`/loadtests/runs/${runId}`)

export const cancelLoadTestRun = (runId: string) =>
  request.post<unknown, void>(`/loadtests/runs/${runId}/cancel`)

export const importLoadTestOpenApi = (data: { projectId: string; name: string; spec: string }) =>
  request.post<unknown, ImportOpenApiResult>('/loadtests/import-openapi', data, { timeout: 120000 })

/**
 * 下载运行摘要 / 日志。
 *
 * 走 axios 拿 blob 再本地触发下载——下载接口需要 Authorization 头，`<a href>` 带不上 JWT，
 * 与验收报告导出沿用同一套写法。
 */
async function downloadBlob(url: string, filename: string) {
  const blob = await request.get<unknown, Blob>(url, { responseType: 'blob', timeout: 60000 })
  const objectUrl = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = objectUrl
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  // 释放对象 URL，否则 blob 会一直留在内存里
  URL.revokeObjectURL(objectUrl)
}

export const downloadLoadTestRunSummary = (runId: string, scenarioName: string) =>
  downloadBlob(`/loadtests/runs/${runId}/summary`, `${scenarioName}-summary-${runId.slice(0, 8)}.json`)

export const downloadLoadTestRunLog = (runId: string, scenarioName: string) =>
  downloadBlob(`/loadtests/runs/${runId}/log`, `${scenarioName}-log-${runId.slice(0, 8)}.log`)
