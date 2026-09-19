import request from './request'
import type { ParsedScriptStep } from './script'

/** 录制会话状态（与后端 Domain/Entities/RecorderSession.cs 的 RecorderStatus 数值一致） */
export const RecorderStatus = {
  Idle: 0,
  Recording: 1,
  Stopped: 2,
  Failed: 3,
} as const

export type RecorderStatusValue = (typeof RecorderStatus)[keyof typeof RecorderStatus]

export interface RecorderSession {
  id: string
  projectId: string
  name: string
  baseUrl: string | null
  browser: string
  status: RecorderStatusValue
  processId: number
  outputPath: string
  stepCount: number
  scriptFingerprint: string | null
  lastError: string | null
  createdById: string | null
  createdByName: string | null
  savedTestCaseId: string | null
  savedTestCaseName: string | null
  createdAt: string
  startedAt: string | null
  stoppedAt: string | null
  lastPolledAt: string | null
}

/** 一次轮询的快照：脚本原文 + 解析后的步骤 */
export interface RecorderSnapshot {
  sessionId: string
  status: RecorderStatusValue
  /** false 表示脚本自上次轮询以来没有变化，前端可跳过重渲染 */
  changed: boolean
  script: string
  stepCount: number
  steps: ParsedScriptStep[]
  warnings: string[]
  baseUrl: string | null
  lastError: string | null
  savedTestCaseId: string | null
  savedTestCaseName: string | null
}

export interface RecorderCapabilities {
  available: boolean
  reason: string | null
  browsers: { id: string; name: string; note: string }[]
}

export interface RecorderSaveResult {
  testCaseId: string
  name: string
  stepCount: number
  message: string
}

/** 录制能力探测（服务端是否具备 node/cli） */
export const recorderCapabilitiesApi = () =>
  request.get<unknown, RecorderCapabilities>('/recorder/capabilities')

export const listRecorderSessionsApi = (projectId?: string) =>
  request.get<unknown, RecorderSession[]>('/recorder/sessions', { params: { projectId } })

export const getRecorderSessionApi = (id: string) =>
  request.get<unknown, RecorderSession>(`/recorder/sessions/${id}`)

/** 创建会话并拉起录制浏览器 */
export const startRecorderSessionApi = (data: {
  projectId: string
  name?: string
  baseUrl?: string
  browser?: string
}) => request.post<unknown, RecorderSession>('/recorder/sessions', data, { timeout: 60000 })

/** 轮询当前脚本与步骤 */
export const pollRecorderStepsApi = (id: string) =>
  request.get<unknown, RecorderSnapshot>(`/recorder/sessions/${id}/steps`)

export const stopRecorderSessionApi = (id: string) =>
  request.post<unknown, RecorderSession>(`/recorder/sessions/${id}/stop`)

/** 把录制结果保存为用例 */
export const saveRecorderSessionApi = (
  id: string,
  data: { name?: string; module?: string; priority?: string; baseUrl?: string; description?: string },
) => request.post<unknown, RecorderSaveResult>(`/recorder/sessions/${id}/save`, data)

export const deleteRecorderSessionApi = (id: string) =>
  request.delete<unknown, void>(`/recorder/sessions/${id}`)
