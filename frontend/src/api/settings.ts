import request from './request'
import type {
  AIProviderPreset, NotifyTestResult, SettingsView, TestConnectionResult, TestMailResult,
  UpdateSettingsPayload,
} from '@/types/settings'

export const getSettings = () => request.get<unknown, SettingsView>('/settings')

export const updateSettings = (data: UpdateSettingsPayload) =>
  request.put<unknown, SettingsView>('/settings', data)

// AI 提供商预设（含 Base URL 与常用模型列表）
export const getAIProviders = () => request.get<unknown, AIProviderPreset[]>('/settings/ai-providers')

export const testAiConnection = () =>
  request.post<unknown, TestConnectionResult>('/settings/ai/test', null, { timeout: 60000 })

// 通知渠道连通性测试（channel 为空表示测试所有已配置渠道）
export const testNotification = (channel?: string) =>
  request.post<unknown, NotifyTestResult>('/settings/notify/test', { channel: channel ?? null },
    { timeout: 60000 })

/**
 * 发送测试邮件：发一封真实形态的验收邮件（HTML + 链接 + xlsx 附件）到指定地址。
 * 与 testNotification 的区别是它不测通道，而是让人看到收件人实际会收到什么。
 * 后端失败走 502，axios 会抛异常，所以调用方要 catch 并把 message 显示出来。
 */
export const sendTestMail = (data: { to?: string | null; planId?: string | null }) =>
  request.post<unknown, TestMailResult>('/settings/mail/test', data, { timeout: 120000 })
