import type { StepConfig } from './testcase'

export interface GeneratedStep {
  stepOrder: number
  actionType: number
  config: StepConfig
  aiElementDescription?: string | null
}

export interface GeneratedCase {
  name: string
  priority: string
  type: number
  steps: GeneratedStep[]
}

export interface AdoptCasePayload {
  name: string
  priority: string
  type: number
  steps: GeneratedStep[]
}

export interface AdoptCasesResult {
  created: number
  caseIds: string[]
}

export interface ApiEndpointSummary {
  method: string
  path: string
  summary: string
  response_codes: number[]
}

export interface ImportSwaggerResponse {
  apiName: string
  baseUrl?: string | null
  endpointCount: number
  generatedCases: number
  apiDefinitionId: string
  cases: GeneratedCase[]
  endpointSummaries: ApiEndpointSummary[]
}

/** 需求文档解析结果 */
export interface ExtractedDocument {
  fileName: string
  charCount: number
  text: string
  warnings: string[]
  supportedExtensions: string[]
}
