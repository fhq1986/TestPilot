/** 视觉回归基线与比对 —— 对应后端 Application/Visual/VisualDtos.cs */

export enum VisualStatus {
  BaselineCreated = 0,
  Unchanged = 1,
  Changed = 2,
  Skipped = 3,
}

export const VISUAL_STATUS_LABELS: Record<number, string> = {
  [VisualStatus.BaselineCreated]: '新建基线',
  [VisualStatus.Unchanged]: '无变化',
  [VisualStatus.Changed]: '有变化',
  [VisualStatus.Skipped]: '未比对',
}

export interface VisualBaseline {
  id: string
  testCaseId: string
  testCaseName: string
  module?: string | null
  stepOrder: number
  imageUrl: string
  width: number
  height: number
  compareCount: number
  lastComparedAt?: string | null
  sourceExecutionId?: string | null
  createdAt: string
  updatedAt: string
}

export interface VisualCaseSetting {
  testCaseId: string
  name: string
  visualEnabled: boolean
  visualThreshold: number
  baselineCount: number
}
