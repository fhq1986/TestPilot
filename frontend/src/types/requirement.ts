/** 需求（覆盖统计锚点，精简版） */

export interface Requirement {
  id: string
  projectId: string
  title: string
  description?: string | null
  externalKey?: string | null
  priority?: string | null
  createdAt: string
}

/** 列表项：带覆盖统计 */
export interface RequirementListItem extends Requirement {
  /** 关联的可见用例数 */
  caseCount: number
  /** 关联用例中最近一次执行已通过的数量 */
  passedCaseCount: number
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
}
