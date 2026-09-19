import { getAuthToken } from './request'

export interface ChatMessagePayload {
  role: 'system' | 'user' | 'assistant'
  content: string
}

export interface ChatStreamHandlers {
  /** 逐段增量文本 */
  onDelta: (text: string) => void
  /** 提示（如模型不支持图片已降级） */
  onNotice?: (message: string) => void
  onError?: (message: string) => void
  onDone?: () => void
}

const baseURL = import.meta.env.VITE_API_BASE_URL || '/api'

/**
 * 调用后端 SSE 流式对话接口，逐帧回调。
 * 用 fetch + ReadableStream 而不是 EventSource：需要 POST 传消息体与截图。
 */
export async function streamChat(
  payload: { messages: ChatMessagePayload[]; images?: string[] },
  handlers: ChatStreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetch(`${baseURL}/chat/stream`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${getAuthToken()}`,
    },
    body: JSON.stringify(payload),
    signal,
  })

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    handlers.onError?.(`请求失败（${response.status}）${text.slice(0, 200)}`)
    return
  }
  if (!response.body) {
    handlers.onError?.('当前浏览器不支持流式响应')
    return
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder('utf-8')
  let buffer = ''

  const handleFrame = (frame: string) => {
    const line = frame.split('\n').find((l) => l.startsWith('data:'))
    if (!line) return
    const raw = line.slice(5).trim()
    if (!raw) return
    try {
      const event = JSON.parse(raw)
      if (event.type === 'delta') handlers.onDelta(event.content ?? '')
      else if (event.type === 'notice') handlers.onNotice?.(event.message ?? '')
      else if (event.type === 'error') handlers.onError?.(event.message ?? '未知错误')
    } catch {
      // 忽略无法解析的帧
    }
  }

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    let index = buffer.indexOf('\n\n')
    while (index >= 0) {
      handleFrame(buffer.slice(0, index))
      buffer = buffer.slice(index + 2)
      index = buffer.indexOf('\n\n')
    }
  }
  if (buffer.trim()) handleFrame(buffer)
  handlers.onDone?.()
}
