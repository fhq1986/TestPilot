import request from './request'
import type { AdoptCasePayload, AdoptCasesResult, ApiEndpointSummary, ExtractedDocument, GeneratedCase, ImportSwaggerResponse } from '@/types/ai'

export const generateCases = (data: { requirement: string; minCases: number }) =>
  request.post<unknown, GeneratedCase[]>('/ai/generate-cases', data, { timeout: 300000 })

// 上传需求文档并提取文本（供「需求描述」自动填充）
export const extractDocument = (file: File) => {
  const form = new FormData()
  form.append('file', file)
  return request.post<unknown, ExtractedDocument>('/ai/extract-document', form, { timeout: 120000 })
}

export const adoptCases = (data: {
  projectId: string
  cases: AdoptCasePayload[]
  aiPrompt?: string | null
}) => request.post<unknown, AdoptCasesResult>('/ai/adopt', data)

export const importSwagger = (data: { projectId: string; url?: string; content?: string }) =>
  request.post<unknown, ImportSwaggerResponse>('/ai/import-swagger', data, { timeout: 120000 })

export const analyzeApiFlow = (data: { endpoints: ApiEndpointSummary[] }) =>
  request.post<unknown, GeneratedCase[]>('/ai/analyze-api-flow', data, { timeout: 300000 })
