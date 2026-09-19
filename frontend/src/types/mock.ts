export enum MockStatus {
  Stopped = 0,
  Running = 1,
}

export interface MockDto {
  id: string
  projectId: string
  name: string
  port?: number | null
  status: MockStatus
  createdAt: string
}

export interface CreateMockPayload {
  projectId: string
  name: string
  spec: string
}
