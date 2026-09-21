import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type {
  Requirement, RequirementCoverage, RequirementListItem, RequirementPayload,
  RequirementPlanRef,
} from '@/types/requirement'

export const getRequirements = (params: {
  projectId?: string
  search?: string
  /** 需求状态枚举值（0=未开始 / 1=进行中 / 2=已完成） */
  status?: number
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<RequirementListItem>>('/requirements', { params })

export const getRequirementCoverage = (projectId?: string) =>
  request.get<unknown, RequirementCoverage>('/requirements/coverage', {
    params: projectId ? { projectId } : undefined,
  })

export const getRequirement = (id: string) =>
  request.get<unknown, Requirement>(`/requirements/${id}`)

export const createRequirement = (data: RequirementPayload & { projectId: string }) =>
  request.post<unknown, Requirement>('/requirements', data)

export const updateRequirement = (id: string, data: RequirementPayload) =>
  request.put<unknown, Requirement>(`/requirements/${id}`, data)

export const deleteRequirement = (id: string) =>
  request.delete<unknown, void>(`/requirements/${id}`)

export const batchDeleteRequirements = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/requirements/batch-delete', { ids })

/** 查询某个需求关联的所有测试计划（需求列表页"查看关联计划"弹窗） */
export const getRequirementPlans = (requirementId: string) =>
  request.get<unknown, RequirementPlanRef[]>(`/requirements/${requirementId}/plans`)

/** 下拉框里用的精简需求列表（只返回 id/title，按项目过滤） */
export const listRequirementsSimple = (params: { projectId: string; search?: string }) =>
  request.get<unknown, PagedResult<{ id: string; title: string }>>('/requirements', {
    params: { ...params, page: 1, pageSize: 50 },
  }).then((res) => res.items)
