import request from './request'
import type { PagedResult } from '@/types/project'
import type { BatchDeleteResult } from '@/types/common'
import type {
  CreateSchedulePayload,
  CronPreviewResult,
  ScheduleDetail,
  ScheduleRunResult,
  ScheduleSummary,
  UpdateSchedulePayload,
} from '@/types/schedule'

export const getSchedules = (params: {
  projectId?: string
  enabled?: boolean
  keyword?: string
  page?: number
  pageSize?: number
}) => request.get<unknown, PagedResult<ScheduleSummary>>('/schedules', { params })

export const getSchedule = (id: string) => request.get<unknown, ScheduleDetail>(`/schedules/${id}`)

export const createSchedule = (data: CreateSchedulePayload) =>
  request.post<unknown, ScheduleDetail>('/schedules', data)

export const updateSchedule = (id: string, data: UpdateSchedulePayload) =>
  request.put<unknown, ScheduleDetail>(`/schedules/${id}`, data)

export const deleteSchedule = (id: string) => request.delete<unknown, void>(`/schedules/${id}`)

export const toggleSchedule = (id: string) =>
  request.post<unknown, ScheduleDetail>(`/schedules/${id}/toggle`)

/** 立即试跑：不改变既有排期，额外创建一批执行 */
export const runSchedule = (id: string) =>
  request.post<unknown, ScheduleRunResult>(`/schedules/${id}/run`, null, { timeout: 60000 })

export const previewCron = (cronExpression: string, count = 5) =>
  request.post<unknown, CronPreviewResult>('/schedules/cron-preview', { cronExpression, count })

export const batchDeleteSchedules = (ids: string[]) =>
  request.post<unknown, BatchDeleteResult>('/schedules/batch-delete', { ids })
