import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {
  ChatSession,
  ChatSessionDetail,
  ChatMessage,
  ChatAttachmentContent,
  ChatProject,
  ChatProjectPayload,
  BranchSessionPayload,
  ToolCallEvent,
} from '@/types/chat'
import { chatApi } from '@/api/chat'
import { agentApi } from '@/api/agent'
import { useToast } from '@/composables/useToast'

export type ChatSendOutcome = 'completed' | 'stopped' | 'failed'

export const useChatStore = defineStore('chat', () => {
  const toast = useToast()
  const sessions = ref<ChatSession[]>([])
  const currentSession = ref<ChatSessionDetail | null>(null)
  const isTemporary = ref(false)
  const temporaryId = ref(createTemporaryId())
  const loading = ref(false)
  const sessionsLoading = ref(false)
  const sending = ref(false)
  const lastSendError = ref('')
  const projects = ref<ChatProject[]>([])
  const selectedProjectId = ref<number | null>(null)
  let activeAbortController: AbortController | null = null
  /** 持久化 Agent 运行 ID；SSE 断开时任务仍由服务端继续。 */
  let activeAgentRunId: string | null = null
  const recoverableAgentRunId = ref<string | null>(null)
  /** 递增世代号：停止/新发送后丢弃旧 SSE 事件，防止旧 run 继续写 UI。 */
  let activeRunId = 0

  /**
   * 当前 AI 回复中用于流式显示的临时消息对象。
   * 当流式开始时创建，结束后替换为服务器返回的正式消息。
   */
  const streamMessage = ref<ChatMessage | null>(null)

  // 当前正在进行的工具调用描述
  const currentToolCall = ref<string>('')

  // Agent 控制开关
  const autoMode = ref(true)

  // 流式回复所属的会话 ID（用于防止切换会话时内容显示到错误会话）
  const streamSessionId = ref<number | null>(null)

  // 并行/串行工具调用事件（含已完成，供 Work 模式事件卡展示）
  const toolCalls = ref<ToolCallEvent[]>([])

  // 上下文用量
  const contextUsage = ref<{ estimatedTokens: number; maxTokens: number; percentUsed: number; messageCount: number } | null>(null)

  // 危险操作确认
  const pendingConfirm = ref<{ toolName: string; riskLevel: string; arguments: string } | null>(null)
  let pendingConfirmSessionId: number | null = null
  let pendingConfirmTemporaryId: string | null = null
  let nextTemporaryMessageId = -1_000_000

  // 待发送的附件列表（用户选择文件后上传解析，随下次发送一起提交给 AI）
  const pendingAttachments = ref<ChatAttachmentContent[]>([])
  // 附件和文字草稿一样属于具体会话。切换会话、临时聊天或分支时不能把它带到别处。
  const attachmentDrafts = new Map<string, ChatAttachmentContent[]>()

  const sessionDetailsCache = new Map<number, ChatSessionDetail>()
  /**
   * 工具事件卡仅在本页生命周期内回放；硬刷新后为空。
   * 不随 getSession API 历史返回，也不写入 DB。
   */
  const pageToolEventsByMessageId = new Map<number, ToolCallEvent[]>()
  let sessionsRequestVersion = 0
  let selectionVersion = 0

  function attachmentDraftKey(
    session = currentSession.value,
    temporary = isTemporary.value,
    temporarySessionId = temporaryId.value,
  ) {
    if (!session) return null
    return temporary ? `temporary:${temporarySessionId}` : `session:${session.id}`
  }


  /** UI/历史展示只读本页 map；永不从 API 消息字段回填。 */
  function pageToolEventsFor(messageId: number): ToolCallEvent[] {
    return pageToolEventsByMessageId.get(messageId) ?? []
  }

  /** 始终剥离 API/缓存载荷上的 toolEvents；不把 page map 写回 message。 */
  function stripToolEventsFromHistory(detail: ChatSessionDetail): ChatSessionDetail {
    return {
      ...detail,
      messages: detail.messages.map((message) => {
        const { toolEvents: _ignored, ...rest } = message
        return { ...rest }
      }),
    }
  }

  function rememberPageToolEvents(messageId: number, events: ToolCallEvent[] | undefined) {
    if (!events || events.length === 0) {
      pageToolEventsByMessageId.delete(messageId)
      return
    }
    pageToolEventsByMessageId.set(messageId, events.map((e) => ({ ...e })))
  }

  function stripToolEventsFromMessages(messages: ChatMessage[]): ChatMessage[] {
    return messages.map((message) => {
      const { toolEvents: _ignored, ...rest } = message
      return { ...rest }
    })
  }

  /** bfcache/软恢复：工具卡只属当前页生命周期，恢复时清空。 */
  function clearPageLifetimeToolState() {
    pageToolEventsByMessageId.clear()
    for (const [id, detail] of sessionDetailsCache.entries()) {
      sessionDetailsCache.set(id, {
        ...detail,
        messages: stripToolEventsFromMessages(detail.messages),
      })
    }
    if (currentSession.value) {
      currentSession.value = {
        ...currentSession.value,
        messages: stripToolEventsFromMessages(currentSession.value.messages),
      }
    }
  }

  if (typeof window !== 'undefined') {
    window.addEventListener('pageshow', (event: PageTransitionEvent) => {
      if (event.persisted) clearPageLifetimeToolState()
    })
  }

  function savePendingAttachments() {
    const key = attachmentDraftKey()
    if (key) attachmentDrafts.set(key, [...pendingAttachments.value])
  }

  function restorePendingAttachments() {
    const key = attachmentDraftKey()
    pendingAttachments.value = key ? [...(attachmentDrafts.get(key) ?? [])] : []
  }

  async function fetchSessions() {
    const requestVersion = ++sessionsRequestVersion
    sessionsLoading.value = true
    try {
      const result = await chatApi.getSessions(selectedProjectId.value)
      if (requestVersion === sessionsRequestVersion) sessions.value = result
    } finally {
      if (requestVersion === sessionsRequestVersion) sessionsLoading.value = false
    }
  }

  async function openSession(sessionId: number) {
    savePendingAttachments()
    const requestVersion = ++selectionVersion
    isTemporary.value = false
    const cached = sessionDetailsCache.get(sessionId)
    if (cached) {
      loading.value = false
      currentSession.value = cached
      restorePendingAttachments()
      chatApi.getSession(sessionId)
        .then((fresh) => {
          if (requestVersion !== selectionVersion) return
          // 若该会话正在流式生成回复，跳过本次刷新：
          // 避免用不含新消息的旧数据覆盖 currentSession，导致回复完成后界面显示空白
          // （需重新进入会话才能看到内容）。
          if (streamSessionId.value === sessionId) return
          const normalized = stripToolEventsFromHistory(fresh)
          sessionDetailsCache.set(sessionId, normalized)
          if (currentSession.value?.id === sessionId) {
            currentSession.value = normalized
          }
        })
        .catch(() => {
          // 保留缓存内容，避免后台刷新失败影响切换体验。
        })
      return
    }

    loading.value = true
    try {
      const detail = stripToolEventsFromHistory(await chatApi.getSession(sessionId))
      if (requestVersion !== selectionVersion) return
      sessionDetailsCache.set(sessionId, detail)
      currentSession.value = detail
      restorePendingAttachments()
    } finally {
      if (requestVersion === selectionVersion) loading.value = false
    }
  }

  async function createSession(title?: string) {
    const startedWithoutSession = currentSession.value == null
    const detachedAttachments = startedWithoutSession ? [...pendingAttachments.value] : []
    savePendingAttachments()
    const requestVersion = ++selectionVersion
    loading.value = false
    isTemporary.value = false
    const session = await chatApi.createSession({ title, projectId: selectedProjectId.value })
    if (selectedProjectId.value == null || session.projectId === selectedProjectId.value) {
      sessions.value.unshift(session)
    }
    const detail: ChatSessionDetail = { ...session, messages: [] }
    sessionDetailsCache.set(session.id, detail)
    if (requestVersion === selectionVersion) {
      currentSession.value = detail
      if (startedWithoutSession) {
        pendingAttachments.value = detachedAttachments
        attachmentDrafts.set(`session:${detail.id}`, detachedAttachments)
      } else {
        restorePendingAttachments()
      }
    }
    return session
  }

  async function searchSessions(query: string) {
    const normalized = query.trim()
    if (!normalized) {
      await fetchSessions()
      return
    }
    const requestVersion = ++sessionsRequestVersion
    sessionsLoading.value = true
    try {
      const result = await chatApi.searchSessions(normalized, selectedProjectId.value)
      if (requestVersion === sessionsRequestVersion) sessions.value = result
    } finally {
      if (requestVersion === sessionsRequestVersion) sessionsLoading.value = false
    }
  }

  async function selectProject(projectId: number | null) {
    savePendingAttachments()
    ++selectionVersion
    loading.value = false
    isTemporary.value = false
    selectedProjectId.value = projectId
    currentSession.value = null
    restorePendingAttachments()
    await fetchSessions()
  }

  async function fetchProjects() {
    projects.value = await chatApi.getProjects()
  }

  function startTemporarySession() {
    savePendingAttachments()
    ++selectionVersion
    loading.value = false
    const now = new Date().toISOString()
    isTemporary.value = true
    temporaryId.value = createTemporaryId()
    nextTemporaryMessageId = -1_000_000
    currentSession.value = {
      id: 0,
      title: '临时聊天',
      isArchived: false,
      isPinned: false,
      projectId: null,
      branchedFromSessionId: null,
      branchedFromMessageId: null,
      messages: [],
      createdAt: now,
      updatedAt: now,
    }
    streamMessage.value = null
    streamSessionId.value = null
    currentToolCall.value = ''
    toolCalls.value = []
    contextUsage.value = null
    pendingConfirm.value = null
    pendingConfirmSessionId = null
    pendingConfirmTemporaryId = null
    restorePendingAttachments()
  }

  function replaceSessionSummary(updated: ChatSession) {
    const index = sessions.value.findIndex((session) => session.id === updated.id)
    if (index >= 0) sessions.value[index] = updated
  }

  async function setSessionPinned(sessionId: number, isPinned: boolean) {
    const updated = await chatApi.setPinned(sessionId, isPinned)
    replaceSessionSummary(updated)
    await fetchSessions()
  }

  async function assignSessionProject(sessionId: number, projectId: number | null) {
    const updated = await chatApi.assignProject(sessionId, projectId)
    const cached = sessionDetailsCache.get(sessionId)
    if (cached) cached.projectId = updated.projectId
    if (currentSession.value?.id === sessionId) currentSession.value.projectId = updated.projectId
    await Promise.all([fetchSessions(), fetchProjects()])
  }

  async function branchSession(payload: BranchSessionPayload = {}) {
    if (!currentSession.value || isTemporary.value) return null
    savePendingAttachments()
    const detail = await chatApi.branchSession(currentSession.value.id, payload)
    isTemporary.value = false
    const normalized = stripToolEventsFromHistory(detail)
    sessionDetailsCache.set(normalized.id, normalized)
    currentSession.value = normalized
    restorePendingAttachments()
    await fetchSessions()
    return normalized
  }

  async function createProject(payload: ChatProjectPayload) {
    const project = await chatApi.createProject(payload)
    projects.value.push(project)
    return project
  }

  async function updateProject(projectId: number, payload: ChatProjectPayload) {
    const project = await chatApi.updateProject(projectId, payload)
    const index = projects.value.findIndex((item) => item.id === projectId)
    if (index >= 0) projects.value[index] = project
    return project
  }

  async function deleteProject(projectId: number) {
    await chatApi.deleteProject(projectId)
    projects.value = projects.value.filter((item) => item.id !== projectId)
    if (selectedProjectId.value === projectId) await selectProject(null)
  }

  function stopGeneration() {
    const wasSending = sending.value || !!activeAbortController
    const session = currentSession.value
    const temporary = isTemporary.value
    const tempId = temporaryId.value
    const partial = streamMessage.value?.content?.trim() ?? ''
    const streamCreatedAt = streamMessage.value?.createdAt || new Date().toISOString()
    // 先作废当前 run，再 abort；即使旧 SSE 仍有缓冲帧也不会写入 UI。
    activeRunId += 1
    activeAbortController?.abort()
    activeAbortController = null
    pendingConfirm.value = null
    pendingConfirmSessionId = null
    pendingConfirmTemporaryId = null
    const stoppedEvents = snapshotToolEvents()
    currentToolCall.value = ''
    toolCalls.value = []
    streamMessage.value = null
    streamSessionId.value = null
    sending.value = false

    // 本地立即封闭本轮，避免下一条短消息在 UI/临时历史上挂靠旧长任务。
    // 持久会话服务端还会再写一条「已停止」；随后 openSession 刷新会对齐 id。
    if (wasSending && session) {
      const content = partial ? `${partial}\n\n（已停止）` : '（已停止）'
      const stoppedMsg: ChatMessage = {
        id: temporary ? nextTemporaryMessageId-- : -Date.now(),
        role: 'assistant',
        content,
        createdAt: streamCreatedAt,
        toolEvents: stoppedEvents,
      }
      rememberPageToolEvents(stoppedMsg.id, stoppedEvents)
      session.messages.push(stoppedMsg)
      if (!temporary) sessionDetailsCache.set(session.id, session)

      const stopPromise = temporary
        ? chatApi.stopTemporaryGeneration(tempId)
        : activeAgentRunId
          ? agentApi.stopRun(activeAgentRunId)
          : chatApi.stopSessionGeneration(session.id)
      void stopPromise.catch(() => { /* 忽略网络错误，本地已停止 */ })
    }
    if (wasSending) toast.info('已停止')
  }

  async function deleteSession(sessionId: number) {
    await chatApi.deleteSession(sessionId)
    sessionDetailsCache.delete(sessionId)
    attachmentDrafts.delete(`session:${sessionId}`)
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)
    if (currentSession.value?.id === sessionId) {
      currentSession.value = null
      restorePendingAttachments()
    }
  }

  async function archiveSession(sessionId: number) {
    await chatApi.archiveSession(sessionId)
    sessionDetailsCache.delete(sessionId)
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)
    if (currentSession.value?.id === sessionId) {
      savePendingAttachments()
      currentSession.value = null
      restorePendingAttachments()
    }
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
  async function sendMessageStream(content: string): Promise<ChatSendOutcome> {
    if (!currentSession.value) return 'failed'
    lastSendError.value = ''
    const result: { outcome: ChatSendOutcome } = { outcome: 'failed' }
    const targetSession = currentSession.value
    const sessionId = targetSession.id
    const temporary = isTemporary.value
    const sendingTemporaryId = temporaryId.value
    const temporaryHistory = temporary
      ? targetSession.messages.map(({ role, content }) => ({ role, content }))
      : []

    // 新发送先中止旧本地流；服务端旧 run 由 ChatSessionRunGate.Enter 抢占取消。
    activeAbortController?.abort()
    activeAbortController = null
    activeAgentRunId = null

    sending.value = true
    currentToolCall.value = ''
    toolCalls.value = []
    streamSessionId.value = sessionId

    // 1. 添加临时用户消息
    const sendingAttachmentDraftKey = attachmentDraftKey(targetSession, temporary, sendingTemporaryId)
    const attachmentsToSend = [...pendingAttachments.value]
    pendingAttachments.value = []
    if (sendingAttachmentDraftKey) attachmentDrafts.set(sendingAttachmentDraftKey, [])
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
    const exportedFiles: ExportedFileLink[] = []
    const abortController = new AbortController()
    activeAbortController = abortController
    const runId = ++activeRunId

    // 3. 用 SSE 向服务器发送请求
    try {
      const payload = {
        content,
        attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined,
      }
      const sendStream = temporary
        ? chatApi.sendTemporaryMessageStream(
            { ...payload, history: temporaryHistory },
            (event) => handleEvent(event),
            abortController.signal,
          )
        : chatApi.sendMessageStream(sessionId, payload, (event) => handleEvent(event), abortController.signal)

      function handleEvent(event: { type: string; data: any }) {
        if (runId !== activeRunId || abortController.signal.aborted) return
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

          case 'tool_call': {
            const label = getToolLabel(event.data.name)
            currentToolCall.value = `🔧 正在${label}…`
            upsertRunningToolCall(event.data, label)
            break
          }

          case 'tool_progress': {
            updateToolProgress(event.data)
            currentToolCall.value = String(event.data?.message || '任务仍在处理中…')
            break
          }

          case 'heartbeat':
            if (!streamedContent && !hasRunningToolCalls()) {
              currentToolCall.value = String(event.data?.message || '任务仍在处理，连接正常…')
            }
            break

          case 'tool_result':
            // 工具执行完成：保留事件卡并更新状态，供 Work 模式独立展示
            currentToolCall.value = ''
            collectExportedFile(event.data, exportedFiles)
            completeToolCall(event.data)
            break

          case 'done':
            result.outcome = 'completed'
            // done 自带完整正文。即使临时渲染对象被刷新/替换，也能可靠落入消息列表。
            const finalMsg: ChatMessage = {
              id: temporary ? nextTemporaryMessageId-- : event.data.messageId,
              role: 'assistant',
              content: appendExportedFileLinks(
                preferCompleteContent(streamedContent, String(event.data?.content ?? '')),
                exportedFiles,
              ),
              createdAt: event.data.createdAt || activeStreamMessage.createdAt,
              toolEvents: snapshotToolEvents(),
            }
            rememberPageToolEvents(finalMsg.id, finalMsg.toolEvents)
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
            if (!temporary) {
              const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
              if (sessionIdx >= 0) {
                sessions.value[sessionIdx].title = event.data.title
              }
              targetSession.title = event.data.title
              sessionDetailsCache.set(sessionId, targetSession)
            }
            break

          case 'stopped':
            result.outcome = 'stopped'
            commitStoppedAssistant(
              targetSession,
              temporary,
              streamedContent,
              event.data,
              activeStreamMessage,
              exportedFiles,
            )
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            break
          case 'error':
            result.outcome = 'failed'
            lastSendError.value = event.data?.message || '对话出错，请重试'
            // 出错时清除 streamMessage 占位，给用户提示
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            shouldRestoreAttachments = !persistedUserMessage
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      }
      await sendStream
    } catch (e: any) {
      if (runId !== activeRunId || abortController.signal.aborted) return 'stopped'
      lastSendError.value = '连接意外中断，未收到任务完成结果'
      result.outcome = e?.name === 'AbortError' ? 'stopped' : 'failed'
      const wasExecutingTool = hasRunningToolCalls()
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      shouldRestoreAttachments = !persistedUserMessage
      if (e?.name !== 'AbortError') {
        toast.error(wasExecutingTool
          ? '执行连接意外中断，任务已停止，请重试'
          : '对话连接意外中断，请重试；已发送的用户消息仍会保留')
      }
    } finally {
      if (abortController.signal.aborted && result.outcome !== 'completed') result.outcome = 'stopped'
      if (result.outcome === 'failed' && !persistedUserMessage) {
        const index = targetSession.messages.findIndex((message) => message.id === tempUserMsg.id)
        if (index >= 0) targetSession.messages.splice(index, 1)
      }
      const stillInSendingSession = currentSession.value?.id === sessionId
        && (!temporary || temporaryId.value === sendingTemporaryId)
      if (shouldRestoreAttachments && attachmentsToSend.length > 0) {
        const existing = sendingAttachmentDraftKey
          ? attachmentDrafts.get(sendingAttachmentDraftKey) ?? []
          : []
        const restored = [...attachmentsToSend, ...existing]
        if (sendingAttachmentDraftKey) attachmentDrafts.set(sendingAttachmentDraftKey, restored)
        if (stillInSendingSession) pendingAttachments.value = restored
      }
      if (runId === activeRunId) {
      sending.value = false
      currentToolCall.value = ''
      toolCalls.value = []
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      if (streamSessionId.value === sessionId) {
        streamSessionId.value = null
      }
      if (activeAbortController === abortController) activeAbortController = null
      if (!temporary) sessionDetailsCache.set(sessionId, targetSession)
      // 兜底：若流式过程中 currentSession 被其他逻辑替换为同一会话的不同对象，
      // 这里强制恢复为包含完整回复内容的 targetSession，避免界面显示空白。
      if (currentSession.value?.id === sessionId && currentSession.value !== targetSession) {
        currentSession.value = targetSession
      }
      }
    }
    return result.outcome
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
   * 执行工具调用并流式返回结果。
   */
  async function sendAgentMessageStream(content: string): Promise<ChatSendOutcome> {
    if (!currentSession.value) return 'failed'
    lastSendError.value = ''
    const result: { outcome: ChatSendOutcome } = { outcome: 'failed' }
    const targetSession = currentSession.value
    const sessionId = targetSession.id
    const temporary = isTemporary.value
    const sendingTemporaryId = temporaryId.value
    const temporaryHistory = temporary
      ? targetSession.messages.map(({ role, content }) => ({ role, content }))
      : []

    activeAbortController?.abort()
    activeAbortController = null
    activeAgentRunId = null

    sending.value = true
    currentToolCall.value = ''
    toolCalls.value = []
    contextUsage.value = null
    pendingConfirm.value = null
    streamSessionId.value = sessionId

    const sendingAttachmentDraftKey = attachmentDraftKey(targetSession, temporary, sendingTemporaryId)
    const attachmentsToSend = [...pendingAttachments.value]
    pendingAttachments.value = []
    if (sendingAttachmentDraftKey) attachmentDrafts.set(sendingAttachmentDraftKey, [])
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
    const exportedFiles: ExportedFileLink[] = []
    const abortController = new AbortController()
    activeAbortController = abortController
    const runId = ++activeRunId

    try {
      const payload = {
          content,
          // 执行计划只用于展示，不参与工具执行；关闭可减少一次额外模型调用。
          enablePlanner: false,
          enableReflector: false,
          skipConfirmation: autoMode.value,
          attachments: attachmentsToSend.length > 0 ? attachmentsToSend : undefined,
      }
      const onEvent = (event: { type: string; data: any }) => {
        if (runId !== activeRunId || abortController.signal.aborted) return
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

          case 'token':
            streamedContent += String(event.data?.content ?? '')
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value.content = streamedContent
            }
            break

          case 'tool_call': {
            const label = getToolLabel(event.data.name)
            currentToolCall.value = `🔧 正在${label}…`
            upsertRunningToolCall(event.data, label)
            break
          }

          case 'tool_progress':
            updateToolProgress(event.data)
            currentToolCall.value = String(event.data?.message || '任务仍在处理中…')
            break

          case 'heartbeat':
            if (!streamedContent && !hasRunningToolCalls()) {
              currentToolCall.value = String(event.data?.message || '任务仍在处理，连接正常…')
            }
            break

          case 'tool_result':
            currentToolCall.value = ''
            collectExportedFile(event.data, exportedFiles)
            completeToolCall(event.data)
            break

          case 'confirm':
            // 暂停流，等待用户确认
            if (temporary) {
              pendingConfirmTemporaryId = temporaryId.value
            } else {
              pendingConfirmSessionId = sessionId
            }
            pendingConfirm.value = {
              toolName: event.data.toolName,
              riskLevel: event.data.riskLevel,
              arguments: event.data.arguments,
            }
            break

          case 'context':
            contextUsage.value = event.data
            break

          case 'recoverable':
            recoverableAgentRunId.value = String(event.data?.runId || activeAgentRunId || '') || null
            currentToolCall.value = '任务因服务重启中断，等待你确认继续执行…'
            break

          case 'done':
            result.outcome = 'completed'
            const finalMsg: ChatMessage = {
              id: temporary ? nextTemporaryMessageId-- : event.data.messageId,
              role: 'assistant',
              content: appendExportedFileLinks(
                preferCompleteContent(streamedContent, String(event.data?.content ?? '')),
                exportedFiles,
              ),
              createdAt: event.data.createdAt || activeStreamMessage.createdAt,
              toolEvents: snapshotToolEvents(),
            }
            rememberPageToolEvents(finalMsg.id, finalMsg.toolEvents)
            const finalIdx = targetSession.messages.findIndex((m) => m.id === finalMsg.id)
            if (finalIdx >= 0) {
              targetSession.messages[finalIdx] = finalMsg
            } else {
              targetSession.messages.push(finalMsg)
            }
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            if (!temporary) {
              const sessionIdx = sessions.value.findIndex((s) => s.id === sessionId)
              if (sessionIdx >= 0) {
                sessions.value[sessionIdx].title = event.data.title
              }
              targetSession.title = event.data.title
              sessionDetailsCache.set(sessionId, targetSession)
            }
            break

          case 'stopped':
            result.outcome = 'stopped'
            commitStoppedAssistant(
              targetSession,
              temporary,
              streamedContent,
              event.data,
              activeStreamMessage,
              exportedFiles,
            )
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            break
          case 'error':
            result.outcome = 'failed'
            lastSendError.value = event.data?.message || '对话出错，请重试'
            if (streamMessage.value?.id === activeStreamMessage.id) {
              streamMessage.value = null
            }
            shouldRestoreAttachments = !persistedUserMessage
            toast.error(event.data?.message || '对话出错，请重试')
            break
        }
      }
      if (temporary) {
        await agentApi.sendTemporaryAgentMessageStream(
          temporaryId.value,
          { ...payload, history: temporaryHistory },
          onEvent,
          abortController.signal,
        )
      } else {
        await agentApi.sendAgentMessageStream(
          sessionId,
          payload,
          onEvent,
          abortController.signal,
          (persistentRun) => {
            activeAgentRunId = persistentRun.runId
            recoverableAgentRunId.value = null
          },
        )
      }
    } catch (e: any) {
      if (runId !== activeRunId || abortController.signal.aborted) return 'stopped'
      lastSendError.value = '连接意外中断，未收到任务完成结果'
      result.outcome = e?.name === 'AbortError' ? 'stopped' : 'failed'
      const wasExecutingTool = hasRunningToolCalls()
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      shouldRestoreAttachments = !persistedUserMessage
      if (e?.name !== 'AbortError') {
        toast.error(wasExecutingTool
          ? '执行连接意外中断，任务已停止，请重试'
          : 'Agent 连接意外中断，请重试')
      }
    } finally {
      if (abortController.signal.aborted && result.outcome !== 'completed') result.outcome = 'stopped'
      if (result.outcome === 'failed' && !persistedUserMessage) {
        const index = targetSession.messages.findIndex((message) => message.id === tempUserMsg.id)
        if (index >= 0) targetSession.messages.splice(index, 1)
      }
      const stillInSendingSession = currentSession.value?.id === sessionId
        && (!temporary || temporaryId.value === sendingTemporaryId)
      if (shouldRestoreAttachments && attachmentsToSend.length > 0) {
        const existing = sendingAttachmentDraftKey
          ? attachmentDrafts.get(sendingAttachmentDraftKey) ?? []
          : []
        const restored = [...attachmentsToSend, ...existing]
        if (sendingAttachmentDraftKey) attachmentDrafts.set(sendingAttachmentDraftKey, restored)
        if (stillInSendingSession) pendingAttachments.value = restored
      }
      if (runId === activeRunId) {
      sending.value = false
      currentToolCall.value = ''
      toolCalls.value = []
      if (streamMessage.value?.id === activeStreamMessage.id) {
        streamMessage.value = null
      }
      if (streamSessionId.value === sessionId) {
        streamSessionId.value = null
      }
      if (activeAbortController === abortController) activeAbortController = null
      if (result.outcome === 'completed' || result.outcome === 'stopped' || result.outcome === 'failed') {
        activeAgentRunId = null
        recoverableAgentRunId.value = null
      }
      if (!temporary) sessionDetailsCache.set(sessionId, targetSession)
      if (currentSession.value?.id === sessionId && currentSession.value !== targetSession) {
        currentSession.value = targetSession
      }
      }
    }
    return result.outcome
  }

  /** 用户确认/取消危险操作 */
  async function confirmToolCall(confirmed: boolean) {
    const sid = pendingConfirmSessionId
    const tempId = pendingConfirmTemporaryId
    pendingConfirm.value = null
    pendingConfirmSessionId = null
    pendingConfirmTemporaryId = null
    if (tempId != null) {
      try {
        await agentApi.confirmTemporaryToolCall(tempId, confirmed)
      } catch {
        // 忽略网络错误
      }
    } else if (sid != null) {
      try {
        if (activeAgentRunId) await agentApi.confirmRun(activeAgentRunId, confirmed)
        else await agentApi.confirmToolCall(sid, confirmed)
      } catch {
        // 忽略网络错误
      }
    }
  }

  async function resumeRecoverableAgentRun() {
    const runId = recoverableAgentRunId.value
    if (!runId) return
    await agentApi.resumeRun(runId)
    recoverableAgentRunId.value = null
    currentToolCall.value = '任务正在继续执行…'
  }


  function commitStoppedAssistant(
    targetSession: ChatSessionDetail,
    temporary: boolean,
    streamedContent: string,
    data: any,
    activeStreamMessage: ChatMessage,
    exportedFiles: ExportedFileLink[],
  ) {
    const serverContent = typeof data?.content === 'string' ? data.content : ''
    const content = appendExportedFileLinks(
      preferCompleteContent(streamedContent, serverContent) || '（已停止）',
      exportedFiles,
    )
    const finalContent = content.includes('（已停止）') ? content : `${content.trimEnd()}\n\n（已停止）`
    const finalMsg: ChatMessage = {
      id: temporary
        ? (typeof data?.messageId === 'number' ? data.messageId : nextTemporaryMessageId--)
        : (typeof data?.messageId === 'number' ? data.messageId : -Date.now()),
      role: 'assistant',
      content: finalContent,
      createdAt: data?.createdAt || activeStreamMessage.createdAt,
      toolEvents: snapshotToolEvents(),
    }
    rememberPageToolEvents(finalMsg.id, finalMsg.toolEvents)
    const finalIdx = targetSession.messages.findIndex((m) => m.id === finalMsg.id)
    if (finalIdx >= 0) targetSession.messages[finalIdx] = finalMsg
    else targetSession.messages.push(finalMsg)
    if (!temporary) sessionDetailsCache.set(targetSession.id, targetSession)
  }

  function hasRunningToolCalls() {
    return toolCalls.value.some((tool) => tool.status === 'running')
  }

  function snapshotToolEvents(): ToolCallEvent[] | undefined {
    if (toolCalls.value.length === 0) return undefined
    return toolCalls.value.map((tool) => ({ ...tool }))
  }

  function findToolCallIndex(data: any) {
    const id = String(data?.id || data?.toolCallId || '')
    const name = String(data?.name || '')
    return toolCalls.value.findIndex((tool) =>
      (id && tool.id === id) || (name && tool.name === name),
    )
  }

  function upsertRunningToolCall(data: any, label: string) {
    const id = String(data?.id || data?.name || '')
    const name = String(data?.name || '')
    const inputSummary = summarizeToolInput(name, data?.arguments)
    const index = findToolCallIndex(data)
    const next: ToolCallEvent = {
      id: id || name,
      name,
      label,
      status: 'running',
      detail: '正在启动…',
      inputSummary: inputSummary || undefined,
    }
    if (index >= 0) {
      toolCalls.value[index] = {
        ...toolCalls.value[index],
        ...next,
        // 保留已有进度文案（极少见的重复 tool_call）
        detail: toolCalls.value[index].status === 'running'
          ? (toolCalls.value[index].detail || next.detail)
          : next.detail,
      }
      return
    }
    toolCalls.value.push(next)
  }

  function updateToolProgress(data: any) {
    const id = String(data?.id || data?.toolCallId || data?.name || '')
    const name = String(data?.name || '')
    const index = findToolCallIndex(data)
    const detail = String(data?.message || '任务仍在处理中…')
    const elapsedSeconds = Number(data?.elapsedSeconds || 0)

    if (index >= 0) {
      toolCalls.value[index] = {
        ...toolCalls.value[index],
        status: 'running',
        detail,
        elapsedSeconds,
      }
      return
    }

    toolCalls.value.push({
      id: id || name,
      name,
      label: getToolLabel(name),
      status: 'running',
      detail,
      elapsedSeconds,
    })
  }

  function completeToolCall(data: any) {
    const id = String(data?.toolCallId || data?.id || '')
    const name = String(data?.name || '')
    const { summary, failed, detail } = summarizeToolResult(data?.result)
    const index = findToolCallIndex(data)

    if (index >= 0) {
      const prev = toolCalls.value[index]
      toolCalls.value[index] = {
        ...prev,
        status: failed ? 'failure' : 'success',
        detail: failed ? (prev.detail || '执行失败') : (prev.detail || '已完成'),
        resultSummary: summary,
        errorDetail: failed ? detail : undefined,
      }
      return
    }

    if (!id && !name) return

    toolCalls.value.push({
      id: id || name,
      name,
      label: getToolLabel(name),
      status: failed ? 'failure' : 'success',
      detail: failed ? '执行失败' : '已完成',
      resultSummary: summary,
      errorDetail: failed ? detail : undefined,
    })
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
    projects,
    selectedProjectId,
    currentSession,
    isTemporary,
    loading,
    sessionsLoading,
    sending,
    lastSendError,
    streamMessage,
    streamSessionId,
    currentToolCall,
    toolCalls,
    autoMode,
    contextUsage,
    pendingConfirm,
    pendingAttachments,
    archivedSessions,
    archivedLoading,
    fetchSessions,
    searchSessions,
    selectProject,
    fetchProjects,
    openSession,
    createSession,
    startTemporarySession,
    setSessionPinned,
    assignSessionProject,
    branchSession,
    createProject,
    updateProject,
    deleteProject,
    stopGeneration,
    deleteSession,
    archiveSession,
    fetchArchivedSessions,
    unarchiveSession,
    sendMessage,
    sendMessageStream,
    sendAgentMessageStream,
    recoverableAgentRunId,
    resumeRecoverableAgentRun,
    confirmToolCall,
    updateTitle,
    pageToolEventsFor,
    rememberPageToolEvents,
    clearPageLifetimeToolState,
  }
})

