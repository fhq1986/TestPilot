import request from './request'
import type { CreateEnvironmentPayload, EnvironmentView, UpdateEnvironmentPayload } from '@/types/environment'

export const getEnvironments = (projectId: string) =>
  request.get<unknown, EnvironmentView[]>(`/projects/${projectId}/environments`)

export const createEnvironment = (projectId: string, data: CreateEnvironmentPayload) =>
  request.post<unknown, EnvironmentView>(`/projects/${projectId}/environments`, data)

export const updateEnvironment = (id: string, data: UpdateEnvironmentPayload) =>
  request.put<unknown, EnvironmentView>(`/environments/${id}`, data)

export const deleteEnvironment = (id: string) => request.delete<unknown, void>(`/environments/${id}`)
