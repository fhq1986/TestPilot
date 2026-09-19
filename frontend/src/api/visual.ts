import request from './request'
import type { PagedResult } from '@/types/project'
import type { VisualBaseline, VisualCaseSetting } from '@/types/visual'

export const getVisualBaselines = (params: {
  projectId?: string
  testCaseId?: string
  keyword?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<VisualBaseline>>('/visual/baselines', { params })

/** 接受变化：把某次执行的步骤截图设为新基线 */
export const acceptVisualChange = (executionResultId: string) =>
  request.post<unknown, VisualBaseline>('/visual/baselines/accept', { executionResultId })

export const deleteVisualBaseline = (id: string) =>
  request.delete<unknown, void>(`/visual/baselines/${id}`)

export const getVisualCaseSetting = (testCaseId: string) =>
  request.get<unknown, VisualCaseSetting>(`/visual/cases/${testCaseId}`)
