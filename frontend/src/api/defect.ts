import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { DefectDetail, DefectListResult, DefectStats, ExternalDefectProvider } from '@/types/defect'

export const getDefects = (params: {
  projectId?: string
  status?: number
  severity?: number
  assignedToId?: string
  /** 执行详情页「该执行关联的缺陷」 */
  executionId?: string
  /** 用例详情页「该用例暴露的缺陷」 */
  testCaseId?: string
  search?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, DefectListResult>('/defects', { params })

export const getDefectStats = (projectId?: string) =>
  request.get<unknown, DefectStats>('/defects/stats', {
    params: projectId ? { projectId } : undefined,
  })

export const getDefect = (id: string) => request.get<unknown, DefectDetail>(`/defects/${id}`)

export interface CreateDefectPayload {
  projectId: string
  title: string
  description?: string | null
  severity: number
  assignedToId?: string | null
  externalRef?: string | null
  /** 一键转缺陷：来源执行与步骤（提供时后端自动快照证据并关联用例） */
  foundInExecutionId?: string | null
  foundInStepOrder?: number | null
  /** 关联用例（可选，非必填）。必须与缺陷同项目 */
  testCaseIds?: string[]
}

export const createDefect = (data: CreateDefectPayload) =>
  request.post<unknown, DefectDetail>('/defects', data)

export const updateDefect = (
  id: string,
  data: {
    title: string
    description?: string | null
    severity: number
    assignedToId?: string | null
    externalRef?: string | null
    /** 关联用例的**目标全集**（省略 = 不改动关联；空数组 = 解除全部关联） */
    testCaseIds?: string[]
  },
) => request.put<unknown, DefectDetail>(`/defects/${id}`, data)

/**
 * 删除缺陷。物理删除，关联用例与复现流水由后端数据库级联清理。
 * 语义是「误建 / 重复 / 填错了」——「不想再看到它」请用状态流转的驳回/挂起/关闭。
 */
export const deleteDefect = (id: string) => request.delete<unknown, void>(`/defects/${id}`)

export const batchDeleteDefects = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/defects/batch-delete', { ids })

/** 状态流转：assign / fix / verify / close / reject / defer / reopen */
export const transitionDefect = (id: string, data: { action: string; assignedToId?: string | null; note?: string | null }) =>
  request.post<unknown, DefectDetail>(`/defects/${id}/transition`, data)

export const linkDefectCase = (id: string, testCaseId: string) =>
  request.post<unknown, void>(`/defects/${id}/cases`, { testCaseId })

export const unlinkDefectCase = (id: string, testCaseId: string) =>
  request.delete<unknown, void>(`/defects/${id}/cases/${testCaseId}`)

/** 认领：把执行里的失败步骤记到该缺陷名下（同一执行同一步骤幂等） */
export const addDefectOccurrence = (id: string, executionId: string, stepOrder: number) =>
  request.post<unknown, void>(`/defects/${id}/occurrences`, { executionId, stepOrder })

// ------------------------------ 外部缺陷系统（配置开关驱动，未启用时 providers 为空）

export const listExternalProviders = () =>
  request.get<unknown, ExternalDefectProvider[]>('/defects/external-providers')

/** 推送缺陷到外部系统（Jira/禅道），返回 externalRef 与可直达 URL */
export const pushDefectExternal = (id: string, provider: string) =>
  request.post<unknown, { externalRef: string; externalUrl: string }>(
    `/defects/${id}/push-external`,
    { provider },
  )
