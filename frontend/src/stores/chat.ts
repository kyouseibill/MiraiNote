import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatSession, ChatSessionDetail, ChatMessage, ChatAttachmentContent } from '@/types/chat'
import { chatApi } from '@/api/chat'
import { agentApi, type AgentPlanData } from '@/api/agent'
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

  // Agent 控制开关
  const enablePlanner = ref(true)
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

  const sessionDetailsCache = new Map<number, ChatSessionDetail>()

  async function fetchSessions() {
    loading.value = true
    try {
      sessions.value = await chatApi.getSessions()
    } finally {
      loading.value = false
    }
  }

  async function openSession(sessionId: number) {
    const cached = sessionDetailsCache.get(sessionId)
    if (cached) {
      loading.value = false
      currentSession.value = cached
      chatApi.getSession(sessionId)
        .then((fresh) => {
          // 若该会话正在流式生成回复，跳过本次刷新：
          // 避免用不含新消息的旧数据覆盖 currentSession，导致回复完成后界面显示空白
          // （需重新进入会话才能看到内容）。
          if (streamSessionId.value === sessionId) return
          sessionDetailsCache.set(sessionId, fresh)
          if (currentSession.value?.id === sessionId) {
            currentSession.value = fresh
          }
        })
        .catch(() => {
          // 保留缓存内容，避免后台刷新失败影响切换体验。
        })
      return
    }

    loading.value = true
    try {
      const detail = await chatApi.getSession(sessionId)
      sessionDetailsCache.set(sessionId, detail)
      currentSession.value = detail
    } finally {
      loading.value = false
    }
  }

  async function createSession(title?: string) {
    const session = await chatApi.createSession({ title })
    sessions.value.unshift(session)
    const detail: ChatSessionDetail = { ...session, messages: [] }
    sessionDetailsCache.set(session.id, detail)
    currentSession.value = detail
    return session
  }

  async function deleteSession(sessionId: number) {
    await chatApi.deleteSession(sessionId)
    sessionDetailsCache.delete(sessionId)
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)
    if (currentSession.value?.id === sessionId) currentSession.value = null
  }

  async function archiveSession(sessionId: number) {
    await chatApi.archiveSession(sessionId)
    sessionDetailsCache.delete(sessionId)
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)
    if (currentSession.value?.id === sessionId) currentSession.value = null
  }

  // 归档管理：仅在打开归档管理面板时按需加载，避免每次进入页面都拉取归档内容。
  const archivedSessions = ref<ChatSession[]>([])
  const archivedLoading = ref(false)

  async function fetchArchivedSessions() {
    archivedLoading.value = true
    try {
      archivedSessions.value = await chatApi.getArchivedSessions()
    } finally {
      archivedLoading.value = false
    }
  }

  async function unarchiveSession(sessionId: number) {
    await chatApi.unarchiveSession(sessionId)
    archivedSessions.value = archivedSessions.value.filter((s) => s.id !== sessionId)
    await fetchSessions()
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
      sessionDetailsCache.set(sessionId, currentSession.value)
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
    let persistedUserMessage = false
    let shouldRestoreAttachments = false
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
    const activeStreamMessage: ChatMessage = {
      id: -Date.now() - 1,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }
    streamMessage.value = activeStreamMessage
    let streamedContent = ''

    // 3. 用 SSE 向服务器发送请求
    try {
      await chatApi.sendMessageStream(
        sessionId,
        { content, attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined },
        (event) => {
        switch (event.type) {
          case 'user_msg':
            persistedUserMessage = true
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
            streamedContent += String(event.data?.content ?? '')
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value.content = streamedContent
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
            // done 自带完整正文。即使临时渲染对象被刷新/替换，也能可靠落入消息列表。
            const finalMsg: ChatMessage = {
              id: event.data.messageId,
              role: 'assistant',
              content: streamedContent || String(event.data?.content ?? ''),
              createdAt: event.data.createdAt || activeStreamMessage.createdAt,
            }
            const finalIdx = targetSession.messages.findIndex((m) => m.id === finalMsg.id)
            if (finalIdx >= 0) {
              targetSession.messages[finalIdx] = finalMsg
            } else {
              targetSession.messages.push(finalMsg)
            }
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            // 更新会话列表（标题可能已更改）
            const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
            if (sessionIdx >= 0) {
              sessions.value[sessionIdx].title = event.data.title
            }
            targetSession.title = event.data.title
            sessionDetailsCache.set(sessionId, targetSession)
            break

          case 'error':
            // 出错时清除 streamMessage 占位，给用户提示
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            shouldRestoreAttachments = !persistedUserMessage
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      })
    } catch (e) {
      // 网络错误或流中断：清除 streamMessage 占位（不在 messages 中）
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      shouldRestoreAttachments = !persistedUserMessage
      toast.error('网络连接失败，请检查后端服务是否正常运行')
    } finally {
      if (shouldRestoreAttachments && attachmentsToSend.length > 0) {
        pendingAttachments.value = [...attachmentsToSend, ...pendingAttachments.value]
      }
      sending.value = false
      currentToolCall.value = ''
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      if (streamSessionId.value === sessionId) {
        streamSessionId.value = null
      }
      sessionDetailsCache.set(sessionId, targetSession)
      // 兜底：若流式过程中 currentSession 被其他逻辑替换为同一会话的不同对象，
      // 这里强制恢复为包含完整回复内容的 targetSession，避免界面显示空白。
      if (currentSession.value?.id === sessionId && currentSession.value !== targetSession) {
        currentSession.value = targetSession
      }
    }
  }

  async function updateTitle(sessionId: number, title: string) {
    const updated = await chatApi.updateTitle(sessionId, title)
    const idx = sessions.value.findIndex((s) => s.id === sessionId)
    if (idx >= 0) sessions.value[idx] = updated
    if (currentSession.value?.id === sessionId) currentSession.value.title = updated.title
    const cached = sessionDetailsCache.get(sessionId)
    if (cached) cached.title = updated.title
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
    contextUsage.value = null
    pendingConfirm.value = null
    streamSessionId.value = sessionId

    const attachmentsToSend = [...pendingAttachments.value]
    pendingAttachments.value = []
    let persistedUserMessage = false
    let shouldRestoreAttachments = false
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

    const activeStreamMessage: ChatMessage = {
      id: -Date.now() - 1,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }
    streamMessage.value = activeStreamMessage
    let streamedContent = ''

    try {
      await agentApi.sendAgentMessageStream(
        sessionId,
        {
          content,
          enablePlanner: enablePlanner.value,
          enableReflector: false,
          skipConfirmation: autoMode.value,
          attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined,
        },
        (event) => {
        switch (event.type) {
          case 'user_msg':
            persistedUserMessage = true
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
            streamedContent += String(event.data?.content ?? '')
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value.content = streamedContent
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

          case 'context':
            contextUsage.value = event.data
            break

          case 'done':
            const finalMsg: ChatMessage = {
              id: event.data.messageId,
              role: 'assistant',
              content: streamedContent || String(event.data?.content ?? ''),
              createdAt: event.data.createdAt || activeStreamMessage.createdAt,
            }
            const finalIdx = targetSession.messages.findIndex((m) => m.id === finalMsg.id)
            if (finalIdx >= 0) {
              targetSession.messages[finalIdx] = finalMsg
            } else {
              targetSession.messages.push(finalMsg)
            }
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
            if (sessionIdx >= 0) {
              sessions.value[sessionIdx].title = event.data.title
            }
            targetSession.title = event.data.title
            sessionDetailsCache.set(sessionId, targetSession)
            break

          case 'error':
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            shouldRestoreAttachments = !persistedUserMessage
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      })
    } catch (e) {
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      shouldRestoreAttachments = !persistedUserMessage
      toast.error('网络连接失败')
    } finally {
      if (shouldRestoreAttachments && attachmentsToSend.length > 0) {
        pendingAttachments.value = [...attachmentsToSend, ...pendingAttachments.value]
      }
      sending.value = false
      currentToolCall.value = ''
      toolCalls.value = []
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      if (streamSessionId.value === sessionId) {
        streamSessionId.value = null
      }
      sessionDetailsCache.set(sessionId, targetSession)
      if (currentSession.value?.id === sessionId && currentSession.value !== targetSession) {
        currentSession.value = targetSession
      }
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
      fetch_web_page: '分析网页',
      call_http_api: '调用 API',
      login_and_fetch_web: '登录后访问网页',
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
      get_current_time: '获取当前时间',
      calculate: '执行计算',
      record_overview: '汇总记录概览',
      read_file: '读取文件',
      write_file: '写入文件',
      publish_workspace_file: '展示图片',
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
    enablePlanner,
    autoMode,
    contextUsage,
    pendingConfirm,
    pendingAttachments,
    archivedSessions,
    archivedLoading,
    fetchSessions,
    openSession,
    createSession,
    deleteSession,
    archiveSession,
    fetchArchivedSessions,
    unarchiveSession,
    sendMessage,
    sendMessageStream,
    sendAgentMessageStream,
    confirmToolCall,
    updateTitle,
  }
})
