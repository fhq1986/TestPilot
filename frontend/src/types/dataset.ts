/** 数据集（参数化 / 数据驱动）—— 对应后端 Application/DataSets/DataSetDtos.cs */

export interface DataSetSummary {
  id: string
  projectId: string
  projectName?: string | null
  name: string
  description?: string | null
  columnCount: number
  rowCount: number
  usedByCaseCount: number
  createdAt: string
  updatedAt: string
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null
}

export interface DataSetUsage {
  testCaseId: string
  name: string
  module?: string | null
}

export interface DataSetDetail {
  id: string
  projectId: string
  name: string
  description?: string | null
  columns: string[]
  rows: Record<string, string>[]
  firstRowIsSample: boolean
  usedByCaseCount: number
  usedBy: DataSetUsage[]
  createdAt: string
  updatedAt: string
}

export interface DataSetPayload {
  name: string
  description?: string | null
  columns: string[]
  rows: Record<string, string>[]
  firstRowIsSample?: boolean
}

export interface DataSetImportResult {
  columns: string[]
  rows: Record<string, string>[]
  skippedEmptyRows: number
  warnings: string[]
}

/** 用例步骤里的变量与数据集的匹配情况 */
export interface CaseVariableCheck {
  testCaseId: string
  caseName: string
  dataSetId?: string | null
  dataSetName?: string | null
  usedVariables: string[]
  availableColumns: string[]
  missingVariables: string[]
  unusedColumns: string[]
  sampleRow: Record<string, string>
}
