import request from './request'
import type { DashboardResponse } from '@/types/stats'

/** 趋势窗口天数（后端限制 14–60） */
export const getDashboard = (trendDays?: number) =>
  request.get<unknown, DashboardResponse>('/stats/dashboard', {
    params: trendDays ? { trendDays } : undefined,
  })
