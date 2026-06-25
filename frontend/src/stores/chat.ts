import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatSession, ChatSessionDetail, ChatMessage, ChatAttachmentContent } from '@/types/chat'
import { chatApi } from '@/api/chat'
import { agentApi, type AgentPlanData, type AgentReflectionData } from '@/api/agent'
import { useToast } from '@/composables/useToast'

export const useChatStore = defineStore('chat', () => {
  const toast = useToast()
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

  // Agent 特有状态
  const agentPlan = ref<AgentPlanData | null>(null)
  const agentReflection = ref<AgentReflectionData | null>(null)
  const useAgentMode = ref(true) // 默认使用 Agent 模式

  // Agent 控制开关
  const enablePlanner = ref(true)
  const enableReflector = ref(true)
  const autoMode = ref(true)

  // 流式回复所属的会话 ID（用于防止切换会话时内容显示到错误会话）
  const streamSessionId = ref<number | null>(null)

  // 并行工具调用列表
  const toolCalls = ref<{ id: string; name: string; label: string }[]>([])

  // 上下文用量
  const contextUsage = ref<{ estimatedTokens: number; maxTokens: number; percentUsed: number; messageCount: number } | null>(null)

  // 危险操作确认
  const pendingConfirm = ref<{ toolName: string; riskLevel: string; arguments: string } | null>(null)
  let pendingConfirmSessionId: number | null = null

  // 待发送的附件列表（用户选择文件后上传解析，随下次发送一起提交给 AI）
  const pendingAttachments = ref<ChatAttachmentContent[]>([])

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
    const targetSession = currentSession.value
    const sessionId = targetSession.id

    sending.value = true
    currentToolCall.value = ''
    streamSessionId.value = sessionId

    // 1. 添加临时用户消息
    const attachmentsToSend = [...pendingAttachments.value]
    pendingAttachments.value = []
    const attachmentNote = attachmentsToSend.length > 0
      ? '\n' + attachmentsToSend.map(a => `📎${a.fileName}`).join(' ')
      : ''
    const tempUserMsg: ChatMessage = {
      id: -Date.now(),
      role: 'user',
      content: content + attachmentNote,
      createdAt: new Date().toISOString(),
    }
    targetSession.messages.push(tempUserMsg)

    // 2. 创建流式 AI 回复占位（不 push 到 messages 中，由独立模板区块渲染）
    streamMessage.value = {
      id: -Date.now() - 1,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }

    // 3. 用 SSE 向服务器发送请求
    try {
      await chatApi.sendMessageStream(
        sessionId,
        { content, attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined },
        (event) => {
        switch (event.type) {
          case 'user_msg':
            // 用户消息已持久化，替换临时 ID
            const userIdx = targetSession.messages.findIndex(
              (m) => m.id === tempUserMsg.id,
            )
            if (userIdx >= 0) {
              targetSession.messages[userIdx].id = event.data.id
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
            if (streamMessage.value) {
              const finalMsg: ChatMessage = {
                id: event.data.messageId,
                role: 'assistant',
                content: streamMessage.value.content,
                createdAt: event.data.createdAt || streamMessage.value.createdAt,
              }
              targetSession.messages.push(finalMsg)
              streamMessage.value = null
            }
            // 更新会话列表（标题可能已更改）
            const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
            if (sessionIdx >= 0) {
              sessions.value[sessionIdx].title = event.data.title
            }
            targetSession.title = event.data.title
            break

          case 'error':
            // 出错时清除 streamMessage 占位，给用户提示
            streamMessage.value = null
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      })
    } catch (e) {
      // 网络错误或流中断：清除 streamMessage 占位（不在 messages 中）
      streamMessage.value = null
      toast.error('网络连接失败，请检查后端服务是否正常运行')
    } finally {
      sending.value = false
      currentToolCall.value = ''
      streamMessage.value = null
      streamSessionId.value = null
    }
  }

  async function updateTitle(sessionId: number, title: string) {
    const updated = await chatApi.updateTitle(sessionId, title)
    const idx = sessions.value.findIndex((s) => s.id === sessionId)
    if (idx >= 0) sessions.value[idx] = updated
    if (currentSession.value?.id === sessionId) currentSession.value.title = updated.title
    return updated
  }

  /**
   * Agent 模式流式发送消息。
   * 包含 Plan → Execute → Reflect 完整流程。
   */
  async function sendAgentMessageStream(content: string) {
    if (!currentSession.value) return
    const targetSession = currentSession.value
    const sessionId = targetSession.id

    sending.value = true
    currentToolCall.value = ''
    toolCalls.value = []
    agentPlan.value = null
    agentReflection.value = null
    contextUsage.value = null
    pendingConfirm.value = null
    streamSessionId.value = sessionId

    const attachmentsToSend = [...pendingAttachments.value]
    pendingAttachments.value = []
    const agentAttachmentNote = attachmentsToSend.length > 0
      ? '\n' + attachmentsToSend.map(a => `📎${a.fileName}`).join(' ')
      : ''

    const tempUserMsg: ChatMessage = {
      id: -Date.now(),
      role: 'user',
      content: content + agentAttachmentNote,
      createdAt: new Date().toISOString(),
    }
    targetSession.messages.push(tempUserMsg)

    streamMessage.value = {
      id: -Date.now() - 1,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }

    try {
      await agentApi.sendAgentMessageStream(
        sessionId,
        {
          content,
          enablePlanner: enablePlanner.value,
          enableReflector: enableReflector.value,
          skipConfirmation: autoMode.value,
          attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined,
        },
        (event) => {
        switch (event.type) {
          case 'user_msg':
            const userIdx = targetSession.messages.findIndex(
              (m) => m.id === tempUserMsg.id,
            )
            if (userIdx >= 0) {
              targetSession.messages[userIdx].id = event.data.id
            }
            break

          case 'plan':
            agentPlan.value = event.data as AgentPlanData
            break

          case 'token':
            if (streamMessage.value) {
              streamMessage.value.content += event.data.content
            }
            break

          case 'tool_call': {
            const label = getToolLabel(event.data.name)
            currentToolCall.value = `🔧 正在${label}…`
            toolCalls.value.push({
              id: event.data.id || event.data.name,
              name: event.data.name,
              label,
            })
            break
          }

          case 'tool_result':
            currentToolCall.value = ''
            // 移除对应的 tool call 指示器
            if (event.data.toolCallId || event.data.name) {
              const id = event.data.toolCallId || event.data.name
              toolCalls.value = toolCalls.value.filter(t => t.id !== id && t.name !== id)
            } else {
              toolCalls.value = []
            }
            break

          case 'confirm':
            // 暂停流，等待用户确认
            pendingConfirmSessionId = sessionId
            pendingConfirm.value = {
              toolName: event.data.toolName,
              riskLevel: event.data.riskLevel,
              arguments: event.data.arguments,
            }
            break

          case 'reflection':
            agentReflection.value = event.data as AgentReflectionData
            break

          case 'context':
            contextUsage.value = event.data
            break

          case 'done':
            if (streamMessage.value) {
              const finalMsg: ChatMessage = {
                id: event.data.messageId,
                role: 'assistant',
                content: streamMessage.value.content,
                createdAt: event.data.createdAt || streamMessage.value.createdAt,
              }
              targetSession.messages.push(finalMsg)
              streamMessage.value = null
            }
            const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
            if (sessionIdx >= 0) {
              sessions.value[sessionIdx].title = event.data.title
            }
            targetSession.title = event.data.title
            break

          case 'error':
            streamMessage.value = null
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      })
    } catch (e) {
      streamMessage.value = null
      toast.error('网络连接失败')
    } finally {
      sending.value = false
      currentToolCall.value = ''
      toolCalls.value = []
      streamMessage.value = null
      streamSessionId.value = null
    }
  }

  /** 用户确认/取消危险操作 */
  async function confirmToolCall(confirmed: boolean) {
    const sid = pendingConfirmSessionId
    pendingConfirm.value = null
    pendingConfirmSessionId = null
    if (sid != null) {
      try {
        await agentApi.confirmToolCall(sid, confirmed)
      } catch {
        // 忽略网络错误
      }
    }
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
      remember: '存储记忆',
      recall: '检索记忆',
      forget: '删除记忆',
      get_weather: '查询天气',
      send_email: '发送邮件',
      export_file: '导出文件',
      query_calendar: '日期计算',
      read_file: '读取文件',
      write_file: '写入文件',
      list_files: '浏览目录',
      run_shell: '执行命令',
    }
    return labels[name] || name
  }

  return {
    sessions,
    currentSession,
    loading,
    sending,
    streamMessage,
    streamSessionId,
    currentToolCall,
    toolCalls,
    agentPlan,
    agentReflection,
    useAgentMode,
    enablePlanner,
    enableReflector,
    autoMode,
    contextUsage,
    pendingConfirm,
    pendingAttachments,
    fetchSessions,
    openSession,
    createSession,
    deleteSession,
    sendMessage,
    sendMessageStream,
    sendAgentMessageStream,
    confirmToolCall,
    updateTitle,
  }
})
