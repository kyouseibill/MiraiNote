import { http, unwrap } from './auth'
import type {
  ChatSession,
  ChatSessionDetail,
  ChatMessage,
  CreateSessionPayload,
  SendMessagePayload,
} from '@/types/chat'

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
}
