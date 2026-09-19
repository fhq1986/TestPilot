import request from './request'

/** 评论挂载的实体类型（与后端 CommentTarget 枚举一致） */
export const CommentTarget = {
  TestCase: 0,
  Defect: 1,
  TestPlan: 2,
} as const

export interface CommentItem {
  id: string
  authorName: string
  body: string
  createdAt: string
}

export const listComments = (target: number, targetId: string) =>
  request.get<unknown, CommentItem[]>('/comments', { params: { target, targetId } })

export const createComment = (target: number, targetId: string, body: string) =>
  request.post<unknown, CommentItem>('/comments', { target, targetId, body })

export const deleteComment = (id: string) =>
  request.delete<unknown, void>(`/comments/${id}`)
