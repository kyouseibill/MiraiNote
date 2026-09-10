import { http, unwrap, getAccessToken, API_BASE_URL } from './auth'
import { consumeSseResponseUntilTerminal } from './sse'

export type AgentSseEventType =
  | 'user_msg'
  | 'token'
  | 'tool_call'
  | 'tool_progress'
  | 'tool_result'
  | 'heartbeat'
  | 'plan'
  | 'reflection'
  | 'confirm'
  | 'context'
  | 'recoverable'
  | 'done'
  | 'stopped'
  | 'error'

export interface AgentPlanData {
  goal: string
  steps: { order: number; action: string; tools: string[]; expected_output: string }[]
  risks: string[]
}

export interface AgentReflectionData {
  isComplete: boolean
  score: number
  strengths: string[]
  issues: string[]
  suggestions: string[]
}

export interface AgentConfirmData {
  toolName: string
  riskLevel: string
  arguments: string
}

export interface AgentSseEvent {
  type: AgentSseEventType
  data: any
}

export type AgentSseCallback = (event: AgentSseEvent) => void

export interface AgentRun {
  runId: string
  sessionId: number
  status: string
  lastSequence: number
  failureMessage?: string
  recoverableAt?: string
}

/** Agent 模式消息请求 */
export interface AgentMessagePayload {
  content: string
  enablePlanner?: boolean
  enableReflector?: boolean
  skipConfirmation?: boolean
  attachments?: {
    fileName: string
    fileType: string
    textContent: string
    mimeType?: string
    dataUrl?: string
    isImage?: boolean
  }[]
}

export interface TemporaryAgentMessagePayload extends AgentMessagePayload {
  history: { role: 'user' | 'assistant'; content: string }[]
}

export const agentApi = {
  /**
   * Agent 模式流式发送消息（含 Plan → Execute → Reflect → Confirm）。
   * SSE 事件类型：plan、reflection、confirm、context。
   */
  sendAgentMessageStream: async (
    sessionId: number,
    payload: AgentMessagePayload,
    onEvent: AgentSseCallback,
    signal?: AbortSignal,
    onRunCreated?: (run: AgentRun) => void,
  ): Promise<void> => {
    const run = await unwrap<AgentRun>(http.post(`/chat/sessions/${sessionId}/messages/agent/runs`, payload))
    onRunCreated?.(run)
    const token = getAccessToken()
    let afterSequence = 0
    let attempts = 0
    while (!signal?.aborted) {
      const url = `${API_BASE_URL}/chat/agent-runs/${encodeURIComponent(run.runId)}/events?afterSequence=${afterSequence}`
      const response = await fetch(url, {
      method: 'GET',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      credentials: 'include',
      signal,
    })

    if (!response.ok) {
        throw new Error(`HTTP ${response.status}`)
    }

      try {
        await consumeSseResponseUntilTerminal(response, (event) => {
          const sequence = Number((event as any).id)
          if (Number.isFinite(sequence)) afterSequence = Math.max(afterSequence, sequence)
          onEvent({ type: event.type as AgentSseEventType, data: event.data })
        }, signal)
        return
      } catch (error: any) {
        if (signal?.aborted || error?.name === 'AbortError') throw error
        const latest = await unwrap<AgentRun>(http.get(`/chat/agent-runs/${encodeURIComponent(run.runId)}`))
        if (latest.status === 'recoverable') {
          onEvent({ type: 'recoverable', data: { runId: run.runId } })
          while (!signal?.aborted) {
            await new Promise(resolve => setTimeout(resolve, 800))
            const current = await unwrap<AgentRun>(http.get(`/chat/agent-runs/${encodeURIComponent(run.runId)}`))
            if (current.status !== 'recoverable') break
          }
          continue
        }
        attempts += 1
        onEvent({ type: 'heartbeat', data: { message: '任务仍在执行，正在重连…' } })
        await new Promise(resolve => setTimeout(resolve, Math.min(8000, 500 * 2 ** attempts)))
      }
    }
  },

  getRun: (runId: string) => unwrap<AgentRun>(http.get(`/chat/agent-runs/${encodeURIComponent(runId)}`)),
  stopRun: (runId: string) => unwrap<null>(http.post(`/chat/agent-runs/${encodeURIComponent(runId)}/stop`)),
  resumeRun: (runId: string) => unwrap<null>(http.post(`/chat/agent-runs/${encodeURIComponent(runId)}/resume`)),
  confirmRun: (runId: string, confirmed: boolean) =>
    unwrap<null>(http.post(`/chat/agent-runs/${encodeURIComponent(runId)}/confirm`, { confirmed })),

  /** 无状态临时 Agent 聊天。temporaryId 仅用于流内的操作确认。 */
  sendTemporaryAgentMessageStream: async (
    temporaryId: string,
    payload: TemporaryAgentMessagePayload,
    onEvent: AgentSseCallback,
    signal?: AbortSignal,
  ): Promise<void> => {
    const token = getAccessToken()
    const url = `${API_BASE_URL}/chat/temporary/${encodeURIComponent(temporaryId)}/messages/agent/stream`
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(payload),
      credentials: 'include',
      signal,
    })

    if (!response.ok) {
      onEvent({ type: 'error', data: { message: `HTTP ${response.status}` } })
      return
    }

    await consumeSseResponseUntilTerminal(response, (event) => {
      onEvent({ type: event.type as AgentSseEventType, data: event.data })
    }, signal)
  },

  /** 确认/取消危险工具调用 */
  confirmToolCall: async (sessionId: number, confirmed: boolean): Promise<void> => {
    const token = getAccessToken()
    await fetch(`${API_BASE_URL}/chat/sessions/${sessionId}/confirm`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ confirmed }),
      credentials: 'include',
    })
  },

  confirmTemporaryToolCall: async (temporaryId: string, confirmed: boolean): Promise<void> => {
    const token = getAccessToken()
    await fetch(`${API_BASE_URL}/chat/temporary/${encodeURIComponent(temporaryId)}/confirm`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ confirmed }),
      credentials: 'include',
    })
  },

  /** 获取 Agent 记忆列表 */
  getMemories: async (category?: string) => {
    const token = getAccessToken()
    const params = category ? `?category=${encodeURIComponent(category)}` : ''
    const resp = await fetch(`${API_BASE_URL}/agent/memories${params}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      credentials: 'include',
    })
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    return resp.json()
  },

  /** 删除记忆 */
  deleteMemory: async (key: string): Promise<void> => {
    const token = getAccessToken()
    const resp = await fetch(`${API_BASE_URL}/agent/memories/key/${encodeURIComponent(key)}`, {
      method: 'DELETE',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      credentials: 'include',
    })
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
  },
}
