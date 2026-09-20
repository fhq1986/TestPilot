import request from './request'
import type { PagedResult } from '@/types/project'
import type { BatchDeleteResult } from '@/types/common'
import type { CreateTestCasePayload, CreateTestStepPayload, TestCaseImportResult, TestCaseModuleStat, UpdateTestCasePayload, TestCase, TestCaseSummary, TestCaseVersionDetail, TestCaseVersionSummary } from '@/types/testcase'

export const getTestCases = (params: {
  projectId?: string
  search?: string
  module?: string
  /** 仅看关联到该需求的用例（需求覆盖页点「关联用例」数字跳转时带上） */
  requirementId?: string
  /** 仅看不稳定（flaky）用例 */
  flakyOnly?: boolean
  /** 按「最近一次执行结果」筛选（见 types/testcase.ts 的 CaseExecFilter，传数字） */
  execState?: number
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<TestCaseSummary>>('/testcases', { params })

export const getTestCase = (id: string) => request.get<unknown, TestCase>(`/testcases/${id}`)

export const createTestCase = (data: CreateTestCasePayload) =>
  request.post<unknown, TestCase>('/testcases', data)

export const updateTestCase = (id: string, data: UpdateTestCasePayload) =>
  request.put<unknown, TestCase>(`/testcases/${id}`, data)

export const deleteTestCase = (id: string) => request.delete<unknown, void>(`/testcases/${id}`)

export const updateTestCaseSteps = (id: string, steps: CreateTestStepPayload[]) =>
  request.put<unknown, TestCase>(`/testcases/${id}/steps`, { steps })

// ------------------------------------------------------------ Excel 批量导入
export const importTestCases = (form: FormData) =>
  request.post<unknown, TestCaseImportResult>('/testcases/import', form, {
    // Excel 解析 + AI 生成步骤耗时较长
    timeout: 600000,
  })

export const getTestCaseModules = (projectId?: string) =>
  request.get<unknown, TestCaseModuleStat[]>('/testcases/modules', { params: { projectId } })

// 下载导入模板（xlsx）
export const downloadImportTemplate = () =>
  request.get<unknown, Blob>('/testcases/import-template', {
    responseType: 'blob',
    timeout: 60000,
  })

// 批量编辑（字段白名单：模块/优先级/状态/项目/需求；null 不改）
export interface BatchUpdatePayload {
  ids: string[]
  module?: string | null
  priority?: string | null
  status?: number | null
  projectId?: string | null
  requirementId?: string | null
}
export const batchUpdateTestCases = (data: BatchUpdatePayload) =>
  request.post<unknown, { updated: number; skipped: { id: string; reason: string }[] }>(
    '/testcases/batch-update', data)

// 项目级扩展字段定义
export interface CustomFieldDef {
  id: string
  name: string
  /** 0 文本 / 1 数字 / 2 日期 / 3 下拉 */
  fieldType: number
  options?: string | null
  createdAt: string
}
export const listCustomFields = (projectId: string) =>
  request.get<unknown, CustomFieldDef[]>(`/projects/${projectId}/custom-fields`)
export const createCustomField = (
  projectId: string, data: { name: string; fieldType: number; options?: string }) =>
  request.post<unknown, CustomFieldDef>(`/projects/${projectId}/custom-fields`, data)
export const deleteCustomField = (projectId: string, id: string) =>
  request.delete<unknown, void>(`/projects/${projectId}/custom-fields/${id}`)

// 批量删除版本历史（按版本号；物理删除，只失去回滚点不影响当前用例）
export const batchDeleteVersions = (id: string, versions: number[]) =>
  request.post<unknown, { deleted: number; skipped: number[] }>(
    `/testcases/${id}/versions/batch-delete`, { versions })

// ==================== 评审流转（方案 A 标记层） ====================
export const submitReview = (id: string) =>
  request.post<unknown, { message: string }>(`/testcases/${id}/submit-review`)

export const reviewCase = (id: string, action: 'approve' | 'reject', note?: string) =>
  request.post<unknown, { message: string }>(`/testcases/${id}/review`, { action, note })

// 批量删除（软删除）
export const batchDeleteTestCases = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/testcases/batch-delete', { ids })

// ------------------------------------------------------------ 不稳定用例（flake）
/** 手动设置 / 解除「不稳定」标记 */
export const setTestCaseFlake = (id: string, isFlaky: boolean) =>
  request.post<unknown, { id: string; isFlaky: boolean; flakeRate: number }>(
    `/testcases/${id}/flake`, { isFlaky })

/** 批量解除不稳定标记（修复后重置统计基准） */
export const resetTestCaseFlake = (ids: string[]) =>
  request.post<unknown, { reset: number }>('/testcases/reset-flake', { ids })

// ------------------------------ 版本历史

/** 该用例的历史版本（按版本号倒序） */
export const listTestCaseVersions = (testCaseId: string) =>
  request.get<unknown, TestCaseVersionSummary[]>(`/testcases/${testCaseId}/versions`)

/** 某个版本的完整内容（查看与对比用） */
export const getTestCaseVersion = (testCaseId: string, version: number) =>
  request.get<unknown, TestCaseVersionDetail>(`/testcases/${testCaseId}/versions/${version}`)

/** 回滚到某版本。回滚本身也会记一版，所以回滚错了还能再回滚回去 */
export const restoreTestCaseVersion = (testCaseId: string, version: number) =>
  request.post<unknown, TestCase>(`/testcases/${testCaseId}/versions/${version}/restore`)
