import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type { ActionType, StepConfig } from '@/types/testcase'

/** 下拉选项（精简字段） */
export interface SharedStepOption {
  id: string
  name: string
  itemCount: number
}

export interface SharedVariable {
  name: string
  value: string | null
}

export interface SharedStepItem {
  stepOrder: number
  actionType: ActionType
  config: StepConfig
  aiInstruction?: string | null
  aiElementDescription?: string | null
}

export interface SharedStepGroupView {
  id: string
  projectId: string
  name: string
  description: string | null
  itemCount: number
  /** 有多少个用例引用了该组（列表页展示，删除前给用户看影响面） */
  usedByCaseCount: number
  variables: SharedVariable[]
  createdAt: string
  updatedAt: string | null
}

export interface SharedStepGroupDetail {
  id: string
  projectId: string
  name: string
  description: string | null
  items: SharedStepItem[]
  variables: SharedVariable[]
  createdAt: string
  updatedAt: string | null
}

export interface SharedStepUsage {
  testCaseId: string
  name: string
  stepOrders: number[]
}

export const listSharedStepGroupsApi = (params?: {
  projectId?: string
  search?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<SharedStepGroupView>>('/shared-steps', { params })

/**
 * 下拉选项。单独一个精简接口：列表接口现在是分页的，
 * 而「插入共享步骤」的下拉需要一次拿全（组数量天然有限），两者诉求不同。
 */
export const sharedStepOptionsApi = (projectId?: string) =>
  request.get<unknown, SharedStepOption[]>('/shared-steps/options', { params: { projectId } })

export const getSharedStepGroupApi = (id: string) =>
  request.get<unknown, SharedStepGroupDetail>(`/shared-steps/${id}`)

/** 引用该组的用例清单（删除前确认用） */
export const sharedStepUsagesApi = (id: string) =>
  request.get<unknown, SharedStepUsage[]>(`/shared-steps/${id}/usages`)

export const createSharedStepGroupApi = (data: {
  projectId: string
  name: string
  description?: string | null
  items: SharedStepItem[]
  variables: SharedVariable[]
}) => request.post<unknown, SharedStepGroupDetail>('/shared-steps', data)

export const updateSharedStepGroupApi = (
  id: string,
  data: {
    projectId: string
    name: string
    description?: string | null
    items: SharedStepItem[]
    variables: SharedVariable[]
  },
) => request.put<unknown, SharedStepGroupDetail>(`/shared-steps/${id}`, data)

export const deleteSharedStepGroupApi = (id: string) =>
  request.delete<unknown, { deleted: boolean; affectedCaseCount: number; message: string }>(
    `/shared-steps/${id}`,
  )

export const batchDeleteSharedStepsApi = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/shared-steps/batch-delete', { ids })
