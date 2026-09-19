/** 通用批量删除结果（项目 / 用例 / 执行记录共用） */
export interface BatchDeleteSkippedItem {
  id: string
  name?: string | null
  reason: string
}

export interface BatchDeleteResult {
  deleted: number
  skipped: BatchDeleteSkippedItem[]
}
