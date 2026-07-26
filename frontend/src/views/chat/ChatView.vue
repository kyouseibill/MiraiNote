<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, nextTick, watch, onErrorCaptured } from 'vue'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'
import { chatApi } from '@/api/chat'
import WorkspaceBrowser from '@/components/WorkspaceBrowser.vue'

const store = useChatStore()
const toast = useToast()

const renderError = ref<string | null>(null)
onErrorCaptured((err) => {
  console.error('[ChatView error]', err)
  renderError.value = '页面渲染出错，请刷新重试'
  return false
})

function safeMarkdown(content: string | null | undefined): string {
  try {
    return renderMarkdown(content)
  } catch {
    return String(content ?? '')
  }
}

function renderUserContent(content: string | null | undefined): string {
  return String(content ?? '').replace(/\n/g, '<br>')
}

function splitAssistantContent(content: string | null | undefined, allowOpenThinking = false): { thinking: string; answer: string } {
  const raw = String(content ?? '')
  const hasClosedThinking = /<thinking>[\s\S]*?<\/thinking>/i.test(raw)
  if (!hasClosedThinking && allowOpenThinking) {
    const openThinkingMatch = raw.match(/<thinking>([\s\S]*)$/i)
    if (openThinkingMatch) {
      return { thinking: openThinkingMatch[1].trim(), answer: '' }
    }
  }

  if (!hasClosedThinking) {
    return {
      thinking: '',
      answer: raw.replace(/<\/?thinking>/gi, '').replace(/<\/?answer>/gi, '').trim(),
    }
  }

  const thinkingMatch = raw.match(/<thinking>([\s\S]*?)<\/thinking>/i)
  const answerMatch = raw.match(/<answer>([\s\S]*?)(?:<\/answer>|$)/i)

  if (!thinkingMatch) {
    return {
      thinking: '',
      answer: raw.replace(/<\/?answer>/gi, '').trim(),
    }
  }

  const thinking = thinkingMatch[1].trim()
  const answer = answerMatch
    ? answerMatch[1].trim()
    : raw
        .replace(/<thinking>[\s\S]*?<\/thinking>/i, '')
        .replace(/<\/?answer>/gi, '')
        .trim()

  return { thinking, answer: answer || thinking }
}

const inputDrafts = reactive(new Map<number, string>())
const newSessionDraft = ref('') // 尚未创建会话时（欢迎页）的草稿
const inputContent = computed({
  get() {
    const id = store.currentSession?.id
    if (id == null) return newSessionDraft.value
    return inputDrafts.get(id) ?? ''
  },
  set(val: string) {
    const id = store.currentSession?.id
    if (id == null) {
      newSessionDraft.value = val
      return
    }
    inputDrafts.set(id, val)
  },
})
const fileInputRef = ref<HTMLInputElement | null>(null)
const uploadingFiles = ref<Set<string>>(new Set())
const showWorkspaceBrowser = ref(false)
const messagesContainer = ref<HTMLElement | null>(null)
const renamingId = ref<number | null>(null)
const renameTitle = ref('')
const showArchiveManager = ref(false)
const showSessionList = ref(false)
const restoringId = ref<number | null>(null)

// 会话条目的单一入口“…”菜单（编辑/归档/删除）
const menuOpenId = ref<number | null>(null)
function toggleSessionMenu(id: number, e: Event) {
  e.stopPropagation()
  menuOpenId.value = menuOpenId.value === id ? null : id
}
function closeSessionMenu() {
  menuOpenId.value = null
}

// 拖拽对话内容与输入框之间的分隔条，调整输入框高度
const inputHeightPx = ref(84)
const MIN_INPUT_HEIGHT = 40
const MAX_INPUT_HEIGHT = 400
let resizeStartY = 0
let resizeStartHeight = 0
let resizingInput = false

