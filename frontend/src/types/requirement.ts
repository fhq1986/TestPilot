/** 需求状态枚举（与后端 RequirementStatus 数值一致） */
export const RequirementStatus = {
  NotStarted: 0,
  InProgress: 1,
  Completed: 2,
} as const

export type RequirementStatusValue = (typeof RequirementStatus)[keyof typeof RequirementStatus]

export const REQUIREMENT_STATUS_LABELS: Record<number, string> = {
  [RequirementStatus.NotStarted]: '未开始',
  [RequirementStatus.InProgress]: '进行中',
  [RequirementStatus.Completed]: '已完成',
}

/** 需求（覆盖统计锚点，精简版） */
export interface Requirement {
  id: string
  projectId: string
  title: string
  description?: string | null
  externalKey?: string | null
  priority?: string | null
  createdAt: string
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null
  // ------------------------------ 进度字段
  planStartDate?: string | null
  planEndDate?: string | null
  actualStartDate?: string | null
  actualEndDate?: string | null
  /** 状态：0 未开始 / 1 进行中 / 2 已完成 */
  status?: RequirementStatusValue
}

/** 列表项：带覆盖统计 + 关联计划数 */
export interface RequirementListItem extends Requirement {
  /** 关联的可见用例数 */
  caseCount: number
  /** 关联用例中最近一次执行已通过的数量 */
  passedCaseCount: number
  /** 关联的测试计划数量 */
  linkedPlanCount: number
}

export interface RequirementCoverage {
  projectId: string
  totalRequirements: number
  coveredRequirements: number
  uncoveredRequirements: number
  /** 覆盖率（百分数，1 位小数） */
  coverageRate: number
  uncoveredList: RequirementListItem[]
}

export interface RequirementPayload {
  title: string
  description?: string | null
  externalKey?: string | null
  priority?: string | null
  planStartDate?: string | null
  planEndDate?: string | null
  actualStartDate?: string | null
  actualEndDate?: string | null
  status?: RequirementStatusValue | null
}

/** 需求关联的测试计划（弹窗用） */
export interface RequirementPlanRef {
  planId: string
  planName: string
  releaseName?: string | null
  status: number
  lastRoundAt?: string | null
}
