import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type {
  CaseVariableCheck, DataSetDetail, DataSetImportResult, DataSetPayload, DataSetSummary,
} from '@/types/dataset'

export const getDataSets = (params: {
  projectId?: string
  keyword?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<DataSetSummary>>('/datasets', { params })

export const getDataSet = (id: string) => request.get<unknown, DataSetDetail>(`/datasets/${id}`)

export const createDataSet = (data: DataSetPayload & { projectId: string }) =>
  request.post<unknown, DataSetDetail>('/datasets', data)

export const updateDataSet = (id: string, data: DataSetPayload) =>
  request.put<unknown, DataSetDetail>(`/datasets/${id}`, data)

export const deleteDataSet = (id: string) => request.delete<unknown, void>(`/datasets/${id}`)

export const batchDeleteDataSets = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/datasets/batch-delete', { ids })

/** 上传 Excel / CSV 解析出列与数据行（仅预览，不落库） */
export const importDataSetFile = (file: File) => {
  const form = new FormData()
  form.append('file', file)
  return request.post<unknown, DataSetImportResult>('/datasets/import', form, { timeout: 120000 })
}

/** 校验用例步骤里引用的变量是否都能在数据集中找到 */
export const checkCaseVariables = (testCaseId: string) =>
  request.get<unknown, CaseVariableCheck>(`/datasets/check/${testCaseId}`)

/** 绑定/解绑数据集（dataSetId 传 null 表示解绑） */
export const attachDataSet = (testCaseId: string, dataSetId: string | null) =>
  request.post<unknown, { id: string; dataSetId: string | null }>(
    `/datasets/attach/${testCaseId}`, { dataSetId })
