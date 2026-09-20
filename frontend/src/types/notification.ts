import type { Component } from 'vue'
import {
  AlarmClock, ChatDotSquare, CircleCheck, CircleClose, DataLine, Flag, InfoFilled,
  Picture, Setting, Stamp, Upload, VideoPlay, WarningFilled,
} from '@element-plus/icons-vue'

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

/**
 * 分类图标：让用户扫一眼形状就知道是哪类消息，不必读分类标签。
 * 与 NOTIFICATION_CATEGORY_LABELS 同处一份，新增分类时不会只改一边。
 */
export const NOTIFICATION_CATEGORY_ICONS: Record<number, Component> = {
  [NotificationCategory.System]: Setting,
  [NotificationCategory.Execution]: VideoPlay,
  [NotificationCategory.Defect]: Flag,
  [NotificationCategory.Review]: Stamp,
  [NotificationCategory.Plan]: DataLine,
  [NotificationCategory.Schedule]: AlarmClock,
  [NotificationCategory.Comment]: ChatDotSquare,
  [NotificationCategory.Import]: Upload,
  [NotificationCategory.Visual]: Picture,
}

/** 级别图标：消息中心与铃铛弹层共用，避免两处各写一份 switch */
export const NOTIFICATION_LEVEL_ICONS: Record<number, Component> = {
  [NotificationLevel.Info]: InfoFilled,
  [NotificationLevel.Success]: CircleCheck,
  [NotificationLevel.Warning]: WarningFilled,
  [NotificationLevel.Error]: CircleClose,
}

export const categoryLabel = (category: number) =>
  NOTIFICATION_CATEGORY_LABELS[category] ?? '消息'

export const categoryIcon = (category: number) =>
  NOTIFICATION_CATEGORY_ICONS[category] ?? InfoFilled

export const levelIcon = (level: number) =>
  NOTIFICATION_LEVEL_ICONS[level] ?? InfoFilled

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
