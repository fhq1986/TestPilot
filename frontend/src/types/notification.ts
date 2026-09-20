/** 站内消息分类（与后端 NotificationCategory 枚举逐位一致） */
export const NotificationCategory = {
  System: 0,
  Execution: 1,
  Defect: 2,
  Review: 3,
  Plan: 4,
  Schedule: 5,
  Comment: 6,
  Import: 7,
  Visual: 8,
} as const

/** 消息级别：只影响图标与配色 */
export const NotificationLevel = {
  Info: 0,
  Success: 1,
  Warning: 2,
  Error: 3,
} as const

export const NOTIFICATION_CATEGORY_LABELS: Record<number, string> = {
  [NotificationCategory.System]: '系统',
  [NotificationCategory.Execution]: '执行',
  [NotificationCategory.Defect]: '缺陷',
  [NotificationCategory.Review]: '评审',
  [NotificationCategory.Plan]: '测试计划',
  [NotificationCategory.Schedule]: '定时任务',
  [NotificationCategory.Comment]: '评论',
  [NotificationCategory.Import]: '导入',
  [NotificationCategory.Visual]: '视觉基线',
}

export interface NotificationItem {
  id: string
  category: number
  level: number
  title: string
  body?: string | null
  linkUrl?: string | null
  linkLabel?: string | null
  sourceType?: string | null
  sourceId?: string | null
  isRead: boolean
  createdAt: string
}

export interface NotificationCategoryCount {
  category: number
  count: number
}

export interface NotificationUnread {
  total: number
  byCategory: NotificationCategoryCount[]
}
