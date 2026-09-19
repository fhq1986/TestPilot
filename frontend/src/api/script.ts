import request from './request'

/** 脚本解析出的步骤（与后端 Application/Scripts/ScriptDtos.cs 对应） */
export interface ParsedScriptStep {
  stepOrder: number
  actionType: number
  config: Record<string, unknown>
  instruction?: string | null
  description?: string | null
  sourceLine: string
  note?: string | null
}

export interface ScriptParseResult {
  ok: boolean
  error?: string | null
  steps: ParsedScriptStep[]
  warnings: string[]
  suggestedName?: string | null
  suggestedType: number
  suggestedBaseUrl?: string | null
}

export interface ScriptImportResult {
  testCaseId: string
  name: string
  stepCount: number
  warnings: string[]
  type: number
  /** 关联测试计划：新加入计划范围的用例数（选择了测试计划时才有值） */
  planLinked?: number
  /** 关联测试计划：已在计划范围内被跳过的用例数 */
  planSkipped?: number
}

export interface ScriptExportResult {
  testCaseId: string
  name: string
  language: string
  script: string
}

/** 解析 Playwright 脚本（仅预览，不落库） */
export const parseScript = (script: string) =>
  request.post<unknown, ScriptParseResult>('/scripts/parse', { script }, { timeout: 60000 })

/** 解析并创建用例 */
export const importScript = (data: {
  projectId: string
  name: string
  script: string
  module?: string | null
  priority?: string | null
  baseUrl?: string | null
  description?: string | null
  /** 可选：创建的用例自动加入该测试计划范围（必须属于同一项目） */
  testPlanId?: string | null
}) => request.post<unknown, ScriptImportResult>('/scripts/import', data, { timeout: 60000 })

/** 导出用例为 Playwright 脚本 */
export const exportScript = (testCaseId: string) =>
  request.get<unknown, ScriptExportResult>(`/scripts/export/${testCaseId}`)
