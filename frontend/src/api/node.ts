import request from './request'
import type { NodeListResponse } from '@/types/node'

/** 执行节点看板：所有运行执行器的实例（主 API、远程 worker） */
export const listNodesApi = () => request.get<unknown, NodeListResponse>('/nodes')

/** 移除一个离线节点的登记（在线节点后端会拒绝） */
export const deleteNodeApi = (name: string) => request.delete<unknown, void>(`/nodes/${name}`)
