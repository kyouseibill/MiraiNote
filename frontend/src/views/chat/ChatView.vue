<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'

const store = useChatStore()
const toast = useToast()

const inputContent = ref('')
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
    await store.sendMessage(text)
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
              <span v-else class="text-sm text-gray-700 truncate flex-1">{{ s.title }}</span>
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
      <div class="px-6 py-3 border-b border-gray-200 bg-white">
        <span class="font-medium text-gray-800">
          {{ store.currentSession?.title ?? 'AI 对话' }}
        </span>
      </div>

      <!-- 消息列表 -->
      <div
        ref="messagesContainer"
        class="flex-1 overflow-y-auto px-6 py-4 space-y-4 bg-gray-50"
      >
        <div v-if="store.loading" class="text-center text-gray-400 text-sm">加载中…</div>
        <div
          v-else-if="!store.currentSession || store.currentSession.messages.length === 0"
          class="text-center text-gray-400 text-sm mt-20"
        >
          开始对话吧～
        </div>
        <div
          v-for="msg in store.currentSession?.messages"
          :key="msg.id"
          class="flex"
          :class="msg.role === 'user' ? 'justify-end' : 'justify-start'"
        >
          <div
            class="max-w-[70%] rounded-2xl px-4 py-3 text-sm leading-relaxed"
            :class="msg.role === 'user'
              ? 'bg-indigo-600 text-white rounded-br-sm'
              : 'bg-white text-gray-800 border border-gray-100 shadow-sm rounded-bl-sm prose prose-sm max-w-none'"
            v-html="msg.role === 'user' ? msg.content.replace(/\n/g, '<br>') : renderMarkdown(msg.content)"
          />
        </div>
        <div v-if="store.sending" class="flex justify-start">
          <div class="bg-white border border-gray-100 shadow-sm rounded-2xl rounded-bl-sm px-4 py-3 text-sm text-gray-400">
            AI 思考中…
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
