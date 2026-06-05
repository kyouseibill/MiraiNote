import { http, unwrap, getAccessToken } from './auth'
import type {
  ChatSession,
  ChatSessionDetail,
  ChatMessage,
  CreateSessionPayload,
  SendMessagePayload,
} from '@/types/chat'

export type SseEventType =
  | 'user_msg'
  | 'token'
  | 'tool_call'
  | 'tool_result'
  | 'done'
  | 'error'

export interface SseEvent {
  type: SseEventType
  data: any
}

export type SseCallback = (event: SseEvent) => void

export const chatApi = {
  getSessions: () => unwrap<ChatSession[]>(http.get('/chat/sessions')),

  getSession: (sessionId: number) =>
    unwrap<ChatSessionDetail>(http.get(`/chat/sessions/${sessionId}`)),

  createSession: (payload: CreateSessionPayload = {}) =>
    unwrap<ChatSession>(http.post('/chat/sessions', payload)),

  updateTitle: (sessionId: number, title: string) =>
    unwrap<ChatSession>(http.put(`/chat/sessions/${sessionId}`, { title })),

  deleteSession: (sessionId: number) =>
    unwrap<null>(http.delete(`/chat/sessions/${sessionId}`)),

  sendMessage: (sessionId: number, payload: SendMessagePayload) =>
    unwrap<ChatMessage>(http.post(`/chat/sessions/${sessionId}/messages`, payload)),

  /**
   * 流式发送消息，通过 onEvent 回调接收 SSE 事件。
   * 使用 AbortController 支持取消。
   */
  sendMessageStream: async (
    sessionId: number,
    payload: SendMessagePayload,
    onEvent: SseCallback,
    signal?: AbortSignal,
  ): Promise<void> => {
    const token = getAccessToken()
    const response = await fetch(`/api/v1/chat/sessions/${sessionId}/messages/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(payload),
      signal,
    })

    if (!response.ok) {
      onEvent({ type: 'error', data: { message: `HTTP ${response.status}: ${response.statusText}` } })
      return
    }

    const reader = response.body!.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })

      // 解析 SSE 事件（可能跨多个 chunk）
      const lines = buffer.split('\n')
      buffer = lines.pop() || '' // 最后一个可能不完整

      let currentEvent = ''
      let currentData = ''

      for (const line of lines) {
        if (line.startsWith('event: ')) {
          currentEvent = line.slice(7).trim()
        } else if (line.startsWith('data: ')) {
          currentData = line.slice(6).trim()
        } else if (line === '' && currentEvent && currentData) {
          // 空行 = 事件结束，触发回调
          try {
            onEvent({ type: currentEvent as SseEventType, data: JSON.parse(currentData) })
          } catch {
            // JSON 解析失败时以字符串形式传递
            onEvent({ type: currentEvent as SseEventType, data: currentData })
          }
          currentEvent = ''
          currentData = ''
        }
      }
    }
  },
}
