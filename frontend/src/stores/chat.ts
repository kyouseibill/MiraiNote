import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatSession, ChatSessionDetail, ChatMessage } from '@/types/chat'
import { chatApi } from '@/api/chat'
import type { SseEventType } from '@/api/chat'

export const useChatStore = defineStore('chat', () => {
  const sessions = ref<ChatSession[]>([])
  const currentSession = ref<ChatSessionDetail | null>(null)
  const loading = ref(false)
  const sending = ref(false)

  /**
   * 当前 AI 回复中用于流式显示的临时消息对象。
   * 当流式开始时创建，结束后替换为服务器返回的正式消息。
   */
  const streamMessage = ref<ChatMessage | null>(null)

  // 当前正在进行的工具调用描述
  const currentToolCall = ref<string>('')

  async function fetchSessions() {
    loading.value = true
    try {
      sessions.value = await chatApi.getSessions()
    } finally {
      loading.value = false
    }
  }

  async function openSession(sessionId: number) {
    loading.value = true
    try {
      currentSession.value = await chatApi.getSession(sessionId)
    } finally {
      loading.value = false
    }
  }

  async function createSession(title?: string) {
    const session = await chatApi.createSession({ title })
    sessions.value.unshift(session)
    currentSession.value = { ...session, messages: [] }
    return session
  }

  async function deleteSession(sessionId: number) {
    await chatApi.deleteSession(sessionId)
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)
    if (currentSession.value?.id === sessionId) currentSession.value = null
  }

  async function sendMessage(content: string) {
    if (!currentSession.value) return
    const sessionId = currentSession.value.id

    // 乐观更新：先追加用户消息（临时 ID）
    const tempUserMsg: ChatMessage = {
      id: -Date.now(),
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }
    currentSession.value.messages.push(tempUserMsg)

    sending.value = true
    try {
      const assistantMsg = await chatApi.sendMessage(sessionId, { content })
      // 非流式模式：追加 AI 回复
      currentSession.value.messages.push(assistantMsg)
      await fetchSessions()
    } finally {
      sending.value = false
    }
  }

  /**
   * 流式发送消息。
   * 通过 SSE 接收逐 token 推送，实时更新界面。
   */
  async function sendMessageStream(content: string) {
    if (!currentSession.value) return
    const sessionId = currentSession.value.id

    sending.value = true
    currentToolCall.value = ''

    // 1. 添加临时用户消息
    const tempUserMsg: ChatMessage = {
      id: -Date.now(),
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }
    currentSession.value.messages.push(tempUserMsg)

        // 2. 创建流式 AI 回复占位（不 push 到 messages 中，由独立模板区块渲染）
    streamMessage.value = {
      id: -Date.now() - 1,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }

        // 3. 用 SSE 向服务器发送请求
    try {
      await chatApi.sendMessageStream(sessionId, { content }, (event) => {
        if (!currentSession.value) return

        switch (event.type) {
          case 'user_msg':
            // 用户消息已持久化，替换临时 ID
            const userIdx = currentSession.value.messages.findIndex(
              (m) => m.id === tempUserMsg.id,
            )
            if (userIdx >= 0) {
              currentSession.value.messages[userIdx].id = event.data.id
            }
            break

          case 'token':
            // 追加文本 token
            if (streamMessage.value) {
              streamMessage.value.content += event.data.content
            }
            break

          case 'tool_call':
            // 显示工具调用提示
            currentToolCall.value = `🔧 正在${getToolLabel(event.data.name)}…`
            break

          case 'tool_result':
            // 工具执行完成，清除提示
            currentToolCall.value = ''
            break

          case 'done':
            // AI 回复完成：将 streamMessage 转为正式消息加入列表
            if (streamMessage.value && currentSession.value) {
              const finalMsg: ChatMessage = {
                id: event.data.messageId,
                role: 'assistant',
                content: streamMessage.value.content,
                createdAt: event.data.createdAt || streamMessage.value.createdAt,
              }
              currentSession.value.messages.push(finalMsg)
              streamMessage.value = null
            }
            // 更新会话列表（标题可能已更改）
            const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
            if (sessionIdx >= 0) {
              sessions.value[sessionIdx].title = event.data.title
            }
            if (currentSession.value) {
              currentSession.value.title = event.data.title
            }
            break

          case 'error':
            // 出错时清除 streamMessage 占位（它不在 messages 中，无需移除）
            streamMessage.value = null
            break
        }
      })
        } catch (e) {
      // 网络错误或流中断：清除 streamMessage 占位（不在 messages 中）
      streamMessage.value = null
    } finally {
      sending.value = false
      currentToolCall.value = ''
      streamMessage.value = null
    }
  }

  async function updateTitle(sessionId: number, title: string) {
    const updated = await chatApi.updateTitle(sessionId, title)
    const idx = sessions.value.findIndex((s) => s.id === sessionId)
    if (idx >= 0) sessions.value[idx] = updated
    if (currentSession.value?.id === sessionId) currentSession.value.title = updated.title
    return updated
  }

  /** 工具名称 → 中文描述 */
  function getToolLabel(name: string): string {
    const labels: Record<string, string> = {
      search_work_logs: '查询工作记录',
      search_memos: '查询备忘',
      search_life_logs: '查询生活记录',
      get_weekly_reports: '获取周报',
      search_internet: '搜索互联网',
      create_work_log: '创建工作记录',
      update_work_log: '更新工作记录',
      delete_work_log: '删除工作记录',
      create_memo: '创建备忘',
      update_memo: '更新备忘',
      patch_memo_status: '更新备忘状态',
      delete_memo: '删除备忘',
      create_life_log: '创建生活记录',
      update_life_log: '更新生活记录',
      delete_life_log: '删除生活记录',
    }
    return labels[name] || name
  }

  return {
    sessions,
    currentSession,
    loading,
    sending,
    streamMessage,
    currentToolCall,
    fetchSessions,
    openSession,
    createSession,
    deleteSession,
    sendMessage,
    sendMessageStream,
    updateTitle,
  }
})