function createTemporaryId(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(16).slice(2)}`
}

interface ExportedFileLink {
  fileName: string
  url: string
  markdown: string
}


function summarizeToolInput(_name: string, argsRaw: unknown): string {
  try {
    const args = typeof argsRaw === 'string'
      ? (argsRaw.trim() ? JSON.parse(argsRaw) : null)
      : argsRaw
    if (!args || typeof args !== 'object') {
      const raw = String(argsRaw ?? '').replace(/\s+/g, ' ').trim()
      return raw.length > 120 ? `${raw.slice(0, 120)}…` : raw
    }
    const record = args as Record<string, unknown>
    const preferred = [
      'query', 'keyword', 'q', 'command', 'path', 'filePath', 'filename', 'fileName',
      'url', 'content', 'title', 'expression', 'city', 'to', 'subject', 'id',
      'memoId', 'logId', 'status', 'format',
    ]
    const parts: string[] = []
    for (const key of preferred) {
      if (record[key] == null) continue
      let value = String(record[key]).replace(/\s+/g, ' ').trim()
      if (!value) continue
      if (value.length > 80) value = `${value.slice(0, 80)}…`
      parts.push(`${key}: ${value}`)
      if (parts.length >= 2) break
    }
    if (parts.length === 0) {
      for (const [key, raw] of Object.entries(record).slice(0, 2)) {
        let value = String(raw).replace(/\s+/g, ' ').trim()
        if (!value) continue
        if (value.length > 60) value = `${value.slice(0, 60)}…`
        parts.push(`${key}: ${value}`)
      }
    }
    return parts.join(' · ')
  } catch {
    const raw = String(argsRaw ?? '').replace(/\s+/g, ' ').trim()
    return raw.length > 120 ? `${raw.slice(0, 120)}…` : raw
  }
}

function isToolFailureResult(text: string): boolean {
  const t = text.trim()
  if (!t) return false
  const head = t.slice(0, 240)
  if (/^用户拒绝/.test(t)) return true
  if (/^(导出失败|工具执行失败|执行失败|错误[:：]|Error\b|Exception\b)/i.test(t)) return true
  try {
    const parsed = JSON.parse(t)
    if (parsed && typeof parsed === 'object') {
      if (parsed.success === false || parsed.ok === false) return true
      if (typeof parsed.error === 'string' && parsed.error.trim()) return true
    }
  } catch {
    // not JSON
  }
  // 开头明确失败语义；避免正文里偶然出现「错误」误判
  if (/(失败|出错|异常|拒绝了此操作|Traceback|PERMISSION_DENIED)/i.test(head)
    && !/(已成功|成功完成|创建成功|更新成功|删除成功|找到\s*\d+)/i.test(head.slice(0, 80))) {
    return true
  }
  return false
}

function summarizeToolResult(result: unknown): { summary: string; failed: boolean; detail?: string } {
  let text = ''
  if (typeof result === 'string') text = result
  else if (result == null) text = ''
  else {
    try { text = JSON.stringify(result) } catch { text = String(result) }
  }
  const failed = isToolFailureResult(text)
  let summary = text.replace(/\s+/g, ' ').trim()
  if (summary.length > 140) summary = `${summary.slice(0, 140)}…`
  if (!summary) summary = failed ? '执行失败' : '已完成'
  return {
    summary,
    failed,
    detail: failed ? text.slice(0, 4000) : undefined,
  }
}

function collectExportedFile(data: any, target: ExportedFileLink[]) {
  if (data?.name !== 'export_file' || typeof data?.result !== 'string') return
  try {
    const parsed = JSON.parse(data.result)
    if (typeof parsed?.url !== 'string' || typeof parsed?.markdown !== 'string') return
    if (target.some((file) => file.url === parsed.url)) return
    target.push({
      fileName: String(parsed.fileName || '导出文件'),
      url: parsed.url,
      markdown: parsed.markdown,
    })
  } catch {
    // 旧版或失败消息不是 JSON，不追加下载链接。
  }
}

function appendExportedFileLinks(content: string, files: ExportedFileLink[]): string {
  const missingLinks = files.filter((file) => !content.includes(file.url))
  if (missingLinks.length === 0) return content
  const links = missingLinks.map((file) => file.markdown).join('\n')
  return `${content.trimEnd()}\n\n${links}`.trim()
}

function preferCompleteContent(streamed: string, completed: string): string {
  if (!completed) return streamed
  if (!streamed) return completed
  if (completed.includes(streamed)) return completed
  if (streamed.includes(completed)) return streamed
  return completed.length >= streamed.length ? completed : streamed
}
