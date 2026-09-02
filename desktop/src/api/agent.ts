// ============================================================
// Agent 模式 SSE 客户端（指令面板用）
// 从 frontend/src/api/agent.ts 复制适配；MIRAI_USE_MOCK=1 时改为本地模拟事件流
// （token 逐段 / tool_call / tool_progress / tool_result / confirm / done / error），
// 用于离线验证指令面板渲染与 Confirm 拦截。
// ============================================================
import { getAccessToken, API_BASE_URL } from './client'
import { consumeSseResponseUntilTerminal } from './sse'

const USE_MOCK = import.meta.env.MIRAI_USE_MOCK === '1'

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
  | 'done'
  | 'error'

export interface AgentConfirmData {
  toolName: string
  riskLevel: string
  arguments: string
}

export interface AgentSseEvent {
  type: AgentSseEventType
  data: Record<string, unknown> & { content?: string; message?: string }
}

export type AgentSseCallback = (event: AgentSseEvent) => void

/** Agent 模式消息请求（对齐现有 /agent/stream 请求体） */
export interface AgentMessagePayload {
  content: string
  enablePlanner?: boolean
  enableReflector?: boolean
  skipConfirmation?: boolean
}

/** tool_result 事件中的被检索记录（数据来源折叠区用；真实事件按同名字段解析） */
export interface RetrievedRecord {
  type: string
  id: number
  title: string
}

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))

// ---------- mock 会话确认队列 ----------
const pendingConfirms = new Map<number, (confirmed: boolean) => void>()

let mockSessionSeq = 0

