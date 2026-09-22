/** Webhook 渠道视图：地址脱敏 + 是否已配置 */
export interface NotifyChannelView {
  webhookMasked: string
  configured: boolean
}

export interface SettingsView {
  aiBaseUrl: string
  aiApiKeyMasked: string
  hasAiApiKey: boolean
  aiModel: string
  aiMaxTokens: number
  webhookTokenMasked: string
  hasWebhookToken: boolean
  updatedAt: string
  allowPrivateNetworkImport: boolean
  // ------------------------------ 通知配置
  notifyEnabled: boolean
  notifyOnFailureOnly: boolean
  /** 测试计划轮次完成后，把验收结果（含报告与在线链接）发给项目测试负责人与计划负责人 */
  notifyPlanResultEmail: boolean
  wecom: NotifyChannelView
  dingtalk: NotifyChannelView
  feishu: NotifyChannelView
  smtpHost: string
  smtpPort: number
  smtpUseSsl: boolean
  smtpUser: string
  smtpPasswordMasked: string
  hasSmtpPassword: boolean
  mailTo: string
  // ------------------------------ SSO 扫码登录
  ssoAutoProvision: boolean
  ssoFrontendBaseUrl: string
  ssoWecomEnabled: boolean
  ssoWecomCorpId: string
  ssoWecomAgentId: string
  ssoWecomSecretMasked: string
  hasSsoWecomSecret: boolean
  ssoDingtalkEnabled: boolean
  ssoDingtalkClientId: string
  ssoDingtalkClientSecretMasked: string
  hasSsoDingtalkClientSecret: boolean
  // ------------------------------ 标准 OIDC
  ssoOidcEnabled: boolean
  ssoOidcAuthority: string
  ssoOidcClientId: string
  ssoOidcClientSecretMasked: string
  hasSsoOidcClientSecret: boolean
  ssoOidcDisplayName: string
  ssoOidcScopes: string
  // ------------------------------ M8 Agent 自愈闭环（系统级总开关）
  agentLoopEnabled: boolean
}

export interface UpdateSettingsPayload {
  aiBaseUrl?: string | null
  aiApiKey?: string | null
  aiModel?: string | null
  aiMaxTokens?: number | null
  webhookToken?: string | null
  allowPrivateNetworkImport?: boolean | null
  // ------------------------------ 通知配置
  notifyEnabled?: boolean | null
  notifyOnFailureOnly?: boolean | null
  notifyPlanResultEmail?: boolean | null
  notifyWecomWebhook?: string | null
  notifyDingtalkWebhook?: string | null
  notifyFeishuWebhook?: string | null
  smtpHost?: string | null
  smtpPort?: number | null
  smtpUseSsl?: boolean | null
  smtpUser?: string | null
  smtpPassword?: string | null
  mailTo?: string | null
  // ------------------------------ SSO 扫码登录
  ssoAutoProvision?: boolean | null
  ssoFrontendBaseUrl?: string | null
  ssoWecomEnabled?: boolean | null
  ssoWecomCorpId?: string | null
  ssoWecomAgentId?: string | null
  /** 留空表示保留原值 */
  ssoWecomSecret?: string | null
  ssoDingtalkEnabled?: boolean | null
  ssoDingtalkClientId?: string | null
  /** 留空表示保留原值 */
  ssoDingtalkClientSecret?: string | null
  // ------------------------------ 标准 OIDC
  ssoOidcEnabled?: boolean | null
  ssoOidcAuthority?: string | null
  ssoOidcClientId?: string | null
  /** 留空表示保留原值 */
  ssoOidcClientSecret?: string | null
  ssoOidcDisplayName?: string | null
  ssoOidcScopes?: string | null
  // ------------------------------ M8 Agent 自愈闭环（系统级总开关）
  agentLoopEnabled?: boolean | null
  /** 需要清空的字段，逗号分隔：wecom,dingtalk,feishu,mailto,smtppassword */
  clearWebhook?: string | null
}

export interface TestConnectionResult {
  ok: boolean
  model: string
  latencyMs?: number | null
  message?: string | null
}

export interface NotifyTestChannelResult {
  channel: string
  ok: boolean
  error?: string | null
}

export interface NotifyTestResult {
  ok: boolean
  channels: NotifyTestChannelResult[]
  message?: string | null
}

/**
 * 「发送测试邮件」的结果。
 * 把收件人与是否带附件一并回传，让界面能如实说明发生了什么
 * （例如"发出去了但没带附件"，否则用户会以为功能坏了）。
 */
export interface TestMailResult {
  ok: boolean
  message: string
  recipients: string[]
  subject?: string | null
  hasAttachment: boolean
}

/** AI 模型选项 */
export interface AIModelOption {
  id: string
  label: string
  note?: string | null
}

/** AI 提供商预设 */
export interface AIProviderPreset {
  id: string
  name: string
  baseUrl: string
  keyUrl: string
  note?: string | null
  models: AIModelOption[]
}
