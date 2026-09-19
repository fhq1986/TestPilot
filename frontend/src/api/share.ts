import request from './request'
import type { CreateSharePayload, PublicReport, ReportShareKind, ShareLink } from '@/types/share'

/** 创建分享链接（需登录） */
export const createShare = (data: CreateSharePayload) =>
  request.post<unknown, ShareLink>('/shares', data)

export const getShares = (params: { kind?: ReportShareKind; refId?: string } = {}) =>
  request.get<unknown, ShareLink[]>('/shares', { params })

/** 吊销分享链接 */
export const revokeShare = (id: string) => request.delete<unknown, void>(`/shares/${id}`)

/** 读取免登录报告（/share/:token 页面使用，无需鉴权） */
export const getPublicReport = (token: string) =>
  request.get<unknown, PublicReport>(`/public/reports/${token}`)
