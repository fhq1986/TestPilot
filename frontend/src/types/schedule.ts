/** 定时任务（Cron）——与后端 Application/Schedules/ScheduleDtos.cs 对应 */

export interface ScheduleSummary {
  id: string
  projectId: string
  name: string
  cronExpression: string
  enabled: boolean
  module?: string | null
  priority?: string | null
  testCaseCount: number
  environmentId?: string | null
  environmentName?: string | null
  lastRunAt?: string | null
  nextRunAt?: string | null
  lastCreatedCount: number
  lastError?: string | null
  /** 可读描述，如「每天 02:00」 */
  cronDescription: string
  createdAt: string
  updatedAt: string
  /** 执行范围类型 */
  scopeKind: ScheduleScopeKindValue
  /** 范围为测试计划时，引用的计划（含名称，供列表直接展示） */
  testPlans?: SchedulePlanRef[] | null
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null
}

export interface ScheduleDetail extends Omit<ScheduleSummary, 'testCaseCount'> {
  testCaseIds: string[]
  testPlanIds?: string[] | null
}

/** 执行范围类型（与后端 ScheduleScopeKind 数值一致） */
export const ScheduleScopeKind = {
  /** 按用例：模块/优先级筛选，或直接指定用例 */
  Cases: 0,
  /** 按测试计划：到期时给选中的计划开新一轮 */
  TestPlan: 1,
} as const

export type ScheduleScopeKindValue = (typeof ScheduleScopeKind)[keyof typeof ScheduleScopeKind]

/** 范围里引用的计划（列表页显示计划名而不是 ID） */
export interface SchedulePlanRef {
  id: string
  name: string
  releaseName?: string | null
  /** 0 草稿 / 1 进行中 / 2 已完成 / 3 已归档 —— 只有「进行中」会被定时触发 */
  status: number
}

export interface CreateSchedulePayload {
  projectId: string
  name: string
  cronExpression: string
  enabled: boolean
  module?: string | null
  priority?: string | null
  testCaseIds?: string[] | null
  environmentId?: string | null
  /** 执行范围类型 */
  scopeKind?: ScheduleScopeKindValue
  /** 范围 = 测试计划时的计划 ID 列表（可多选） */
  testPlanIds?: string[] | null
}

export interface UpdateSchedulePayload {
  name: string
  cronExpression: string
  enabled: boolean
  module?: string | null
  priority?: string | null
  testCaseIds?: string[] | null
  environmentId?: string | null
}

export interface ScheduleRunResult {
  scheduleId: string
  createdCount: number
  executionIds: string[]
  nextRunAt?: string | null
  /** 真正的失败 */
  error?: string | null
  /** 信息性提示（例如「计划已有进行中的轮次，本次跳过」），不是失败 */
  message?: string | null
}

export interface CronPreviewResult {
  valid: boolean
  error?: string | null
  cronDescription: string
  occurrences: string[]
}

/** 常用 Cron 预设，供表单快速选择 */
export const CRON_PRESETS: { label: string; value: string }[] = [
  { label: '每小时整点', value: '0 * * * *' },
  { label: '每 30 分钟', value: '*/30 * * * *' },
  { label: '每天 02:00', value: '0 2 * * *' },
  { label: '每天 09:00', value: '0 9 * * *' },
  { label: '工作日 09:00', value: '0 9 * * 1-5' },
  { label: '每周一 09:00', value: '0 9 * * 1' },
  { label: '每月 1 日 03:00', value: '0 3 1 * *' },
]
