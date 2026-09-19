export interface EnvironmentView {
  id: string
  projectId: string
  name: string
  baseUrl: string
  loginUrl?: string | null
  loginUsername?: string | null
  loginPasswordMasked: string
  hasLoginPassword: boolean
  loginSuccessIndicator?: string | null
  autoLogin: boolean
  createdAt: string
  updatedAt: string
  /** 环境默认浏览器（chromium / firefox / webkit）；为空表示跟随用例配置 */
  browser?: string | null
}

export interface CreateEnvironmentPayload {
  name: string
  baseUrl: string
  loginUrl?: string | null
  loginUsername?: string | null
  loginPassword?: string | null
  loginSuccessIndicator?: string | null
  autoLogin: boolean
  browser?: string | null
}

export type UpdateEnvironmentPayload = CreateEnvironmentPayload
