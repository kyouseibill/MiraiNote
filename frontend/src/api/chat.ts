import { http, unwrap, getAccessToken, API_BASE_URL } from './auth'
import type {
  ChatSession,
  ChatSessionDetail,
  ChatMessage,
  CreateSessionPayload,
  SendMessagePayload,
  ChatAttachmentResponse,
} from '@/types/chat'
import { consumeSseResponse } from './sse'

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

  archiveSession: (sessionId: number) =>
    unwrap<null>(http.post(`/chat/sessions/${sessionId}/archive`)),

  /**
   * 获取已归档会话的轻量列表（仅标题/时间，不含消息内容）。
   */
  getArchivedSessions: () => unwrap<ChatSession[]>(http.get('/chat/sessions/archived')),

  unarchiveSession: (sessionId: number) =>
    unwrap<null>(http.post(`/chat/sessions/${sessionId}/unarchive`)),

  sendMessage: (sessionId: number, payload: SendMessagePayload) =>
    unwrap<ChatMessage>(http.post(`/chat/sessions/${sessionId}/messages`, payload)),

  /**
   * 上传聊天附件，返回提取的文本内容。
   * 支持 PDF / Word / Excel / 文本 / 图片。
   */
  uploadAttachment: async (file: File): Promise<ChatAttachmentResponse> => {
    const formData = new FormData()
    formData.append('file', file)
    return unwrap<ChatAttachmentResponse>(
      http.post('/chat/attachments', formData, {
        timeout: 120000,
      }),
    )
  },

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
        const url = `${API_BASE_URL}/chat/sessions/${sessionId}/messages/stream`
        const response = await fetch(url, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          body: JSON.stringify(payload),
          credentials: 'include',  // 携带 HttpOnly Cookie（RefreshToken）
          signal,
        })

    if (!response.ok) {
      onEvent({ type: 'error', data: { message: `HTTP ${response.status}: ${response.statusText}` } })
      return
    }

    await consumeSseResponse(response, (event) => {
      onEvent({ type: event.type as SseEventType, data: event.data })
    })
  },
}
