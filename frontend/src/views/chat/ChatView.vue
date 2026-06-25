<script setup lang="ts">
import { ref, onMounted, nextTick, watch, onErrorCaptured } from 'vue'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'
import { chatApi } from '@/api/chat'
import WorkspaceBrowser from '@/components/WorkspaceBrowser.vue'

const store = useChatStore()
const toast = useToast()

// 捕获子树渲染错误，防止白屏
const renderError = ref<string | null>(null)
onErrorCaptured((err) => {
  console.error('[ChatView error]', err)
  renderError.value = '页面渲染出错，请刷新重试'
  return false // 阻止继续向上传播
})

// 安全的 markdown 渲染（避免 marked 抛出异常导致白屏）
function safeMarkdown(content: string | null | undefined): string {
  try {
    return renderMarkdown(content)
  } catch {
    return String(content ?? '')
  }
}

const inputContent = ref('')

// 每个会话的输入草稿（切换时保存/恢复）
const inputDrafts = new Map<number, string>()

// ── 文件附件 ──
const fileInputRef = ref<HTMLInputElement | null>(null)
const uploadingFiles = ref<Set<string>>(new Set())

const ACCEPTED_TYPES = [
  '.pdf', '.docx', '.xlsx', '.xls',
  '.txt', '.md', '.csv', '.json', '.xml', '.html', '.yaml', '.yml',
  '.ts', '.js', '.tsx', '.jsx', '.py', '.cs', '.java', '.go', '.rs',
  '.sql', '.sh', '.bat', '.ps1', '.vue', '.css', '.log',
  '.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg',
].join(',')

function triggerFileInput() {
  fileInputRef.value?.click()
}

async function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = '' // 清空，允许重复选同一文件

  for (const file of files) {
    if (uploadingFiles.value.has(file.name)) continue
    uploadingFiles.value = new Set([...uploadingFiles.value, file.name])
    try {
      const result = await chatApi.uploadAttachment(file)
      store.pendingAttachments.push({
        fileName: result.fileName,
        fileType: result.fileType,
        textContent: result.textContent,
      })
    } catch (err: any) {
      toast.error(`文件「${file.name}」上传失败：${err?.response?.data?.message ?? '请重试'}`)
    } finally {
      uploadingFiles.value = new Set([...uploadingFiles.value].filter(n => n !== file.name))
    }
  }
}

function removeAttachment(idx: number) {
  store.pendingAttachments.splice(idx, 1)
}

function getFileIcon(fileType: string): string {
  const icons: Record<string, string> = {
    'PDF': '📄',
    'Word': '📝',
    'Excel': '📊',
    '图片': '🖼️',
    '文本': '📃',
  }
  return icons[fileType] ?? '📎'
}

// ── 工作区浏览器 ──
const showWorkspaceBrowser = ref(false)

function onWorkspaceAttach(file: { fileName: string; fileType: string; textContent: string }) {
  store.pendingAttachments.push(file)
  showWorkspaceBrowser.value = false
}

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
  // 保存当前会话的输入草稿
  if (store.currentSession) {
    inputDrafts.set(store.currentSession.id, inputContent.value)
  }
  try {
    await store.openSession(id)
    // 恢复新会话的输入草稿
    inputContent.value = inputDrafts.get(id) ?? ''
    store.pendingAttachments.splice(0)
    scrollToBottom()
  } catch {
    // ignore
  }
}

