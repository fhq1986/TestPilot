import request from './request'
import type { PagedResult } from '@/types/project'
import type { NotificationItem, NotificationUnread } from '@/types/notification'

/** 我的消息列表（按时间倒序） */
export const getNotifications = (params: {
  unreadOnly?: boolean
  category?: number
  projectId?: string
  title?: string
  dateFrom?: string
  dateTo?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<NotificationItem>>('/notifications', { params })

/** 未读数（铃铛角标；SignalR 断线时靠轮询它兜底） */
export const getNotificationUnread = () =>
  request.get<unknown, NotificationUnread>('/notifications/unread-count')

export const readNotification = (id: string) =>
  request.post<unknown, void>(`/notifications/${id}/read`)

export const readAllNotifications = (category?: number) =>
  request.post<unknown, { read: number }>('/notifications/read-all', undefined, {
    params: category === undefined ? undefined : { category },
  })

export const deleteNotification = (id: string) =>
  request.delete<unknown, void>(`/notifications/${id}`)
