// 用户角色（与后端 AI.TestPlatform.Domain.Entities.UserRole 数值一一对应）
export const UserRole = {
  Admin: 0,
  Tester: 1,
  Viewer: 2,
  /** 内置超级管理员：拥有全部权限，不在他人用户列表出现、不可删除、不可降权 */
  SuperAdmin: 3,
} as const

export type UserRoleValue = (typeof UserRole)[keyof typeof UserRole]

export interface UserInfo {
  id: string
  username: string
  displayName: string
  role: UserRoleValue
  roleName: string
  /** 权限位图（整数），与后端 Permission 枚举一致 */
  permissions: number
  /** 权限点中文名，便于调试与展示 */
  permissionNames: string[]
}
export interface LoginResponse {
  token: string
  user: UserInfo
}

/** 登录页 SSO 按钮（GET /auth/sso/providers） */
export interface SsoProviderInfo {
  /** URL 段标识：wecom / dingtalk / mock */
  id: string
  /** 按钮显示名 */
  displayName: string
  /** 后端授权跳转端点（浏览器直接 location 跳过去） */
  authorizeUrl: string
}

/** 用户管理页用的完整用户视图 */
export interface UserView {
  id: string
  username: string
  displayName: string
  /** 邮箱：接收测试计划验收结果邮件 */
  email: string | null
  role: UserRoleValue
  roleName: string
  isActive: boolean
  lastLoginAt: string | null
  lastLoginIp: string | null
  createdAt: string
  updatedAt: string | null
  /** 已绑定的 SSO 方式标识（wecom/dingtalk/mock），null 表示仅密码登录 */
  ssoProvider?: string | null
}

/** 本人 SSO 绑定状态（GET /users/me/sso） */
export interface MySsoBinding {
  ssoProvider: string | null
}

/** SSO 方式标识 → 显示名（个人中心/用户列表展示用） */
export const SSO_PROVIDER_NAMES: Record<string, string> = {
  wecom: '企业微信',
  dingtalk: '钉钉',
  mock: '演示登录',
}

/**
 * 下拉用的用户选项。
 * 只带 hasEmail 而不是邮箱明文——这是全量用户列表，
 * 具体地址只在「本项目的测试负责人」那一处暴露（见 Project.testOwnerEmail）。
 */
export interface UserOption {
  id: string
  name: string
  hasEmail: boolean
  isActive: boolean
}

export interface RoleMatrixRow {
  role: string
  roleName: string
  permissions: number
  permissionNames: string[]
  matrix: Record<string, boolean>
}

export interface AuditLogView {
  id: string
  username: string | null
  userRole: string | null
  action: string
  resourceType: string
  resourceId: string | null
  resourceName: string | null
  method: string
  path: string
  statusCode: number
  succeeded: boolean
  /** 变更摘要（请求内容，脱敏后）；非 JSON 请求或未采集时为 null */
  detail: string | null
  /** 响应结果摘要（脱敏后）；非 JSON 响应（文件下载/导出）或未采集时为 null */
  responseBody?: string | null
  ipAddress: string | null
  durationMs: number
  createdAt: string
}

export interface AuditLogPage {
  total: number
  page: number
  pageSize: number
  items: AuditLogView[]
}
