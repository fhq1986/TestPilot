import request from './request'
import type { PagedResult, Project, ProjectPayload } from '@/types/project'
import type { BatchDeleteResult } from '@/types/common'

export const getProjects = (params: { search?: string; page?: number; pageSize?: number }) =>
  request.get<unknown, PagedResult<Project>>('/projects', { params })

export const getProject = (id: string) => request.get<unknown, Project>(`/projects/${id}`)

export const createProject = (data: ProjectPayload) =>
  request.post<unknown, Project>('/projects', data)

export const updateProject = (id: string, data: ProjectPayload) =>
  request.put<unknown, Project>(`/projects/${id}`, data)

export const deleteProject = (id: string) => request.delete<unknown, void>(`/projects/${id}`)

// 批量删除（仅删除没有任何用例的项目）
export const batchDeleteProjects = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/projects/batch-delete', { ids })

// ==================== 项目级 API Token ====================
// 供 CI / 外部系统以项目身份触发执行（webhook 端点用 Bearer 认证）。
// 明文只在创建响应里出现一次，服务端只存哈希——列表里永远只有前缀。
export interface ApiTokenItem {
  id: string
  name: string
  /** 明文前 12 字符，用于辨认 */
  prefix: string
  createdAt: string
  expiresAt?: string | null
  revokedAt?: string | null
  lastUsedAt?: string | null
}

export interface ApiTokenCreated extends ApiTokenItem {
  /** 仅此一次可见 */
  plainToken: string
}

export const listApiTokens = (projectId: string) =>
  request.get<unknown, ApiTokenItem[]>(`/projects/${projectId}/api-tokens`)

export const createApiToken = (projectId: string, data: { name: string; expiresInDays?: number }) =>
  request.post<unknown, ApiTokenCreated>(`/projects/${projectId}/api-tokens`, data)

export const revokeApiToken = (projectId: string, tokenId: string) =>
  request.delete<unknown, void>(`/projects/${projectId}/api-tokens/${tokenId}`)