function startInputResize(e: MouseEvent) {
  resizingInput = true
  resizeStartY = e.clientY
  resizeStartHeight = inputHeightPx.value
  window.addEventListener('mousemove', onInputResizeMove)
  window.addEventListener('mouseup', stopInputResize)
  e.preventDefault()
}

function onInputResizeMove(e: MouseEvent) {
  if (!resizingInput) return
  const delta = resizeStartY - e.clientY
  inputHeightPx.value = Math.min(MAX_INPUT_HEIGHT, Math.max(MIN_INPUT_HEIGHT, resizeStartHeight + delta))
}

function stopInputResize() {
  resizingInput = false
  window.removeEventListener('mousemove', onInputResizeMove)
  window.removeEventListener('mouseup', stopInputResize)
}

onMounted(() => {
  window.addEventListener('click', closeSessionMenu)
})
onUnmounted(() => {
  window.removeEventListener('click', closeSessionMenu)
  stopInputResize()
})

const ACCEPTED_TYPES = [
  '.pdf', '.docx', '.xlsx', '.xls',
  '.txt', '.md', '.csv', '.json', '.xml', '.html', '.yaml', '.yml',
  '.ts', '.js', '.tsx', '.jsx', '.py', '.cs', '.java', '.go', '.rs',
  '.sql', '.sh', '.bat', '.ps1', '.vue', '.css', '.log',
  '.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg', '.tiff', '.tif', '.avif',
].join(',')

const LOCAL_TEXT_EXTENSIONS = new Set([
  '.txt', '.md', '.csv', '.json', '.xml', '.html', '.htm', '.yaml', '.yml',
  '.toml', '.ini', '.env', '.log', '.sql', '.ts', '.js', '.jsx', '.tsx',
  '.py', '.cs', '.java', '.cpp', '.c', '.h', '.go', '.rs', '.php',
  '.rb', '.sh', '.bat', '.ps1', '.vue', '.css', '.scss', '.less',
  '.conf', '.config', '.csproj', '.sln',
])
const LOCAL_TEXT_MAX_CHARS = 800_000

function triggerFileInput() {
  fileInputRef.value?.click()
}

async function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  await uploadFiles(files)
}

async function uploadFiles(files: File[]) {
  for (const file of files) {
    if (uploadingFiles.value.has(file.name)) continue
    uploadingFiles.value = new Set([...uploadingFiles.value, file.name])

    try {
      if (isImageFile(file)) {
        const dataUrl = await readFileAsDataUrl(file)
        const mimeType = file.type || imageMimeType(file.name)
        store.pendingAttachments.push({
          fileName: file.name || `clipboard-${Date.now()}.${mimeType.split('/')[1] || 'png'}`,
          fileType: '图片',
          textContent: `[图片文件: ${file.name || 'clipboard image'}]`,
          mimeType,
          dataUrl,
          isImage: true,
        })
        continue
      }

      if (isLocalTextFile(file)) {
        const textContent = truncateLocalText(await readFileAsText(file), file.name)
        store.pendingAttachments.push({
          fileName: file.name,
          fileType: '文本',
          textContent,
          mimeType: file.type || textMimeType(file.name),
          isImage: false,
        })
        continue
      }

      const result = await chatApi.uploadAttachment(file)
      store.pendingAttachments.push({
        fileName: result.fileName,
        fileType: result.fileType,
        textContent: result.textContent,
        mimeType: result.mimeType,
        dataUrl: result.dataUrl,
        isImage: result.isImage,
      })
    } catch (err: any) {
      toast.error(`文件「${file.name}」上传失败：${formatUploadError(err)}`)
    } finally {
      uploadingFiles.value = new Set([...uploadingFiles.value].filter((name) => name !== file.name))
    }
  }
}

function truncateLocalText(text: string, fileName: string): string {
  if (text.length <= LOCAL_TEXT_MAX_CHARS) return text
  return text.slice(0, LOCAL_TEXT_MAX_CHARS)
    + `\n\n... [文件内容已截断，共 ${text.length} 字符，文件名：${fileName}]`
}

