/** 测试套件 / 测试计划 —— 对应后端 Application/Suites/SuiteDtos.cs */
import type { TestType } from './testcase'

export enum SuiteKind {
  Smoke = 0,
  Regression = 1,
  Release = 2,
  Custom = 3,
}

export const SUITE_KIND_LABELS: Record<number, string> = {
  [SuiteKind.Smoke]: '冒烟集',
  [SuiteKind.Regression]: '回归集',
  [SuiteKind.Release]: '发版必跑',
  [SuiteKind.Custom]: '自定义',
}

/** 失败策略（执行编排）：Continue = 一条失败不影响其余用例，StopOnFailure = 快停 */
export enum SuiteFailurePolicy {
  Continue = 0,
  StopOnFailure = 1,
}

export const SUITE_FAILURE_POLICY_LABELS: Record<number, string> = {
  [SuiteFailurePolicy.Continue]: '继续执行',
  [SuiteFailurePolicy.StopOnFailure]: '失败快停',
}

export const SUITE_FAILURE_POLICY_HINTS: Record<number, string> = {
  [SuiteFailurePolicy.Continue]: '某条用例失败后，其余用例照常执行（默认）',
  [SuiteFailurePolicy.StopOnFailure]: '出现失败/错误后，未开始的用例全部跳过并记录原因',
}

export interface SuiteSummary {
  id: string
  projectId: string
  name: string
  description?: string | null
  kind: SuiteKind
  caseCount: number
  environmentId?: string | null
  environmentName?: string | null
  lastRunAt?: string | null
  lastSuiteRunId?: string | null
  lastCreatedCount: number
  lastError?: string | null
  createdAt: string
  updatedAt: string
  failurePolicy?: SuiteFailurePolicy
}

export interface SuiteCaseItem {
  testCaseId: string
  name: string
  module?: string | null
  priority?: string | null
  type: TestType
  isFlaky: boolean
  visualEnabled: boolean
  dataSetId?: string | null
  dataRowCount: number
  order: number
  /** 前置用例（同一套件内）：等它通过后才跑本条 */
  dependsOnTestCaseId?: string | null
  dependsOnName?: string | null
}

export interface SuiteDetail extends Omit<SuiteSummary, 'caseCount'> {
  cases: SuiteCaseItem[]
}

/** 套件成员条目：用例 + 前置用例（与后端 SuiteCaseSpec 对应） */
export interface SuiteCaseSpec {
  testCaseId: string
  dependsOnTestCaseId?: string | null
}

export interface SuitePayload {
  name: string
  description?: string | null
  kind: SuiteKind
  environmentId?: string | null
  failurePolicy?: SuiteFailurePolicy
  cases?: SuiteCaseSpec[] | null
}

export interface RunSuitePayload {
  environmentId?: string | null
  browsers?: string[] | null
  variables?: Record<string, string> | null
  expandDataSets?: boolean
}

export interface SuiteRunResult {
  suiteId: string
  suiteRunId: string
  createdCount: number
  executionIds: string[]
  casesWithoutData: number
  error?: string | null
}

export interface SuiteRunSummary {
  suiteRunId: string
  suiteId: string
  startedAt: string
  total: number
  passed: number
  failed: number
  error: number
  skipped: number
  pending: number
  passRate: number
  durationMs: number
  triggerSource?: string | null
  /** 因编排（前置未通过 / 快停）跳过的条数，用于解释「为什么少了这么多条执行」 */
  orchestrationSkipped?: number
}