export const agentApi = {
  /** Agent 模式流式发送消息（真实：SSE fetch；mock：本地模拟事件流） */
  sendAgentMessageStream: async (
    sessionId: number,
    payload: AgentMessagePayload,
    onEvent: AgentSseCallback,
    signal?: AbortSignal,
  ): Promise<void> => {
    if (USE_MOCK) return runMockAgentStream(sessionId, payload, onEvent, signal)

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

    await consumeSseResponseUntilTerminal(
      response,
      (event) => onEvent({ type: event.type as AgentSseEventType, data: (event.data ?? {}) as AgentSseEvent['data'] }),
      signal,
    )
  },

  /** 确认/取消危险工具调用（mock 分支唤醒被 Confirm 暂停的模拟流） */
  confirmToolCall: async (sessionId: number, confirmed: boolean): Promise<void> => {
    if (USE_MOCK) {
      const resolve = pendingConfirms.get(sessionId)
      if (resolve) {
        pendingConfirms.delete(sessionId)
        resolve(confirmed)
      }
      return
    }
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
}

/** mock 会话 ID 生成（指令面板每次打开创建一个 command 会话） */
export function nextMockSessionId(): number {
  return 900000 + ++mockSessionSeq
}

// ============================================================
// 本地模拟 Agent 事件流（视觉稿 ③ 场景复刻）
// ============================================================
async function runMockAgentStream(
  sessionId: number,
  payload: AgentMessagePayload,
  onEvent: AgentSseCallback,
  signal?: AbortSignal,
): Promise<void> {
  const q = payload.content.trim()
  const aborted = () => signal?.aborted === true
  const emit = (type: AgentSseEventType, data: AgentSseEvent['data']) => onEvent({ type, data })

  await sleep(250)
  if (aborted()) return
  emit('user_msg', { id: -Date.now() })

  // 错误态演示
  if (/断网|网络错误|失败演示/.test(q)) {
    await sleep(400)
    emit('error', { message: 'AI 服务暂不可用（DeepSeek 连接超时）。纯数据功能不受影响，请稍后重试。' })
    return
  }

  const dangerous = /删除|删掉|清空|卸载|执行命令|shell/i.test(q)
  const wantsExport = /整理|导出|一页|汇总|文档|docx/i.test(q)

  if (dangerous) {
    await emitTool({
      id: 't1',
      name: 'delete_work_logs',
      args: JSON.stringify({ keyword: q.slice(0, 12), scope: 'work' }, null, 2),
      progressMessage: '高危操作，等待用户确认…',
    })
    emit('confirm', {
      toolName: 'delete_work_logs',
      riskLevel: 'high',
      arguments: JSON.stringify({ keyword: q.slice(0, 12), scope: 'work', expectedCount: 3 }, null, 2),
    })
    const confirmed = await waitConfirm(sessionId)
    if (aborted()) return
    if (confirmed) {
      emit('tool_result', { toolCallId: 't1', name: 'delete_work_logs', result: '已删除 3 条工作记录' })
      await streamTokens('已删除 3 条匹配「' + q.slice(0, 12) + '」的工作记录。删除为软删除，如需恢复请在 24 小时内联系管理员。')
    } else {
      emit('tool_result', { toolCallId: 't1', name: 'delete_work_logs', result: '用户拒绝了此操作。' })
      await streamTokens('好的，已取消删除。如需批量清理，建议先在工作流页面逐条确认后再操作。')
    }
    finish()
    return
  }

  // ---- 常规检索流（视觉稿 ③：search → overview →（可选）export → 正文） ----
  await emitTool({
    id: 't1',
    name: 'search_work_logs',
    args: JSON.stringify({ query: q.slice(0, 16) }),
    progressMessage: '正在检索工作记录…',
  })
  emit('tool_result', {
    toolCallId: 't1',
    name: 'search_work_logs',
    result: '检索到 12 条记录（8/17–8/21）',
    records: [
      { type: 'worklog', id: 238, title: '迁移方案 v3 修订完成' },
      { type: 'worklog', id: 231, title: '安全评审要求' },
      { type: 'worklog', id: 226, title: '数据迁移校验' },
    ] satisfies RetrievedRecord[],
  })

  await emitTool({
    id: 't2',
    name: 'get_record_overview',
    args: JSON.stringify({ ids: [238, 231, 226] }),
    progressMessage: '正在汇总记录要点…',
  })
  emit('tool_result', {
    toolCallId: 't2',
    name: 'get_record_overview',
    result: '已生成 3 条记录的结构化概览',
    records: [
      { type: 'worklog', id: 226, title: '数据迁移校验' },
    ] satisfies RetrievedRecord[],
  })

  if (wantsExport) {
    await emitTool({
      id: 't3',
      name: 'export_file',
      args: JSON.stringify({ format: 'docx', title: '迁移工作周整理' }),
      progressMessage: 'DOCX 生成中…',
      longRunning: true,
    })
    emit('tool_result', {
      toolCallId: 't3',
      name: 'export_file',
      result: '《迁移工作周整理.docx》已生成',
      downloadUrl: '#/mock-download/迁移工作周整理.docx',
    })
  }

  await streamTokens(standardAnswer(q, wantsExport))
  finish()

  // ---- helpers ----
  async function emitTool(o: { id: string; name: string; args: string; progressMessage: string; longRunning?: boolean }) {
    await sleep(450)
    if (aborted()) throw new AbortError()
    emit('tool_call', { id: o.id, name: o.name, arguments: o.args })
    await sleep(o.longRunning ? 1200 : 700)
    if (aborted()) throw new AbortError()
    emit('tool_progress', { id: o.id, name: o.name, message: o.progressMessage, elapsedSeconds: 1 })
  }

  async function streamTokens(text: string) {
    // 逐段吐 token（2–5 字/段），验证流式渲染与光标动画
    let i = 0
    while (i < text.length) {
      if (aborted()) throw new AbortError()
      const size = 2 + Math.floor(Math.random() * 4)
      emit('token', { content: text.slice(i, i + size) })
      i += size
      await sleep(45)
    }
  }

  function finish() {
    emit('done', {
      messageId: -Date.now() - 1,
      content: standardAnswer(q, wantsExport),
      title: q.slice(0, 16),
      createdAt: new Date().toISOString(),
    })
  }
}

function standardAnswer(q: string, withExport: boolean): string {
  const topic = q.replace(/整理|成|一页|导出|汇总/g, '').slice(0, 8) || '迁移'
  const exportLine = withExport ? '\n\n文档已生成：《迁移工作周整理.docx》（见下方链接）。' : ''
  return (
    `上周（8/17–8/21）与${topic}相关共 **12 条记录**，归纳为三块：\n\n` +
    '**1. 方案演进** —— v2 评审意见（8/18）→ v3 修订完成（8/21）；\n\n' +
    '**2. 安全评审** —— 老王要求周三前排期，授权改造为阻塞项（#231）；\n\n' +
    '**3. 数据迁移** —— 存量数据校验脚本完成，差异 37 条待处理（#226）；' +
    exportLine
  )
}

function waitConfirm(sessionId: number): Promise<boolean> {
  return new Promise((resolve) => pendingConfirms.set(sessionId, resolve))
}

class AbortError extends Error {
  constructor() {
    super('aborted')
    this.name = 'AbortError'
  }
}