function readFileAsText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result ?? ''))
    reader.onerror = () => reject(reader.error ?? new Error('读取文件失败'))
    reader.readAsText(file)
  })
}

function readFileAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result ?? ''))
    reader.onerror = () => reject(reader.error ?? new Error('读取图片失败'))
    reader.readAsDataURL(file)
  })
}

function isLocalTextFile(file: File): boolean {
  if (file.type.startsWith('text/')) return true
  return LOCAL_TEXT_EXTENSIONS.has(fileExtension(file.name))
}

function fileExtension(fileName: string): string {
  const idx = fileName.lastIndexOf('.')
  return idx >= 0 ? fileName.slice(idx).toLowerCase() : ''
}

function isImageFile(file: File): boolean {
  if (file.type.startsWith('image/')) return true
  return /\.(jpe?g|png|gif|webp|bmp|svg|tiff?|avif)$/i.test(file.name)
}

function textMimeType(fileName: string): string {
  const ext = fileExtension(fileName)
  const map: Record<string, string> = {
    '.csv': 'text/csv',
    '.html': 'text/html',
    '.htm': 'text/html',
    '.json': 'application/json',
    '.xml': 'application/xml',
    '.yaml': 'application/yaml',
    '.yml': 'application/yaml',
  }
  return map[ext] ?? 'text/plain'
}

function imageMimeType(fileName: string): string {
  const ext = fileName.split('.').pop()?.toLowerCase()
  const map: Record<string, string> = {
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    png: 'image/png',
    gif: 'image/gif',
    webp: 'image/webp',
    bmp: 'image/bmp',
    svg: 'image/svg+xml',
    tiff: 'image/tiff',
    tif: 'image/tiff',
    avif: 'image/avif',
  }
  return map[ext ?? ''] ?? 'image/png'
}

function formatUploadError(err: any): string {
  if (err?.code === 'ECONNABORTED') return '上传超时，请稍后重试或换一个较小的文件'
  const status = err?.response?.status
  const message = err?.response?.data?.message || err?.message
  if (status) return `HTTP ${status}${message ? `：${message}` : ''}`
  return message || '网络请求失败'
}

async function handlePaste(e: ClipboardEvent) {
  const files = filesFromDataTransfer(e.clipboardData)
  if (files.length === 0) return
  e.preventDefault()
  await uploadFiles(files)
}

function filesFromDataTransfer(data: DataTransfer | null): File[] {
  if (!data) return []
  const files = Array.from(data.files ?? [])
  const itemFiles = Array.from(data.items ?? [])
    .filter((item) => item.kind === 'file')
    .map((item) => item.getAsFile())
    .filter((file): file is File => !!file)
    .map((file) => {
      if (file.name) return file
      const ext = file.type.split('/')[1] || 'bin'
      return new File([file], `clipboard-${Date.now()}.${ext}`, { type: file.type })
    })

  const merged = [...files, ...itemFiles]
  return merged.filter((file, index, arr) =>
    arr.findIndex((x) => x.name === file.name && x.size === file.size && x.type === file.type) === index,
  )
}

function removeAttachment(idx: number) {
  store.pendingAttachments.splice(idx, 1)
}

function getFileIcon(fileType: string): string {
  const icons: Record<string, string> = {
    PDF: 'PDF',
    Word: 'DOC',
    Excel: 'XLS',
    图片: 'IMG',
    文本: 'TXT',
  }
  return icons[fileType] ?? 'FILE'
}

function onWorkspaceAttach(file: { fileName: string; fileType: string; textContent: string; mimeType?: string; dataUrl?: string; isImage?: boolean }) {
  store.pendingAttachments.push(file)
  showWorkspaceBrowser.value = false
}

