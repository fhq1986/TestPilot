import request from './request'
import type { CreateMockPayload, MockDto } from '@/types/mock'

export const getMocks = (params: { projectId?: string }) =>
  request.get<unknown, MockDto[]>('/mocks', { params })

export const createMock = (data: CreateMockPayload) =>
  request.post<unknown, MockDto>('/mocks', data, { timeout: 60000 })

export const deleteMock = (id: string) => request.delete<unknown, void>(`/mocks/${id}`)
