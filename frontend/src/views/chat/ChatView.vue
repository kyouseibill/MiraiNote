<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'

const store = useChatStore()
const toast = useToast()

const inputContent = ref('')

// 格式化会话发起日期（本地时间）
function fmtSessionDate(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

// 格式化消息时间：当天只显示时分，否则显示日期+时分
function fmtMsgTime(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  const isToday =
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`
  if (isToday) return time
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${time}`
}
const messagesContainer = ref<HTMLElement | null>(null)
const renamingId = ref<number | null>(null)
const renameTitle = ref('')

async function scrollToBottom() {
  await nextTick()
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

async function newSession() {
  try {
    await store.createSession()
  } catch {
    // ignore
  }
}

async function selectSession(id: number) {
  try {
    await store.openSession(id)
    scrollToBottom()
  } catch {
    // ignore
  }
}

async function send() {
  const text = inputContent.value.trim()
  if (!text || store.sending) return
  if (!store.currentSession) {
    await newSession()
  }
  inputContent.value = ''
  try {
    if (store.useAgentMode) {
      await store.sendAgentMessageStream(text)
    } else {
      await store.sendMessageStream(text)
    }
    scrollToBottom()
  } catch {
    // ignore
  }
}

async function deleteSession(id: number, e: Event) {
  e.stopPropagation()
  if (!confirm('确定删除此对话？')) return
  await store.deleteSession(id)
  toast.success('已删除')
}

function startRename(id: number, currentTitle: string, e: Event) {
  e.stopPropagation()
  renamingId.value = id
  renameTitle.value = currentTitle
}

async function submitRename(id: number) {
  if (!renameTitle.value.trim()) return
  await store.updateTitle(id, renameTitle.value.trim())
  renamingId.value = null
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    send()
  }
}

watch(() => store.currentSession?.messages.length, () => {
  scrollToBottom()
})

onMounted(async () => {
  await store.fetchSessions()
  if (store.sessions.length > 0) {
    await selectSession(store.sessions[0].id)
  }
})
</script>

<template>
  <div class="flex h-[calc(100vh-4rem)] overflow-hidden">
    <!-- 左侧会话列表 -->
    <div class="w-64 shrink-0 bg-gray-50 border-r border-gray-200 flex flex-col">
      <div class="px-4 py-3 border-b border-gray-200">
        <button
          class="w-full h-9 rounded-lg bg-indigo-600 text-white text-sm hover:bg-indigo-700"
          @click="newSession"
        >
          + 新对话
        </button>
      </div>
      <div class="flex-1 overflow-y-auto">
        <div v-if="store.sessions.length === 0" class="px-4 py-6 text-xs text-gray-400 text-center">
          暂无对话，点击「新对话」开始
        </div>
        <ul class="py-1">
          <li
            v-for="s in store.sessions"
            :key="s.id"
            class="group px-3 py-2 cursor-pointer hover:bg-gray-100 rounded-lg mx-1 my-0.5 transition"
            :class="{ 'bg-indigo-50': store.currentSession?.id === s.id }"
            @click="selectSession(s.id)"
          >
            <div class="flex items-center justify-between gap-1">
              <div v-if="renamingId === s.id" class="flex-1 flex gap-1">
                <input
                  v-model="renameTitle"
                  class="flex-1 text-xs h-6 px-1 border rounded"
                  @click.stop
                  @keyup.enter="submitRename(s.id)"
                  @keyup.escape="renamingId = null"
                  autofocus
                />
                <button class="text-xs text-indigo-600" @click.stop="submitRename(s.id)">✓</button>
              </div>
              <div v-else class="flex-1 min-w-0">
                <div class="text-sm text-gray-700 truncate">{{ s.title }}</div>
                <div class="text-xs text-gray-400 mt-0.5">{{ fmtSessionDate(s.createdAt) }}</div>
              </div>
              <div class="hidden group-hover:flex items-center gap-0.5 shrink-0">
                <button
                  class="text-xs text-gray-400 hover:text-indigo-500 px-1"
                  @click="startRename(s.id, s.title, $event)"
                >
                  ✏
                </button>
                <button
                  class="text-xs text-gray-400 hover:text-red-500 px-1"
                  @click="deleteSession(s.id, $event)"
                >
                  ✕
                </button>
              </div>
            </div>
          </li>
        </ul>
      </div>
    </div>

    <!-- 右侧对话区 -->
    <div class="flex-1 flex flex-col min-w-0">
      <!-- 顶部标题 -->
      <div class="px-6 py-3 border-b border-gray-200 bg-white flex items-center justify-between">
        <span class="font-medium text-gray-800">
          {{ store.currentSession?.title ?? 'AI 对话' }}
        </span>
        <button
          class="text-xs px-2 py-1 rounded border"
          :class="store.useAgentMode ? 'bg-indigo-50 border-indigo-300 text-indigo-700' : 'bg-gray-50 border-gray-200 text-gray-500'"
          @click="store.useAgentMode = !store.useAgentMode"
        >
          {{ store.useAgentMode ? '🧠 Agent' : '💬 Chat' }}
        </button>
      </div>

      <!-- 消息列表 -->
      <div
        ref="messagesContainer"
        class="flex-1 overflow-y-auto px-6 py-4 space-y-4 bg-gray-50"
      >
        <div v-if="store.loading" class="text-center text-gray-400 text-sm">加载中…</div>

        <!-- Agent 执行计划 -->
        <div
          v-if="store.agentPlan"
          class="max-w-[70%] rounded-2xl px-4 py-3 bg-indigo-50 border border-indigo-200 text-sm"
        >
          <p class="font-medium text-indigo-800 mb-2">📋 执行计划：{{ store.agentPlan.goal }}</p>
          <ol class="list-decimal list-inside space-y-1 text-indigo-700">
            <li v-for="(step, i) in store.agentPlan.steps" :key="i">
              {{ step.action }}
              <span class="text-xs text-gray-400">({{ step.tools.join(', ') }})</span>
            </li>
          </ol>
          <div v-if="store.agentPlan.risks.length" class="mt-2 pt-2 border-t border-indigo-200">
            <p class="text-xs text-yellow-600">⚠ {{ store.agentPlan.risks.join('；') }}</p>
          </div>
        </div>
        <div
          v-else-if="!store.currentSession || store.currentSession.messages.length === 0"
          class="text-center text-gray-400 text-sm mt-20"
        >
          开始对话吧～
        </div>
        <div
          v-for="msg in store.currentSession?.messages"
          :key="msg.id"
          class="flex flex-col"
          :class="msg.role === 'user' ? 'items-end' : 'items-start'"
        >
          <span class="text-xs text-gray-400 mb-1 px-1">{{ fmtMsgTime(msg.createdAt) }}</span>
          <div
            class="max-w-[70%] rounded-2xl px-4 py-3 text-sm leading-relaxed"
            :class="msg.role === 'user'
              ? 'bg-indigo-600 text-white rounded-br-sm'
              : 'bg-white text-gray-800 border border-gray-100 shadow-sm rounded-bl-sm prose prose-sm max-w-none'"
            v-html="msg.role === 'user' ? msg.content.replace(/\n/g, '<br>') : renderMarkdown(msg.content)"
          />
        </div>
                <!-- 流式输出中的 AI 回复（通过 streamMessage 实时渲染） -->
        <div v-if="store.streamMessage" class="flex flex-col items-start">
          <span class="text-xs text-gray-400 mb-1 px-1">{{ fmtMsgTime(store.streamMessage.createdAt) }}</span>
          <div
            class="max-w-[70%] rounded-2xl px-4 py-3 text-sm leading-relaxed bg-white text-gray-800 border border-gray-100 shadow-sm rounded-bl-sm prose prose-sm max-w-none"
            v-html="store.streamMessage.content ? renderMarkdown(store.streamMessage.content) : ''"
          />
          <!-- 工具调用指示器 -->
          <div
            v-if="store.currentToolCall && !store.streamMessage.content"
            class="mt-1 flex items-center gap-2 text-xs text-gray-400 animate-pulse"
          >
            <span class="inline-block w-2 h-2 rounded-full bg-indigo-400"></span>
            {{ store.currentToolCall }}
          </div>
        </div>

        <!-- Agent 反思结果 -->
        <div
          v-if="store.agentReflection"
          class="max-w-[70%] rounded-2xl px-4 py-3 bg-fuchsia-50 border border-fuchsia-200 text-sm"
        >
          <p class="font-medium text-fuchsia-800 mb-2">🔍 自我反思</p>
          <div class="space-y-1 text-xs">
            <p>
              <span :class="store.agentReflection.isComplete ? 'text-green-600' : 'text-red-600'">
                {{ store.agentReflection.isComplete ? '✓' : '✗' }}
              </span>
              目标达成：{{ store.agentReflection.isComplete ? '是' : '否' }}
              <span class="ml-2">自评：<b :class="store.agentReflection.score >= 8 ? 'text-green-600' : store.agentReflection.score >= 5 ? 'text-yellow-600' : 'text-red-600'">{{ store.agentReflection.score }}/10</b></span>
            </p>
            <p v-if="store.agentReflection.strengths.length" class="text-green-600">
              ✓ {{ store.agentReflection.strengths.join('；') }}
            </p>
            <p v-if="store.agentReflection.issues.length" class="text-yellow-600">
              ⚠ {{ store.agentReflection.issues.join('；') }}
            </p>
            <p v-if="store.agentReflection.suggestions.length" class="text-blue-600">
              💡 {{ store.agentReflection.suggestions.join('；') }}
            </p>
          </div>
        </div>
      </div>

      <!-- 输入区 -->
      <div class="px-6 py-4 bg-white border-t border-gray-200">
        <div class="flex gap-3">
          <textarea
            v-model="inputContent"
            rows="2"
            placeholder="输入消息，Enter 发送，Shift+Enter 换行"
            class="flex-1 px-4 py-2 rounded-xl border border-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-indigo-200"
            @keydown="handleKeydown"
          />
          <button
            class="shrink-0 h-12 px-5 rounded-xl bg-indigo-600 text-white text-sm hover:bg-indigo-700 disabled:opacity-50 self-end"
            :disabled="store.sending || !inputContent.trim()"
            @click="send"
          >
            发送
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