async function send() {
  const text = inputContent.value.trim()
  if ((!text && store.pendingAttachments.length === 0) || store.sending) return
  if (!store.currentSession) {
    await newSession()
  }
  inputContent.value = ''
  try {
    if (store.useAgentMode) {
      await store.sendAgentMessageStream(text || '请分析这些文件的内容')
    } else {
      await store.sendMessageStream(text || '请分析这些文件的内容')
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
    <!-- 全局错误提示（防白屏） -->
    <div
      v-if="renderError"
      class="fixed inset-0 z-[100] flex items-center justify-center bg-white/90"
    >
      <div class="text-center">
        <p class="text-red-500 text-sm mb-3">{{ renderError }}</p>
        <button
          class="px-4 py-2 bg-indigo-600 text-white text-sm rounded-lg hover:bg-indigo-700"
          @click="renderError = null"
        >重试</button>
      </div>
    </div>

    <!-- 工作区浏览器侧边面板 -->
    <Teleport to="body">
      <div
        v-if="showWorkspaceBrowser"
        class="fixed inset-0 z-40 flex"
        @click.self="showWorkspaceBrowser = false"
      >
        <div class="ml-auto w-80 max-w-full h-full bg-white shadow-2xl flex flex-col border-l border-gray-200">
          <WorkspaceBrowser
            @attach="onWorkspaceAttach"
            @close="showWorkspaceBrowser = false"
          />
        </div>
      </div>
    </Teleport>
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
      <!-- 顶部标题 + Agent 控制栏 -->
      <div class="px-4 py-2 border-b border-gray-200 bg-white">
        <div class="flex items-center justify-between">
          <span class="font-medium text-gray-800 text-sm">
            {{ store.currentSession?.title ?? 'AI 对话' }}
          </span>
          <div class="flex items-center gap-1">
            <!-- 模式切换 -->
            <button
              class="text-xs px-2 py-1 rounded border"
              :class="store.useAgentMode ? 'bg-indigo-50 border-indigo-300 text-indigo-700' : 'bg-gray-50 border-gray-200 text-gray-500'"
              @click="store.useAgentMode = !store.useAgentMode"
            >
              {{ store.useAgentMode ? '🧠 Agent' : '💬 Chat' }}
            </button>
          </div>
        </div>
        <!-- Agent 控制栏（仅在 Agent 模式下显示） -->
        <div v-if="store.useAgentMode" class="flex items-center gap-2 mt-1.5 text-xs text-gray-500">
          <label class="flex items-center gap-1 cursor-pointer" title="任务分解与执行计划">
            <input type="checkbox" v-model="store.enablePlanner" class="w-3 h-3" />
            <span :class="store.enablePlanner ? 'text-indigo-600' : ''">📋 Plan</span>
          </label>
          <label class="flex items-center gap-1 cursor-pointer" title="完成后自我质量评估">
            <input type="checkbox" v-model="store.enableReflector" class="w-3 h-3" />
            <span :class="store.enableReflector ? 'text-fuchsia-600' : ''">🔍 Reflect</span>
          </label>
          <label class="flex items-center gap-1 cursor-pointer" title="跳过所有确认，全自动执行">
            <input type="checkbox" v-model="store.autoMode" class="w-3 h-3" />
            <span :class="store.autoMode ? 'text-orange-600' : ''">⚡ Auto</span>
          </label>
          <!-- 上下文用量 -->
          <span
            v-if="store.contextUsage"
            class="ml-auto px-1.5 py-0.5 rounded text-[10px]"
            :class="store.contextUsage.percentUsed > 40 ? 'bg-yellow-50 text-yellow-700' : 'bg-gray-100 text-gray-400'"
            :title="`${store.contextUsage.estimatedTokens}/${store.contextUsage.maxTokens} tokens, ${store.contextUsage.messageCount} 条消息`"
          >
            📊 {{ store.contextUsage.percentUsed }}%
          </span>
        </div>
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
            <li v-for="(step, i) in (store.agentPlan?.steps ?? [])" :key="i">
              {{ step.action }}
              <span class="text-xs text-gray-400">({{ (step.tools ?? []).join(', ') }})</span>
            </li>
          </ol>
          <div v-if="(store.agentPlan?.risks ?? []).length" class="mt-2 pt-2 border-t border-indigo-200">
            <p class="text-xs text-yellow-600">⚠ {{ (store.agentPlan?.risks ?? []).join('；') }}</p>
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
    v-html="msg.role === 'user' ? (msg.content ?? '').replace(/\n/g, '<br>') : safeMarkdown(msg.content)"
          />
        </div>
        <!-- 流式输出中的 AI 回复（仅属于当前会话时显示） -->
        <div v-if="store.streamMessage && store.streamSessionId === store.currentSession?.id" class="flex flex-col items-start">
          <span class="text-xs text-gray-400 mb-1 px-1">{{ fmtMsgTime(store.streamMessage.createdAt) }}</span>
          <div
            class="max-w-[70%] rounded-2xl px-4 py-3 text-sm leading-relaxed bg-white text-gray-800 border border-gray-100 shadow-sm rounded-bl-sm prose prose-sm max-w-none"
            v-html="store.streamMessage.content ? safeMarkdown(store.streamMessage.content) : ''"
          />
          <!-- 并行工具调用指示器 -->
          <div v-if="store.toolCalls.length > 0" class="mt-1.5 space-y-1">
            <div
              v-for="tc in store.toolCalls"
              :key="tc.id"
              class="flex items-center gap-2 text-xs text-gray-400 animate-pulse"
            >
              <span class="inline-block w-2 h-2 rounded-full bg-indigo-400 animate-pulse"></span>
              <span>🔧 {{ tc.label }}</span>
            </div>
          </div>
          <div
            v-else-if="store.currentToolCall && !store.streamMessage.content"
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
            <p v-if="(store.agentReflection?.strengths ?? []).length" class="text-green-600">
              ✓ {{ (store.agentReflection?.strengths ?? []).join('；') }}
            </p>
            <p v-if="(store.agentReflection?.issues ?? []).length" class="text-yellow-600">
              ⚠ {{ (store.agentReflection?.issues ?? []).join('；') }}
            </p>
            <p v-if="(store.agentReflection?.suggestions ?? []).length" class="text-blue-600">
              💡 {{ (store.agentReflection?.suggestions ?? []).join('；') }}
            </p>
          </div>
        </div>
      </div>

      <!-- 输入区 -->
      <div class="px-6 py-4 bg-white border-t border-gray-200">
        <!-- 附件列表 -->
        <div
          v-if="store.pendingAttachments.length > 0 || uploadingFiles.size > 0"
          class="flex flex-wrap gap-2 mb-2"
        >
          <!-- 正在上传的文件 -->
          <div
            v-for="name in uploadingFiles"
            :key="'uploading-' + name"
            class="flex items-center gap-1 px-2 py-1 rounded-lg bg-gray-100 text-xs text-gray-400 animate-pulse"
          >
            <span>⏳</span>
            <span class="max-w-[120px] truncate">{{ name }}</span>
          </div>
          <!-- 已上传的附件 -->
          <div
            v-for="(att, idx) in store.pendingAttachments"
            :key="idx"
            class="flex items-center gap-1 px-2 py-1 rounded-lg bg-indigo-50 border border-indigo-200 text-xs text-indigo-700"
          >
            <span>{{ getFileIcon(att.fileType) }}</span>
            <span class="max-w-[120px] truncate" :title="att.fileName">{{ att.fileName }}</span>
            <button
              class="ml-1 text-indigo-400 hover:text-red-500 leading-none"
              @click="removeAttachment(idx)"
            >
              ✕
            </button>
          </div>
        </div>

        <div class="flex gap-3 items-end">
          <!-- 隐藏的 file input -->
          <input
            ref="fileInputRef"
            type="file"
            :accept="ACCEPTED_TYPES"
            multiple
            class="hidden"
            @change="handleFileSelect"
          />
          <!-- 附件按钮 -->
          <button
            class="shrink-0 h-10 w-10 rounded-xl border border-gray-200 text-gray-400 hover:text-indigo-600 hover:border-indigo-300 flex items-center justify-center text-lg transition"
            :disabled="store.sending"
            title="上传文件（支持 PDF / Word / Excel / 文本 / 图片）"
            @click="triggerFileInput"
          >
            📎
          </button>
          <!-- 工作区浏览器按钮 -->
          <button
            class="shrink-0 h-10 w-10 rounded-xl border flex items-center justify-center text-lg transition"
            :class="showWorkspaceBrowser
              ? 'bg-indigo-50 border-indigo-300 text-indigo-600'
              : 'border-gray-200 text-gray-400 hover:text-indigo-600 hover:border-indigo-300'"
            :disabled="store.sending"
            title="从工作区选择文件附加到消息"
            @click="showWorkspaceBrowser = !showWorkspaceBrowser"
          >
            🗂️
          </button>
          <textarea
            v-model="inputContent"
            rows="2"
            placeholder="输入消息，Enter 发送，Shift+Enter 换行"
            class="flex-1 px-4 py-2 rounded-xl border border-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-indigo-200"
            @keydown="handleKeydown"
          />
          <button
            class="shrink-0 h-10 px-5 rounded-xl bg-indigo-600 text-white text-sm hover:bg-indigo-700 disabled:opacity-50"
            :disabled="store.sending || (!inputContent.trim() && store.pendingAttachments.length === 0)"
            @click="send"
          >
            发送
          </button>
        </div>
      </div>

      <!-- 危险操作确认弹窗 -->
      <Teleport to="body">
        <div
          v-if="store.pendingConfirm"
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/30"
        >
          <div class="bg-white rounded-2xl shadow-xl w-96 max-w-[90vw] p-6">
            <div class="flex items-center gap-3 mb-4">
              <span class="text-2xl">
                {{ store.pendingConfirm.riskLevel === 'dangerous' ? '⚠️' : '📝' }}
              </span>
              <div>
                <p class="font-medium text-gray-800 text-sm">
                  {{ store.pendingConfirm.riskLevel === 'dangerous' ? '危险操作确认' : '写入操作确认' }}
                </p>
                <p class="text-xs text-gray-400 mt-0.5">
                  工具：<code class="bg-gray-100 px-1 rounded">{{ store.pendingConfirm.toolName }}</code>
                </p>
              </div>
            </div>
            <div
              v-if="store.pendingConfirm.arguments"
              class="bg-gray-50 rounded-lg p-2 mb-4 text-xs text-gray-500 max-h-24 overflow-y-auto font-mono"
            >
              {{ store.pendingConfirm.arguments }}
            </div>
            <p class="text-sm text-gray-600 mb-5">确认执行此操作吗？</p>
            <div class="flex gap-3 justify-end">
              <button
                class="px-4 py-2 rounded-lg border border-gray-200 text-sm text-gray-600 hover:bg-gray-50"
                @click="store.confirmToolCall(false)"
              >
                取消
              </button>
              <button
                class="px-4 py-2 rounded-lg text-sm text-white"
                :class="store.pendingConfirm.riskLevel === 'dangerous' ? 'bg-red-600 hover:bg-red-700' : 'bg-indigo-600 hover:bg-indigo-700'"
                @click="store.confirmToolCall(true)"
              >
                确认执行
              </button>
            </div>
          </div>
        </div>
      </Teleport>
    </div>
  </div>
</template>
