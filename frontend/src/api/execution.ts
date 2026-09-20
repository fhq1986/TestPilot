import request from './request'
import type { ExecutionDetail, ExecutionSummary, ExecutionStatus } from '@/types/execution'
import type { ExecutionDefectLink } from '@/types/defect'
import type { PagedResult } from '@/types/project'
import type { BatchDeleteResult } from '@/types/common'

export const getExecutions = (params: {
  projectId?: string
  testCaseId?: string
  status?: ExecutionStatus
  page?: number
  pageSize?: number
  /** 近 N 天 */
  days?: number
  /** 仅进行中（Pending/Running） */
  runningOnly?: boolean
  /** 套件运行 / 套件筛选 */
  suiteRunId?: string
  suiteId?: string
  /** 浏览器筛选（chromium / firefox / webkit） */
  browser?: string
}) => request.get<unknown, PagedResult<ExecutionSummary>>('/executions', { params })

export const getExecution = (id: string) => request.get<unknown, ExecutionDetail>(`/executions/${id}`)

/** 该执行里已经转成缺陷的步骤（执行详情页「缺陷」列据此避免重复转单） */
export const getExecutionDefectLinks = (executionId: string) =>
  request.get<unknown, ExecutionDefectLink[]>(`/executions/${executionId}/defect-links`)

export const createExecution = (data: {
  testCaseId: string
  environmentId?: string | null
  /** 指定浏览器（不传则按环境/用例配置） */
  browser?: string | null
  /** 指定数据集行（传了则按该行执行，变量生效） */
  dataSetRowIndex?: number | null
  variables?: Record<string, string> | null
}) => request.post<unknown, ExecutionSummary>('/executions', data)

/** 批量执行：支持浏览器矩阵与数据行展开 */
export const batchExecute = (data: {
  testCaseIds: string[]
  environmentId?: string | null
  browsers?: string[] | null
  expandDataSets?: boolean
  variables?: Record<string, string> | null
}) => request.post<unknown, {
  created: number
  executionIds: string[]
  skippedCaseIds: string[]
  casesWithoutData: number
}>('/executions/batch', data, { timeout: 120000 })

// 批量删除执行记录（连带步骤结果与截图；进行中的执行会被跳过）
export const batchDeleteExecutions = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/executions/batch-delete', { ids })

/**
 * 手动终止执行：Running 中断正在跑的步骤（未执行的标记为跳过），Pending 直接落终态。
 * 后端返回 202 表示已进入协作取消流程，终态稍后由执行器落库并经 SignalR 推送。
 */
export const cancelExecution = (id: string) =>
  request.post<unknown, void>(`/executions/${id}/cancel`)

/**
 * 下载执行 trace（zip）。
 * trace 接口需要 Authorization 头（受权端点，安全审查 S1），`<a href>` 带不上 JWT，
 * 与报告导出一样走 axios 拿 blob 再本地触发下载。
 */
export const downloadExecutionTrace = (executionId: string) =>
  request.get<unknown, Blob>(`/executions/${executionId}/trace`, {
    responseType: 'blob' as const,
    timeout: 120000,
  })

/**
 * 执行录像（受权端点，同样需要 Authorization 头，`<video src>` 直接指向 URL 会因为不带
 * token 而 404）。所以先拿成 blob 再造 objectURL 播放——代价是整段视频进内存，
 * 而录像本身只有几 MB，可以接受；换来的是不必把 token 写进 URL（那会进日志与历史记录）。
 */
export const downloadExecutionVideo = (executionId: string) =>
  request.get<unknown, Blob>(`/executions/${executionId}/video`, {
    responseType: 'blob' as const,
    timeout: 300000,
  })
