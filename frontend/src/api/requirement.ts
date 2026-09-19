import request from './request'
import type { BatchDeleteResult } from '@/types/common'
import type { PagedResult } from '@/types/project'
import type { Requirement, RequirementCoverage, RequirementListItem, RequirementPayload } from '@/types/requirement'

export const getRequirements = (params: {
  projectId?: string
  search?: string
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
