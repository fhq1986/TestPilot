import request from './request'
import type { PagedResult } from '@/types/project'
import type {
  AuditLogPage,
  LoginResponse,
  MySsoBinding,
  RoleMatrixRow,
  SsoProviderInfo,
  UserInfo,
  UserOption,
  UserRoleValue,
  UserView,
} from '@/types/auth'

export const loginApi = (data: { username: string; password: string }) =>
  request.post<unknown, LoginResponse>('/auth/login', data)

/** 登录页可用的 SSO 方式（后端配置开关驱动，全关时返回空数组） */
export const ssoProvidersApi = () =>
  request.get<unknown, SsoProviderInfo[]>('/auth/sso/providers')

/** 企业授权回调：code+state 换平台 JWT（与密码登录同构的 AuthResult） */
export const ssoLoginApi = (provider: string, data: { code: string; state: string }) =>
  request.post<unknown, LoginResponse>(`/auth/sso/${provider}/login`, data)

/** 已登录用户扫码绑定自己的企业身份（需带 authorize 发出的 state，mode=bind） */
export const ssoBindApi = (provider: string, data: { code: string; state: string }) =>
  request.post<unknown, { message: string }>(`/auth/sso/${provider}/bind`, data)

/** 本人 SSO 绑定状态（个人中心「账号绑定」卡片） */
export const mySsoApi = () => request.get<unknown, MySsoBinding>('/users/me/sso')

/** 取当前登录用户（刷新页面后校验会话 + 拿最新权限） */
export const meApi = () => request.get<unknown, UserInfo>('/auth/me')

export const listUsersApi = (params?: {
  search?: string
  role?: UserRoleValue
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<UserView>>('/users', { params })

/** 各角色人数（列表页概览徽标用） */
export const userRoleCountsApi = () =>
  request.get<unknown, Record<string, number>>('/users/role-counts')

/**
 * 用户选项（项目负责人下拉用）。
 * 权限要求 ManageProjects（安全审查 S6：ViewProjects 会放过只读访客拉全量用户列表）——
 * 能打开项目表单的人必然有该权限，建项目不受影响。支持关键字搜索，上限 500。
 */
export const listUserOptionsApi = (search?: string) =>
  request.get<unknown, UserOption[]>('/users/options', { params: search ? { search } : undefined })

export const createUserApi = (data: {
  username: string
  password: string
  displayName: string
  role: UserRoleValue
  email?: string | null
}) => request.post<unknown, UserView>('/users', data)

export const updateUserApi = (
  id: string,
  data: {
    displayName: string
    role: UserRoleValue
    isActive: boolean
    email?: string | null
  },
) => request.put<unknown, UserView>(`/users/${id}`, data)

export const resetUserPasswordApi = (id: string, newPassword: string) =>
  request.post<unknown, { message: string }>(`/users/${id}/reset-password`, { newPassword })

export const deleteUserApi = (id: string) => request.delete<unknown, void>(`/users/${id}`)

export const changeOwnPasswordApi = (data: { oldPassword: string; newPassword: string }) =>
  request.post<unknown, { message: string }>('/users/me/password', data)

export const roleMatrixApi = () => request.get<unknown, RoleMatrixRow[]>('/users/roles')

/** 审计日志查询 */
export const listAuditLogsApi = (params: {
  action?: string
  resourceType?: string
  username?: string
  succeeded?: boolean
  from?: string
  to?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, AuditLogPage>('/audit', { params })

export const auditFacetsApi = () =>
  request.get<unknown, { actions: string[]; resourceTypes: string[]; usernames: string[] }>(
    '/audit/facets',
  )

/**
 * 导出审计日志为 xlsx。
 *
 * 走 axios 拿 blob 再本地触发下载——不能像静态文件那样用 <a href>：
 * 导出接口需要 Authorization 头，而 <a> 带不上 JWT。
 */
export const exportAuditLogsApi = async (params: {
  action?: string
  resourceType?: string
  username?: string
  succeeded?: boolean
  from?: string
  to?: string
}) => {
  const blob = await request.get<unknown, Blob>('/audit/export', {
    params,
    responseType: 'blob',
    timeout: 120000,
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
  link.href = url
  link.download = `audit-log-${stamp}.xlsx`
  document.body.appendChild(link)
  link.click()
  link.remove()
  // 释放对象 URL，否则 blob 会一直留在内存里
  URL.revokeObjectURL(url)
}
