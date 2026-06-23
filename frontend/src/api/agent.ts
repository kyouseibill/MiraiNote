import { getAccessToken, API_BASE_URL } from './auth'

export type AgentSseEventType =
  | 'user_msg'
  | 'token'
  | 'tool_call'
  | 'tool_result'
  | 'plan'
  | 'reflection'
  | 'confirm'
  | 'context'
  | 'done'
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

/** Agent 模式消息请求 */
export interface AgentMessagePayload {
  content: string
  enablePlanner?: boolean
  enableReflector?: boolean
  skipConfirmation?: boolean
  attachments?: { fileName: string; fileType: string; textContent: string }[]
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
  ): Promise<void> => {
    const token = getAccessToken()
    const url = `${API_BASE_URL}/chat/sessions/${sessionId}/messages/agent/stream`
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

    const reader = response.body!.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })

      const lines = buffer.split('\n')
      buffer = lines.pop() || ''

      let currentEvent = ''
      let currentData = ''

      for (const line of lines) {
        if (line.startsWith('event: ')) {
          currentEvent = line.slice(7).trim()
        } else if (line.startsWith('data: ')) {
          currentData = line.slice(6).trim()
        } else if (line === '' && currentEvent && currentData) {
          try {
            onEvent({ type: currentEvent as AgentSseEventType, data: JSON.parse(currentData) })
          } catch {
            onEvent({ type: currentEvent as AgentSseEventType, data: currentData })
          }
          currentEvent = ''
          currentData = ''
        }
      }
    }
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