function fmtSessionDate(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function fmtMsgTime(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`
  const isToday =
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  if (isToday) return time
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${time}`
}

async function scrollToBottom() {
  await nextTick()
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

async function newSession() {
  try {
    await store.createSession()
    newSessionDraft.value = ''
  } catch {
    // ignore
  }
}

async function selectSession(id: number) {
  try {
    await store.openSession(id)
    showSessionList.value = false
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
    if (shouldUseAgent(text)) {
      await store.sendAgentMessageStream(text || '请分析这些文件的内容')
    } else {
      await store.sendMessageStream(text || '请分析这些文件的内容')
    }
    scrollToBottom()
  } catch {
    // ignore
  }
}

function shouldUseAgent(text: string): boolean {
  if (store.pendingAttachments.length > 0) return true
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const agentPatterns = [
    /查|找|搜索|检索|联网|天气|新闻|价格|最新/,
    /网页|网站|链接|网址|url|api|接口|http|https|登录|用户名|密码|模拟操作|抓取/,
    /创建|新增|添加|记录|保存|写入|生成|导出/,
    /更新|修改|编辑|删除|归档|完成|置顶/,
    /提醒|定时|计划|日程|待办|备忘/,
    /总结|汇总|分析|统计|趋势|周报|日报|复盘/,
    /今天|明天|昨天|本周|本月|现在几点|当前时间|日期|星期|多少天/,
    /\d+\s*[+\-*/%]\s*\d+/,
    /工作记录|生活记录|备忘|文件|目录|运行|命令/,
    /\b(search|find|create|update|delete|export|schedule|remind|analyze|summarize|file|run|api|http|url|login|fetch|web)\b/,
  ]
  return agentPatterns.some((pattern) => pattern.test(normalized))
}

async function deleteSession(id: number, e: Event) {
  e.stopPropagation()
  if (!confirm('确定删除此对话？')) return
  await store.deleteSession(id)
  inputDrafts.delete(id)
  toast.success('已删除')
}

async function archiveSession(id: number, e: Event) {
  e.stopPropagation()
  if (!confirm('确定归档此对话？归档后需要在“归档管理”中还原才能继续查看。')) return
  await store.archiveSession(id)
  inputDrafts.delete(id)
  toast.success('已归档')
}

async function openArchiveManager() {
  showArchiveManager.value = true
  await store.fetchArchivedSessions()
}

async function restoreSession(id: number) {
  restoringId.value = id
  try {
    await store.unarchiveSession(id)
    toast.success('已还原')
  } finally {
    restoringId.value = null
  }
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
  <div class="relative flex h-[calc(100vh-4.5rem)] overflow-hidden bg-slate-50">
    <div
      v-if="renderError"
      class="fixed inset-0 z-[100] flex items-center justify-center bg-white/90"
    >
      <div class="text-center">
        <p class="text-red-500 text-sm mb-3">{{ renderError }}</p>
        <button
          class="px-4 py-2 bg-teal-600 text-white text-sm rounded-lg hover:bg-teal-700"
          @click="renderError = null"
        >
          重试
        </button>
      </div>
    </div>

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

    <div
      v-if="showSessionList"
      class="fixed inset-0 top-[72px] z-30 bg-slate-950/30 backdrop-blur-sm md:hidden"
      @click="showSessionList = false"
    />

    <aside
      class="fixed bottom-0 left-0 top-[72px] z-40 flex w-72 shrink-0 flex-col border-r border-slate-200 bg-white shadow-float transition-transform duration-300 md:static md:w-64 md:translate-x-0 md:shadow-none"
      :class="showSessionList ? 'translate-x-0' : '-translate-x-full'"
    >
      <div class="border-b border-slate-100 px-4 py-4">
        <p class="mb-3 text-[10px] font-semibold uppercase tracking-[0.16em] text-slate-400">Conversations</p>
        <button
          class="h-10 w-full rounded-xl bg-teal-700 text-sm font-semibold text-white shadow-sm transition hover:bg-teal-800"
          @click="newSession"
        >
          + 新对话
        </button>
      </div>
      <div class="flex-1 overflow-y-auto">
        <div v-if="store.sessions.length === 0" class="px-4 py-6 text-xs text-gray-400 text-center">
          暂无对话，点击“新对话”开始
        </div>
        <ul class="py-1">
          <li
            v-for="s in store.sessions"
            :key="s.id"
            class="group px-3 py-2 cursor-pointer hover:bg-gray-100 rounded-lg mx-1 my-0.5 transition"
            :class="{ 'bg-teal-50': store.currentSession?.id === s.id }"
            @click="selectSession(s.id)"
          >
            <div class="flex items-center justify-between gap-1">
              <div v-if="renamingId === s.id" class="flex-1 flex gap-1">
                <input
                  v-model="renameTitle"
                  class="flex-1 text-xs h-6 px-1 border rounded"
                  autofocus
                  @click.stop
                  @keyup.enter="submitRename(s.id)"
                  @keyup.escape="renamingId = null"
                />
                <button
                  class="text-xs text-teal-600 px-1"
                  title="保存"
                  aria-label="保存"
                  @click.stop="submitRename(s.id)"
                >
                  ✓
                </button>
              </div>
              <div v-else class="flex-1 min-w-0">
                <div class="text-sm text-gray-700 truncate">{{ s.title }}</div>
                <div class="text-xs text-gray-400 mt-0.5">{{ fmtSessionDate(s.createdAt) }}</div>
              </div>
              <div class="relative shrink-0">
                <button
                  class="flex items-center justify-center w-7 h-7 rounded-md text-base leading-none text-gray-500 hover:bg-white hover:text-gray-700 hover:shadow transition"
                  title="更多操作"
                  aria-label="更多操作"
                  @click="toggleSessionMenu(s.id, $event)"
                >
                  ⋯
                </button>
                <div
                  v-if="menuOpenId === s.id"
                  class="absolute right-0 top-8 z-20 w-32 bg-white rounded-lg shadow-lg border border-gray-100 py-1"
                  @click.stop
                >
                  <button
                    class="w-full flex items-center gap-2 px-3 py-2 text-sm text-gray-600 hover:bg-gray-50"
                    @click="startRename(s.id, s.title, $event); menuOpenId = null"
                  >
                    <span class="w-4 text-center">✎</span> 编辑
                  </button>
                  <button
                    class="w-full flex items-center gap-2 px-3 py-2 text-sm text-gray-600 hover:bg-amber-50 hover:text-amber-600"
                    @click="archiveSession(s.id, $event); menuOpenId = null"
                  >
                    <span class="w-4 text-center">▾</span> 归档
                  </button>
                  <button
                    class="w-full flex items-center gap-2 px-3 py-2 text-sm text-red-500 hover:bg-red-50"
                    @click="deleteSession(s.id, $event); menuOpenId = null"
                  >
                    <span class="w-4 text-center">×</span> 删除
                  </button>
                </div>
              </div>
            </div>
          </li>
        </ul>
      </div>
      <div class="border-t border-slate-100 px-4 py-3">
        <button
          class="w-full h-8 rounded-lg text-xs text-gray-500 hover:text-teal-600 hover:bg-gray-100 transition"
          @click="openArchiveManager"
        >
          归档管理
        </button>
      </div>
    </aside>

    <div class="flex-1 flex flex-col min-w-0">
      <div class="border-b border-slate-200 bg-white px-4 py-3 sm:px-6">
        <div class="flex items-center justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <button
              class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 md:hidden"
              aria-label="打开对话列表"
              @click="showSessionList = true"
            >
              ☰
            </button>
            <span class="truncate text-sm font-semibold text-slate-800">
            {{ store.currentSession?.title ?? 'Mirai Chat' }}
            </span>
          </div>
          <span
            v-if="store.contextUsage"
            class="px-1.5 py-0.5 rounded text-[10px]"
            :class="store.contextUsage.percentUsed > 40 ? 'bg-yellow-50 text-yellow-700' : 'bg-gray-100 text-gray-400'"
            :title="`${store.contextUsage.estimatedTokens}/${store.contextUsage.maxTokens} tokens, ${store.contextUsage.messageCount} 条消息`"
          >
            {{ store.contextUsage.percentUsed }}%
          </span>
        </div>
      </div>

      <div
        ref="messagesContainer"
        class="flex-1 space-y-5 overflow-y-auto bg-slate-50 px-4 py-5 sm:px-6 lg:px-10"
      >
        <div v-if="store.loading" class="text-center text-gray-400 text-sm">加载中...</div>

        <div
          v-if="store.agentPlan"
          class="max-w-[70%] rounded-2xl px-4 py-3 bg-teal-50 border border-teal-200 text-sm"
        >
          <p class="font-medium text-teal-800 mb-2">执行计划：{{ store.agentPlan.goal }}</p>
          <ol class="list-decimal list-inside space-y-1 text-teal-700">
            <li v-for="(step, i) in (store.agentPlan?.steps ?? [])" :key="i">
              {{ step.action }}
              <span class="text-xs text-gray-400">({{ (step.tools ?? []).join(', ') }})</span>
            </li>
          </ol>
          <div v-if="(store.agentPlan?.risks ?? []).length" class="mt-2 pt-2 border-t border-teal-200">
            <p class="text-xs text-yellow-600">{{ (store.agentPlan?.risks ?? []).join('；') }}</p>
          </div>
        </div>

        <div
          v-else-if="!store.currentSession || store.currentSession.messages.length === 0"
          class="mx-auto mt-20 max-w-sm rounded-2xl border border-dashed border-slate-300 bg-white/70 px-8 py-12 text-center text-sm text-slate-400"
        >
          开始对话吧
        </div>

        <div
          v-for="msg in store.currentSession?.messages"
          :key="msg.id"
          class="flex flex-col"
          :class="msg.role === 'user' ? 'items-end' : 'items-start'"
        >
          <span class="text-xs text-gray-400 mb-1 px-1">{{ fmtMsgTime(msg.createdAt) }}</span>
          <div
            class="max-w-[88%] rounded-2xl px-4 py-3 text-sm leading-relaxed sm:max-w-[76%] lg:max-w-[68%]"
            :class="msg.role === 'user'
              ? 'bg-teal-600 text-white rounded-br-sm'
              : 'bg-white text-gray-800 border border-gray-100 shadow-sm rounded-bl-sm prose prose-sm max-w-none'"
          >
            <div v-if="msg.role === 'user'" v-html="renderUserContent(msg.content)" />
            <template v-else>
              <details
                v-if="splitAssistantContent(msg.content).thinking"
                class="mb-3 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-[11px] text-slate-600"
              >
                <summary class="cursor-pointer select-none font-medium text-slate-500">思考过程</summary>
                <div class="mt-2 prose max-w-none !text-[11px] leading-relaxed" v-html="safeMarkdown(splitAssistantContent(msg.content).thinking)" />
              </details>
              <div v-html="safeMarkdown(splitAssistantContent(msg.content).answer)" />
            </template>
          </div>
        </div>

        <div v-if="store.streamMessage && store.streamSessionId === store.currentSession?.id" class="flex flex-col items-start">
          <span class="text-xs text-gray-400 mb-1 px-1">{{ fmtMsgTime(store.streamMessage.createdAt) }}</span>
          <div
            class="max-w-[88%] rounded-2xl rounded-bl-sm border border-slate-200 bg-white px-4 py-3 text-sm leading-relaxed text-slate-800 shadow-sm prose prose-sm sm:max-w-[76%] lg:max-w-[68%]"
          >
            <details
              v-if="splitAssistantContent(store.streamMessage.content, true).thinking"
              class="mb-3 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-[11px] text-slate-600"
              open
            >
              <summary class="cursor-pointer select-none font-medium text-slate-500">思考过程</summary>
              <div class="mt-2 prose max-w-none !text-[11px] leading-relaxed" v-html="safeMarkdown(splitAssistantContent(store.streamMessage.content, true).thinking)" />
            </details>
            <div v-html="safeMarkdown(splitAssistantContent(store.streamMessage.content, true).answer)" />
          </div>
          <div v-if="store.toolCalls.length > 0" class="mt-1.5 space-y-1">
            <div
              v-for="tc in store.toolCalls"
              :key="tc.id"
              class="flex items-center gap-2 text-xs text-gray-400 animate-pulse"
            >
              <span class="inline-block w-2 h-2 rounded-full bg-teal-400 animate-pulse"></span>
              <span>{{ tc.label }}</span>
            </div>
          </div>
          <div
            v-else-if="store.currentToolCall && !store.streamMessage.content"
            class="mt-1 flex items-center gap-2 text-xs text-gray-400 animate-pulse"
          >
            <span class="inline-block w-2 h-2 rounded-full bg-teal-400"></span>
            {{ store.currentToolCall }}
          </div>
        </div>
      </div>

      <div
        class="h-2 shrink-0 flex items-center justify-center bg-white border-t border-gray-200 cursor-row-resize select-none hover:bg-gray-50"
        title="拖动调整输入框高度"
        @mousedown="startInputResize"
      >
        <div class="w-10 h-1 rounded-full bg-gray-300"></div>
      </div>

      <div class="px-6 py-4 bg-white">
        <div
          v-if="store.pendingAttachments.length > 0 || uploadingFiles.size > 0"
          class="flex flex-wrap gap-2 mb-2"
        >
          <div
            v-for="name in uploadingFiles"
            :key="'uploading-' + name"
            class="flex items-center gap-1 px-2 py-1 rounded-lg bg-gray-100 text-xs text-gray-400 animate-pulse"
          >
            <span>...</span>
            <span class="max-w-[120px] truncate">{{ name }}</span>
          </div>
          <div
            v-for="(att, idx) in store.pendingAttachments"
            :key="idx"
            class="flex items-center gap-1 px-2 py-1 rounded-lg bg-teal-50 border border-teal-200 text-xs text-teal-700"
          >
            <span>{{ getFileIcon(att.fileType) }}</span>
            <span class="max-w-[120px] truncate" :title="att.fileName">{{ att.fileName }}</span>
            <button
              class="ml-1 text-teal-400 hover:text-red-500 leading-none"
              @click="removeAttachment(idx)"
            >
              x
            </button>
          </div>
        </div>

        <div class="flex gap-3 items-end">
          <input
            ref="fileInputRef"
            type="file"
            :accept="ACCEPTED_TYPES"
            multiple
            class="hidden"
            @change="handleFileSelect"
          />
          <button
            class="shrink-0 h-10 w-10 rounded-xl border border-gray-200 text-gray-400 hover:text-teal-600 hover:border-teal-300 flex items-center justify-center transition"
            :disabled="store.sending"
            title="上传文件（支持 PDF / Word / Excel / 文本 / 图片）"
            aria-label="上传文件"
            @click="triggerFileInput"
          >
            <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path
                d="M21.4 11.6 12.2 20.8a6 6 0 0 1-8.5-8.5l9.2-9.2a4 4 0 0 1 5.7 5.7l-9.3 9.2a2 2 0 0 1-2.8-2.8l8.6-8.6"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </button>
          <button
            class="shrink-0 h-10 w-10 rounded-xl border flex items-center justify-center transition"
            :class="showWorkspaceBrowser
              ? 'bg-teal-50 border-teal-300 text-teal-600'
              : 'border-gray-200 text-gray-400 hover:text-teal-600 hover:border-teal-300'"
            :disabled="store.sending"
            title="从工作区选择文件附加到消息"
            aria-label="从工作区选择文件"
            @click="showWorkspaceBrowser = !showWorkspaceBrowser"
          >
            <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path
                d="M3.5 6.5a2 2 0 0 1 2-2h4.2l2 2H18.5a2 2 0 0 1 2 2v1H3.5v-3Z"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linejoin="round"
              />
              <path
                d="M3.5 9.5h17l-1.4 8.1a2 2 0 0 1-2 1.7H6.9a2 2 0 0 1-2-1.7L3.5 9.5Z"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linejoin="round"
              />
            </svg>
          </button>
          <textarea
            v-model="inputContent"
            :style="{ height: inputHeightPx + 'px' }"
            placeholder="输入消息，Enter 发送，Shift+Enter 换行"
            class="flex-1 px-4 py-2 rounded-xl border border-gray-200 text-sm resize-none overflow-y-auto focus:outline-none focus:ring-2 focus:ring-teal-200"
            @keydown="handleKeydown"
            @paste="handlePaste"
          />
          <button
            class="shrink-0 h-10 px-5 rounded-xl bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-50"
            :disabled="store.sending || (!inputContent.trim() && store.pendingAttachments.length === 0)"
            @click="send"
          >
            发送
          </button>
        </div>
      </div>

      <Teleport to="body">
        <div
          v-if="store.pendingConfirm"
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/30"
        >
          <div class="bg-white rounded-2xl shadow-xl w-96 max-w-[90vw] p-6">
            <div class="flex items-center gap-3 mb-4">
              <span class="text-2xl">{{ store.pendingConfirm.riskLevel === 'dangerous' ? '!' : 'i' }}</span>
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
                :class="store.pendingConfirm.riskLevel === 'dangerous' ? 'bg-red-600 hover:bg-red-700' : 'bg-teal-600 hover:bg-teal-700'"
                @click="store.confirmToolCall(true)"
              >
                确认执行
              </button>
            </div>
          </div>
        </div>
      </Teleport>

      <Teleport to="body">
        <div
          v-if="showArchiveManager"
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/30"
          @click.self="showArchiveManager = false"
        >
          <div class="bg-white rounded-2xl shadow-xl w-[420px] max-w-[90vw] max-h-[80vh] flex flex-col p-6">
            <div class="flex items-center justify-between mb-4 shrink-0">
              <h3 class="text-sm font-medium text-gray-800">归档管理</h3>
              <button
                class="text-gray-400 hover:text-gray-600 text-lg leading-none"
                aria-label="关闭"
                @click="showArchiveManager = false"
              >
                ✕
              </button>
            </div>
            <div class="flex-1 overflow-y-auto -mx-2 px-2">
              <div v-if="store.archivedLoading" class="py-8 text-center text-xs text-gray-400">加载中…</div>
              <div v-else-if="store.archivedSessions.length === 0" class="py-8 text-center text-xs text-gray-400">
                暂无已归档内容
              </div>
              <ul v-else class="space-y-1">
                <li
                  v-for="s in store.archivedSessions"
                  :key="s.id"
                  class="flex items-center justify-between gap-2 px-3 py-2 rounded-lg hover:bg-gray-50"
                >
                  <div class="min-w-0">
                    <div class="text-sm text-gray-700 truncate">{{ s.title }}</div>
                    <div class="text-xs text-gray-400 mt-0.5">{{ fmtSessionDate(s.updatedAt) }}</div>
                  </div>
                  <button
                    class="shrink-0 text-xs px-2 py-1 rounded border border-gray-200 text-gray-500 hover:text-teal-600 hover:border-teal-300 disabled:opacity-50"
                    :disabled="restoringId === s.id"
                    @click="restoreSession(s.id)"
                  >
                    {{ restoringId === s.id ? '还原中…' : '还原' }}
                  </button>
                </li>
              </ul>
            </div>
          </div>
        </div>
      </Teleport>
    </div>
  </div>
</template>
