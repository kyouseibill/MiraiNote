// ============================================================
// Chat 会话 API（指令面板 command 会话 + 侧边对话 context 会话）
// 端点沿用现有 /chat/sessions 体系 + M1 扩展字段（契约 §2.9–2.10）：
//   POST /chat/sessions { sessionType, attachToType, attachToObjectId }
//   POST /chat/sessions/{id}/messages/stream（context 会话普通模式流）
// mock 分支：内存会话 + 模拟 token 流。
// ============================================================
import { getAccessToken, API_BASE_URL, http, unwrap } from './client'
import { consumeSseResponseUntilTerminal } from './sse'
import type { AttachToType, SessionType } from './types'

const USE_MOCK = import.meta.env.MIRAI_USE_MOCK === '1'

export interface ChatMessage {
  id: number
  role: 'user' | 'assistant'
  content: string
  createdAt: string
}

export interface ChatSession {
  id: number
  title: string
  isArchived: boolean
  isPinned: boolean
  sessionType?: SessionType | null
  attachToType?: AttachToType | null
  attachToObjectId?: number | null
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface CreateMiraiSessionRequest {
  title?: string | null
  sessionType?: SessionType
  attachToType?: AttachToType | null
  attachToObjectId?: number | null
}

export type SseEventType = 'user_msg' | 'token' | 'done' | 'error'
export interface SseEvent {
  type: SseEventType
  data: Record<string, unknown> & { content?: string; message?: string }
}
export type SseCallback = (event: SseEvent) => void

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))

// ---------- mock 会话存储 ----------
const mockSessions = new Map<number, ChatSession>()
let mockSeq = 0
let mockMsgSeq = 0

export const chatApi = {
  /** 创建会话（sessionType='command' 指令面板；'context' 侧边对话需挂载对象） */
  createSession: (payload: CreateMiraiSessionRequest = {}): Promise<ChatSession> => {
    if (USE_MOCK) {
      const session: ChatSession = {
        id: 800000 + ++mockSeq,
        title: payload.title ?? '新会话',
        isArchived: false,
        isPinned: false,
        sessionType: payload.sessionType ?? null,
        attachToType: payload.attachToType ?? null,
        attachToObjectId: payload.attachToObjectId ?? null,
        messages: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      mockSessions.set(session.id, session)
      return sleep(200).then(() => structuredClone(session))
    }
    return unwrap<Record<string, unknown>>(http.post('/chat/sessions', payload)).then((s) => normalizeSession(s))
  },

  getSession: (sessionId: number): Promise<ChatSession> => {
    if (USE_MOCK) {
      const s = mockSessions.get(sessionId)
      return s ? sleep(150).then(() => structuredClone(s)) : Promise.reject(new Error('会话不存在'))
    }
    return unwrap<Record<string, unknown>>(http.get(`/chat/sessions/${sessionId}`)).then((s) => normalizeSession(s))
  },

  /** 普通模式流式消息（侧边对话）。context 会话由服务端自动注入对象快照。 */
  sendMessageStream: async (
    sessionId: number,
    content: string,
    onEvent: SseCallback,
    signal?: AbortSignal,
  ): Promise<void> => {
    if (USE_MOCK) return runMockChatStream(sessionId, content, onEvent, signal)

    const token = getAccessToken()
    const url = `${API_BASE_URL}/chat/sessions/${sessionId}/messages/stream`
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ content }),
      credentials: 'include',
      signal,
    })
    if (!response.ok) {
      onEvent({ type: 'error', data: { message: `HTTP ${response.status}: ${response.statusText}` } })
      return
    }
    await consumeSseResponseUntilTerminal(
      response,
      (event) => onEvent({ type: event.type as SseEventType, data: (event.data ?? {}) as SseEvent['data'] }),
      signal,
    )
  },
}

/** 现有会话 DTO → 本地结构（messages 在 detail 响应里） */
function normalizeSession(dto: Record<string, unknown>): ChatSession {
  return {
    id: dto.id as number,
    title: (dto.title as string) ?? '',
    isArchived: !!dto.isArchived,
    isPinned: !!dto.isPinned,
    sessionType: (dto.sessionType as SessionType | null) ?? null,
    attachToType: (dto.attachToType as AttachToType | null) ?? null,
    attachToObjectId: (dto.attachToObjectId as number | null) ?? null,
    messages: (dto.messages as ChatMessage[] | undefined) ?? [],
    createdAt: (dto.createdAt as string) ?? '',
    updatedAt: (dto.updatedAt as string) ?? '',
  }
}

// ============================================================
// mock：context 会话的模拟回答（携带挂载对象语境，视觉稿 ⑤ 场景复刻）
// ============================================================
async function runMockChatStream(
  sessionId: number,
  content: string,
  onEvent: SseCallback,
  signal?: AbortSignal,
): Promise<void> {
  const session = mockSessions.get(sessionId)
  const aborted = () => signal?.aborted === true
  const emit = (type: SseEventType, data: SseEvent['data']) => onEvent({ type, data })

  session?.messages.push({ id: --mockMsgSeq, role: 'user', content, createdAt: new Date().toISOString() })
  await sleep(300)
  if (aborted()) return
  emit('user_msg', { id: mockMsgSeq })

  const target = session?.attachToType ?? 'worklog'
  const title = attachTitle(session)
  const answer = contextAnswer(content, target, title)

  let i = 0
  while (i < answer.length) {
    if (aborted()) return
    const size = 2 + Math.floor(Math.random() * 4)
    emit('token', { content: answer.slice(i, i + size) })
    i += size
    await sleep(40)
  }
  session?.messages.push({ id: --mockMsgSeq, role: 'assistant', content: answer, createdAt: new Date().toISOString() })
  emit('done', { messageId: mockMsgSeq, content: answer, createdAt: new Date().toISOString() })
}

function attachTitle(session: ChatSession | undefined): string {
  if (!session) return '当前记录'
  return `${session.attachToType ?? '记录'} #${session.attachToObjectId ?? '?'}`
}

function contextAnswer(question: string, target: string, title: string): string {
  if (/风险|问题|隐患/.test(question)) {
    return (
      `基于${title}，主要风险有二：\n\n` +
      '1. **存量数据 37 条差异没有回滚预案**，建议补充「差异处理决策表」；\n' +
      '2. **安全评审若周三前排不上期**，会连锁影响上线窗口。\n\n' +
      '可参考你 8/13 与老王的讨论结论（见右侧「转为记录」可沉淀结论）。'
    )
  }
  if (/总结|摘要|概括/.test(question)) {
    return `${title} 的要点：v3 完成授权收敛（RBAC 3 角色），风险集中在存量数据与安全评审排期。${target === 'lifelog' ? '从生活记录看，本周整体状态平稳。' : ''}`
  }
  return (
    `围绕${title}：${question.replace(/[？?]/g, '')}——从关联记录看，当前推进方向没有明显偏差。` +
    '如需更深入的分析，可以在指令面板（Ctrl+K）发起跨记录检索。'
  )
}
