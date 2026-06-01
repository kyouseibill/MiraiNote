import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatSession, ChatSessionDetail, ChatMessage } from '@/types/chat'
import { chatApi } from '@/api/chat'

export const useChatStore = defineStore('chat', () => {
  const sessions = ref<ChatSession[]>([])
  const currentSession = ref<ChatSessionDetail | null>(null)
  const loading = ref(false)
  const sending = ref(false)

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

    // 乐观更新：先追加用户消息
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
      // 替换临时用户消息（服务器会自动创建），追加 AI 回复
      // 实际上用户消息已在后端持久化，这里仅追加 AI 回复
      currentSession.value.messages.push(assistantMsg)

      // 更新 session 标题（AI 可能修改了标题）
      await fetchSessions()
    } finally {
      sending.value = false
    }
  }

  async function updateTitle(sessionId: number, title: string) {
    const updated = await chatApi.updateTitle(sessionId, title)
    const idx = sessions.value.findIndex((s) => s.id === sessionId)
    if (idx >= 0) sessions.value[idx] = updated
    if (currentSession.value?.id === sessionId) currentSession.value.title = updated.title
    return updated
  }

  return {
    sessions,
    currentSession,
    loading,
    sending,
    fetchSessions,
    openSession,
    createSession,
    deleteSession,
    sendMessage,
    updateTitle,
  }
})
