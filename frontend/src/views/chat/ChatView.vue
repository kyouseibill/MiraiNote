<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, nextTick, watch, onErrorCaptured } from 'vue'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'
import {
  downloadExportFile as downloadExportFileBase,
  fetchExportPreview,
  fileNameFromUrl,
  isExportDownload,
  isExportPreviewable,
  isMarkdownExport,
  type ExportPreviewResult,
  exportApiPath,
} from '@/composables/useExportDownload'
import {
  ACCOUNT_TIMEZONE,
  ACCOUNT_TIMEZONE_HINT,
  formatAccountDate,
  formatAccountDateTime,
  sessionDateGroup as sessionDateGroupByAccountTz,
} from '@/utils/accountTime'
import { chatApi } from '@/api/chat'
import WorkspaceBrowser from '@/components/WorkspaceBrowser.vue'
import { staticUrl } from '@/composables/useStaticUrl'
import type { ChatMessage, ChatProject, ToolCallEvent } from '@/types/chat'
import AppDialog from '@/components/AppDialog.vue'
import {
  IconPlus,
  IconSearch,
  IconX,
  IconMessageCircle,
  IconDots,
  IconFolder,
  IconPaperclip,
  IconArrowUp,
  IconArrowDown,
  IconArchive,
  IconPencil,
  IconPin,
  IconTrash,
  IconCopy,
  IconGitBranch,
  IconRefresh,
  IconCheck,
  IconLoader2,
  IconArrowRight,
  IconCircleDashed,
  IconFileText,
  IconDownload,
  IconSquare,
  IconLayoutSidebar,
  IconMaximize,
  IconMinimize,
  IconBulb,
} from '@tabler/icons-vue'

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
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
  }
}

async function downloadExportFile(url: string, fileName = fileNameFromUrl(url)) {
  try {
    await downloadExportFileBase(url, fileName)
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : '文件下载失败，请稍后重试'
    toast.error(message)
  }
}

function onMessageLinkClick(event: MouseEvent) {
  const target = event.target
  if (!(target instanceof Element)) return
  const link = target.closest('a')
  const href = link?.getAttribute('href')
  if (!href || !isExportDownload(href)) return
  event.preventDefault()
  void downloadExportFile(href, fileNameFromUrl(href))
}

// 全局剥离思考块（兼容 <think> 变体；未闭合的思考块剥到末尾）
function stripThinkingBlocks(text: string): string {
  return text.replace(/<(?:thinking|think)>[\s\S]*?(?:<\/(?:thinking|think)>|$)/gi, '')
}

// 清理残留的思考/答案标签，以及模型泄漏的内部工具调用标记（｜｜DSML｜｜，全角/半角竖线变体）
function stripResidualTags(text: string): string {
  return text
    .replace(/<\/?[|｜]{1,2}\s*DSML\s*[|｜]{1,2}\w*>/gi, '')
    .replace(/<\/?(?:thinking|think|answer)>/gi, '')
    .trim()
}

function splitAssistantContent(
  content: string | null | undefined,
  allowOpenThinking = false,
): { thinking: string; answer: string } {
  const raw = String(content ?? '')
  const hasClosedThinking = /<(?:thinking|think)>[\s\S]*?<\/(?:thinking|think)>/i.test(raw)
  if (!hasClosedThinking && allowOpenThinking) {
    const openThinkingMatch = raw.match(/<(?:thinking|think)>([\s\S]*)$/i)
    if (openThinkingMatch) {
      return { thinking: openThinkingMatch[1].trim(), answer: '' }
    }
  }

  if (!hasClosedThinking) {
    return {
      thinking: '',
      answer: stripResidualTags(raw),
    }
  }

  // 多轮工具调用会产生多个思考块，全部合并展示；
  // 末尾未闭合的思考块（流式进行中）也一并纳入。
  const thinkingParts: string[] = []
  let afterClosed = 0
  for (const m of raw.matchAll(/<(?:thinking|think)>([\s\S]*?)<\/(?:thinking|think)>/gi)) {
    const part = stripResidualTags(m[1])
    if (part) thinkingParts.push(part)
    afterClosed = (m.index ?? 0) + m[0].length
  }
  const openTail = raw.slice(afterClosed).match(/<(?:thinking|think)>([\s\S]*)$/i)
  if (openTail && openTail[1].trim()) thinkingParts.push(stripResidualTags(openTail[1]))
  const thinking = thinkingParts.join('\n\n---\n\n')

  // 正文 = 剥离所有思考块与残留标签后的剩余文本，
  // 这样混入正文的 thinking 标签/思考文字不会泄漏（含多轮拼接的情况）。
  const answer = stripResidualTags(stripThinkingBlocks(raw))

  return { thinking, answer }
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
const showArchiveManager = ref(false)
const showSessionList = ref(false)
const isNarrow = ref(window.matchMedia('(max-width: 767px)').matches)
const narrowMedia = window.matchMedia('(max-width: 767px)')
function onViewportChange(event: MediaQueryListEvent) {
  isNarrow.value = event.matches
  if (!event.matches) showSessionList.value = false
}
const restoringId = ref<number | null>(null)
const searchQuery = ref('')
const showProjectManager = ref(false)
const editingProjectId = ref<number | null>(null)
const projectForm = reactive({ name: '', instructions: '', color: '#4c6178', icon: '◇' })
const showArtifacts = ref(false)
let searchTimer: ReturnType<typeof setTimeout> | null = null
const sidebarCollapsed = ref(false)

/** Chat / Work 双模式：会话数据共用，只改默认 UI 侧重。 */
const CHAT_UI_MODE_KEY = 'mirainote:chat:uiMode'
type ChatUiMode = 'chat' | 'work'
function readInitialUiMode(): ChatUiMode {
  try {
    const saved = localStorage.getItem(CHAT_UI_MODE_KEY)
    if (saved === 'chat' || saved === 'work') return saved
  } catch {
    // private mode / quota
  }
  return 'chat'
}
const uiMode = ref<ChatUiMode>(readInitialUiMode())
const isWorkMode = computed(() => uiMode.value === 'work')

const toolStatusLabel: Record<ToolCallEvent['status'], string> = {
  running: '运行中',
  success: '成功',
  failure: '失败',
}

/** 仅展示本页生命周期内的工具卡；永不读 msg.toolEvents（API/缓存字段）。 */
function toolEventsForMessage(msg: { id?: number; streaming?: boolean }) {
  if (msg.streaming) return store.toolCalls
  if (typeof msg.id === 'number') return store.pageToolEventsFor(msg.id)
  return []
}

function toolEventsSummary(events: ToolCallEvent[]) {
  const running = events.filter((e) => e.status === 'running').length
  const success = events.filter((e) => e.status === 'success').length
  const failure = events.filter((e) => e.status === 'failure').length
  const parts: string[] = [`${events.length} 项`]
  if (running) parts.push(`${running} 运行中`)
  if (success) parts.push(`${success} 成功`)
  if (failure) parts.push(`${failure} 失败`)
  return parts.join(' · ')
}

function setUiMode(mode: ChatUiMode) {
  if (uiMode.value === mode) return
  uiMode.value = mode
  try {
    localStorage.setItem(CHAT_UI_MODE_KEY, mode)
  } catch {
    // ignore
  }
}
const inputRef = ref<HTMLTextAreaElement | null>(null)
const searchRef = ref<HTMLInputElement | null>(null)
const searchComposing = ref(false)
const inputExpanded = ref(false)
const atBottom = ref(true)
const creatingSession = ref(false)
const actionBusy = ref(false)
const uiError = ref('')
const uiErrorKind = ref<'load' | 'send' | 'create'>('load')
const copiedId = ref<number | null>(null)
let copiedTimer: ReturnType<typeof setTimeout> | null = null
const dialog = ref<{
  kind: 'delete' | 'archive' | 'project-delete' | 'rename' | 'edit'
  id: number
  title: string
  description: string
} | null>(null)
const dialogText = ref('')
const dialogError = ref('')
const projectError = ref('')
const currentProject = computed(() => store.projects.find((p) => p.id === store.currentSession?.projectId))
const isCurrentStreaming = computed(() => store.sending && store.streamSessionId === store.currentSession?.id)
const displayMessages = computed(() => [
  ...(store.currentSession?.messages ?? []).map((message) => ({
    ...message,
    streaming: false,
    ...splitAssistantContent(message.content),
  })),
  ...(store.streamMessage && store.streamSessionId === store.currentSession?.id
    ? [
        {
          ...store.streamMessage,
          streaming: true,
          ...splitAssistantContent(store.streamMessage.content, true),
        },
      ]
    : []),
])
const starters = [
  {
    title: '整理工作，写一份周报',
    hint: '把零散记录整理成清晰的进展',
    icon: IconFileText,
    prompt: '帮我整理本周的工作记录，写一份简洁的周报。',
  },
  {
    title: '一起梳理一个想法',
    hint: '从一个念头，走向可行的下一步',
    icon: IconBulb,
    prompt: '我有一个想法，想和你一起梳理：',
  },
  {
    title: '读懂一份文件',
    hint: '提炼重点，发现值得关注的细节',
    icon: IconFolder,
    prompt: '请阅读我附上的文件，整理核心内容和需要关注的问题。',
  },
]

async function focusInput() {
  await nextTick()
  inputRef.value?.focus()
}
async function useStarter(prompt: string) {
  inputContent.value = prompt
  await focusInput()
}
async function resizeInput() {
  await nextTick()
  const el = inputRef.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${inputExpanded.value ? 220 : Math.min(160, Math.max(64, el.scrollHeight))}px`
}
function handleScroll() {
  const el = messagesContainer.value
  if (el) atBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 72
}
async function copyMessage(message: ChatMessage) {
  try {
    await navigator.clipboard.writeText(
      message.role === 'assistant' ? splitAssistantContent(message.content).answer : message.content,
    )
    copiedId.value = message.id
    if (copiedTimer) clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => {
      copiedId.value = null
    }, 1800)
    toast.success('已复制消息')
  } catch {
    toast.error('复制失败，请选择消息文字后复制')
  }
}

function scheduleSearch() {
  if (searchTimer) clearTimeout(searchTimer)
  if (searchComposing.value) return
  searchTimer = setTimeout(() => runSearch(), 300)
}
async function runSearch() {
  if (searchTimer) clearTimeout(searchTimer)
  if (searchComposing.value) return
  uiError.value = ''
  uiErrorKind.value = 'load'
  try {
    await store.searchSessions(searchQuery.value)
  } catch {
    uiError.value = '对话列表加载失败，请重试。'
  }
}
async function clearSearch() {
  searchQuery.value = ''
  await runSearch()
  searchRef.value?.focus()
}
function onSearchCompositionEnd() {
  searchComposing.value = false
  scheduleSearch()
}
function openDialog(
  kind: NonNullable<typeof dialog.value>['kind'],
  id: number,
  title: string,
  description: string,
  text = '',
) {
  menuOpenId.value = null
  dialogText.value = text
  dialogError.value = ''
  dialog.value = { kind, id, title, description }
}
async function submitDialog() {
  const target = dialog.value
  if (!target || actionBusy.value) return
  const text = dialogText.value.trim()
  if ((target.kind === 'rename' || target.kind === 'edit') && !text) {
    dialogError.value = '请填写内容后再保存。'
    return
  }
  actionBusy.value = true
  dialogError.value = ''
  try {
    if (target.kind === 'delete') {
      await store.deleteSession(target.id)
      inputDrafts.delete(target.id)
      toast.success('对话已删除')
    }
    if (target.kind === 'archive') {
      await store.archiveSession(target.id)
      inputDrafts.delete(target.id)
      toast.success('对话已归档')
    }
    if (target.kind === 'project-delete') {
      await store.deleteProject(target.id)
      resetProjectForm()
      toast.success('项目已删除')
    }
    if (target.kind === 'rename') {
      await store.updateTitle(target.id, text)
      toast.success('对话已重命名')
    }
    if (target.kind === 'edit') {
      const messages = store.currentSession?.messages ?? []
      const index = messages.findIndex((m) => m.id === target.id)
      const previousId = index > 0 && messages[index - 1].id > 0 ? messages[index - 1].id : null
      await store.branchSession({
        messageId: previousId,
        title: `${store.currentSession?.title || '对话'} · 编辑`,
      })
      inputContent.value = text
      dialog.value = null
      await send()
    }
    dialog.value = null
  } catch {
    dialogError.value = '操作未完成，请重试。已填写的内容仍然保留。'
  } finally {
    actionBusy.value = false
  }
}

const groupedSessions = computed(() => {
  const groups = new Map<string, typeof store.sessions>()
  const sorted = [...store.sessions].sort(
    (a, b) =>
      Number(b.isPinned) - Number(a.isPinned) ||
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  )
  for (const session of sorted) {
    const label = session.isPinned ? '已置顶' : sessionDateGroup(session.updatedAt)
    if (!groups.has(label)) groups.set(label, [])
    groups.get(label)!.push(session)
  }
  return Array.from(groups, ([label, sessions]) => ({ label, sessions }))
})

interface ConversationArtifact {
  name: string
  url: string
  extension: string
  messageId: number
  createdAt: string
}

const artifacts = computed<ConversationArtifact[]>(() => {
  const results: ConversationArtifact[] = []
  const seen = new Set<string>()
  for (const message of store.currentSession?.messages ?? []) {
    if (message.role !== 'assistant') continue
    const markdownLink = /\[([^\]]+)\]\(([^)\s]+)\)/g
    let match: RegExpExecArray | null
    while ((match = markdownLink.exec(message.content)) !== null) {
      const url = match[2]
      try {
        const parsed = new URL(url, window.location.origin)
        if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.pathname.includes('/exports/')) continue
      } catch {
        continue
      }
      if (seen.has(url)) continue
      seen.add(url)
      const name = match[1] || url.split('/').pop() || '导出文件'
      results.push({
        name,
        url,
        extension: fileExtension(name),
        messageId: message.id,
        createdAt: message.createdAt,
      })
    }
  }
  return results.reverse()
})

type ArtifactPreviewState =
  | { status: 'loading' }
  | { status: 'error' }
  | ({ status: 'ready' } & ExportPreviewResult)

const artifactPreviewMap = ref<Record<string, ArtifactPreviewState>>({})

function revokeArtifactPreviews() {
  for (const preview of Object.values(artifactPreviewMap.value)) {
    if (preview.status === 'ready' && preview.objectUrl) {
      URL.revokeObjectURL(preview.objectUrl)
    }
  }
  artifactPreviewMap.value = {}
}

async function loadArtifactPreviews() {
  const targets = artifacts.value.filter((item) => isExportPreviewable(item.extension))
  for (const artifact of targets) {
    const existing = artifactPreviewMap.value[artifact.url]
    if (existing?.status === 'ready' || existing?.status === 'loading') continue
    artifactPreviewMap.value = {
      ...artifactPreviewMap.value,
      [artifact.url]: { status: 'loading' },
    }
    try {
      const result = await fetchExportPreview(artifact.url)
      artifactPreviewMap.value = {
        ...artifactPreviewMap.value,
        [artifact.url]: { status: 'ready', ...result },
      }
    } catch {
      artifactPreviewMap.value = {
        ...artifactPreviewMap.value,
        [artifact.url]: { status: 'error' },
      }
    }
  }
}

watch(showArtifacts, (open) => {
  if (open) void loadArtifactPreviews()
  else revokeArtifactPreviews()
})

watch(artifacts, () => {
  if (showArtifacts.value) void loadArtifactPreviews()
})

function artifactPreviewOf(url: string): ArtifactPreviewState | undefined {
  return artifactPreviewMap.value[url]
}

function artifactPreviewText(url: string): string | undefined {
  const preview = artifactPreviewOf(url)
  return preview?.status === 'ready' ? preview.text : undefined
}

function artifactPreviewObjectUrl(url: string): string | undefined {
  const preview = artifactPreviewOf(url)
  return preview?.status === 'ready' ? preview.objectUrl : undefined
}

function artifactPreviewKind(url: string): ExportPreviewResult['kind'] | undefined {
  const preview = artifactPreviewOf(url)
  return preview?.status === 'ready' ? preview.kind : undefined
}

function sessionDateGroup(iso: string): string {
  return sessionDateGroupByAccountTz(iso, ACCOUNT_TIMEZONE)
}

async function changeProject(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  if (searchTimer) clearTimeout(searchTimer)
  searchQuery.value = ''
  try {
    await store.selectProject(value ? Number(value) : null)
    if (store.sessions.length > 0) await selectSession(store.sessions[0].id)
  } catch {
    uiError.value = '项目对话加载失败，请重试。'
  }
}

function resetProjectForm() {
  editingProjectId.value = null
  projectError.value = ''
  Object.assign(projectForm, { name: '', instructions: '', color: '#4c6178', icon: '◇' })
}

function editProject(project: ChatProject) {
  projectError.value = ''
  editingProjectId.value = project.id
  Object.assign(projectForm, {
    name: project.name,
    instructions: project.instructions,
    color: project.color,
    icon: project.icon,
  })
}

async function saveProject() {
  if (actionBusy.value) return
  if (!projectForm.name.trim()) {
    projectError.value = '请填写项目名称。'
    return
  }
  actionBusy.value = true
  projectError.value = ''
  const payload = {
    name: projectForm.name.trim(),
    instructions: projectForm.instructions.trim(),
    color: projectForm.color,
    icon: projectForm.icon.trim() || '◇',
  }
  try {
    if (editingProjectId.value == null) {
      const project = await store.createProject(payload)
      await store.selectProject(project.id)
    } else {
      await store.updateProject(editingProjectId.value, payload)
    }
    resetProjectForm()
    toast.success('项目已保存')
  } catch {
    projectError.value = '项目保存失败，请重试。'
  } finally {
    actionBusy.value = false
  }
}

async function removeProject(project: ChatProject) {
  openDialog(
    'project-delete',
    project.id,
    `删除“${project.name}”？`,
    '项目内对话会保留，并移回全部对话。项目专属指令将被删除。',
  )
}

async function removeEditingProject() {
  const project = store.projects.find((item) => item.id === editingProjectId.value)
  if (project) await removeProject(project)
}

async function togglePinned(sessionId: number, isPinned: boolean) {
  if (actionBusy.value) return
  actionBusy.value = true
  try {
    await store.setSessionPinned(sessionId, !isPinned)
    if (searchQuery.value.trim()) await store.searchSessions(searchQuery.value)
    menuOpenId.value = null
  } catch {
    toast.error('置顶状态更新失败，请重试')
  } finally {
    actionBusy.value = false
  }
}

async function moveSession(sessionId: number, event: Event) {
  if (actionBusy.value) return
  actionBusy.value = true
  const value = (event.target as HTMLSelectElement).value
  try {
    await store.assignSessionProject(sessionId, value ? Number(value) : null)
    if (searchQuery.value.trim()) await store.searchSessions(searchQuery.value)
    menuOpenId.value = null
  } catch {
    toast.error('移动对话失败，请重试')
  } finally {
    actionBusy.value = false
  }
}

async function branchFromMessage(message: ChatMessage) {
  if (store.isTemporary || message.id <= 0 || actionBusy.value || store.sending) return
  actionBusy.value = true
  try {
    await store.branchSession({
      messageId: message.id,
      title: `${store.currentSession?.title || '对话'} · 分支`,
    })
    toast.success('已创建分支对话')
    await focusInput()
  } catch {
    toast.error('创建分支失败，请重试')
  } finally {
    actionBusy.value = false
  }
}

async function editUserMessage(message: ChatMessage) {
  if (store.isTemporary || message.role !== 'user') return
  openDialog('edit', message.id, '编辑消息', '发送后会创建一段分支对话，原对话保持完整。', message.content)
}

async function retryAssistantMessage(message: ChatMessage) {
  if (store.isTemporary || message.role !== 'assistant' || actionBusy.value || store.sending) return
  const messages = store.currentSession?.messages ?? []
  const index = messages.findIndex((item) => item.id === message.id)
  let userMessage: ChatMessage | undefined
  for (let i = index - 1; i >= 0; i--) {
    if (messages[i].role === 'user') {
      userMessage = messages[i]
      break
    }
  }
  if (!userMessage || userMessage.id <= 0) return
  actionBusy.value = true
  try {
    await store.branchSession({
      messageId: userMessage.id,
      title: `${store.currentSession?.title || '对话'} · 重试`,
    })
    inputContent.value = '请重新回答上一条消息，并给出更准确、完整的结果。'
    await send()
  } catch {
    toast.error('重新回答失败，请重试')
  } finally {
    actionBusy.value = false
  }
}

// 会话条目的单一入口“…”菜单（编辑/归档/删除）
const menuOpenId = ref<number | null>(null)
function toggleSessionMenu(id: number, e: Event) {
  e.stopPropagation()
  menuOpenId.value = menuOpenId.value === id ? null : id
}
function closeSessionMenu() {
  menuOpenId.value = null
}

onMounted(() => {
  window.addEventListener('click', closeSessionMenu)
  narrowMedia.addEventListener('change', onViewportChange)
})
onUnmounted(() => {
  window.removeEventListener('click', closeSessionMenu)
  narrowMedia.removeEventListener('change', onViewportChange)
  if (searchTimer) clearTimeout(searchTimer)
  if (copiedTimer) clearTimeout(copiedTimer)
  revokeArtifactPreviews()
  store.stopGeneration()
})

const ACCEPTED_TYPES = [
  '.pdf',
  '.docx',
  '.xlsx',
  '.xls',
  '.txt',
  '.md',
  '.csv',
  '.json',
  '.xml',
  '.html',
  '.yaml',
  '.yml',
  '.ts',
  '.js',
  '.tsx',
  '.jsx',
  '.py',
  '.cs',
  '.java',
  '.go',
  '.rs',
  '.sql',
  '.sh',
  '.bat',
  '.ps1',
  '.vue',
  '.css',
  '.log',
].join(',')

const LOCAL_TEXT_EXTENSIONS = new Set([
  '.txt',
  '.md',
  '.csv',
  '.json',
  '.xml',
  '.html',
  '.htm',
  '.yaml',
  '.yml',
  '.toml',
  '.ini',
  '.env',
  '.log',
  '.sql',
  '.ts',
  '.js',
  '.jsx',
  '.tsx',
  '.py',
  '.cs',
  '.java',
  '.cpp',
  '.c',
  '.h',
  '.go',
  '.rs',
  '.php',
  '.rb',
  '.sh',
  '.bat',
  '.ps1',
  '.vue',
  '.css',
  '.scss',
  '.less',
  '.conf',
  '.config',
  '.csproj',
  '.sln',
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
        toast.warning('当前模型不支持图片解析，请上传 PDF、Word、Excel 或文本文件')
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
  return (
    text.slice(0, LOCAL_TEXT_MAX_CHARS) +
    `\n\n... [文件内容已截断，共 ${text.length} 字符，文件名：${fileName}]`
  )
}

function readFileAsText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result ?? ''))
    reader.onerror = () => reject(reader.error ?? new Error('读取文件失败'))
    reader.readAsText(file)
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
  return merged.filter(
    (file, index, arr) =>
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

function onWorkspaceAttach(file: {
  fileName: string
  fileType: string
  textContent: string
  mimeType?: string
  dataUrl?: string
  isImage?: boolean
}) {
  store.pendingAttachments.push(file)
  showWorkspaceBrowser.value = false
}

function fmtSessionDate(iso: string): string {
  return formatAccountDate(iso, ACCOUNT_TIMEZONE)
}

function fmtMsgTime(iso: string): string {
  return formatAccountDateTime(iso, ACCOUNT_TIMEZONE)
}

const copiedArtifactUrl = ref<string | null>(null)

function artifactPath(url: string): string {
  return exportApiPath(url) || url
}

async function copyArtifactPath(url: string) {
  const path = artifactPath(url)
  try {
    await navigator.clipboard.writeText(path)
    copiedArtifactUrl.value = url
    toast.success('已复制路径')
    window.setTimeout(() => {
      if (copiedArtifactUrl.value === url) copiedArtifactUrl.value = null
    }, 1600)
  } catch {
    toast.error('复制失败，请手动选择路径文字后复制')
  }
}

async function scrollToBottom() {
  await nextTick()
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = displayMessages.value.length ? messagesContainer.value.scrollHeight : 0
    atBottom.value = true
  }
}

async function newSession() {
  if (creatingSession.value || store.sending) return false
  creatingSession.value = true
  uiError.value = ''
  uiErrorKind.value = 'create'
  try {
    await store.createSession()
    newSessionDraft.value = ''
    showSessionList.value = false
    await focusInput()
    return true
  } catch {
    uiError.value = '新对话创建失败，请重试。'
    return false
  } finally {
    creatingSession.value = false
  }
}

function newTemporarySession() {
  store.startTemporarySession()
  inputDrafts.delete(0)
  newSessionDraft.value = ''
  showSessionList.value = false
  scrollToBottom()
}

async function selectSession(id: number) {
  uiError.value = ''
  uiErrorKind.value = 'load'
  try {
    await store.openSession(id)
    showSessionList.value = false
    scrollToBottom()
  } catch {
    uiError.value = '对话加载失败，请重新选择或重试。'
  }
}

async function send() {
  const text = inputContent.value.trim()
  if (
    (!text && store.pendingAttachments.length === 0) ||
    store.sending ||
    creatingSession.value ||
    uploadingFiles.value.size > 0
  )
    return
  if (!store.currentSession) {
    if (!(await newSession())) return
  }
  const targetId = store.currentSession?.id
  uiError.value = ''
  uiErrorKind.value = 'send'
  inputContent.value = ''
  atBottom.value = true
  void scrollToBottom()
  void focusInput()
  try {
    const outcome = shouldUseAgent(text)
      ? await store.sendAgentMessageStream(text || '请分析这些文件的内容')
      : await store.sendMessageStream(text || '请分析这些文件的内容')
    if (outcome === 'failed') restoreFailedDraft(targetId, text)
    if (atBottom.value) void scrollToBottom()
  } catch {
    restoreFailedDraft(targetId, text)
  }
}

function restoreFailedDraft(targetId: number | undefined, text: string) {
  if (targetId != null && !inputDrafts.get(targetId)) inputDrafts.set(targetId, text)
  if (store.currentSession?.id === targetId) {
    uiErrorKind.value = 'send'
    uiError.value = `${store.lastSendError ? `${store.lastSendError}。` : '回复未完成。'}消息和草稿已保留。你可以修改后重新发送。`
  }
}

async function retryFailedAction() {
  if (uiErrorKind.value === 'send') await send()
  else if (uiErrorKind.value === 'create') await newSession()
  else await reloadConversations()
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
  openDialog(
    'delete',
    id,
    '删除这段对话？',
    `“${store.sessions.find((s) => s.id === id)?.title || '对话'}”的全部消息将被删除，此操作无法撤销。`,
  )
}

async function archiveSession(id: number, e: Event) {
  e.stopPropagation()
  openDialog('archive', id, '归档这段对话？', '归档后会从列表收起，你可以随时在“归档管理”中还原。')
}

async function openArchiveManager() {
  showArchiveManager.value = true
  try {
    await store.fetchArchivedSessions()
  } catch {
    toast.error('归档列表加载失败，请关闭后重试')
  }
}

async function restoreSession(id: number) {
  if (restoringId.value != null) return
  restoringId.value = id
  try {
    await store.unarchiveSession(id)
    toast.success('已还原')
  } catch {
    toast.error('还原失败，请重试')
  } finally {
    restoringId.value = null
  }
}

function startRename(id: number, currentTitle: string, e: Event) {
  e.stopPropagation()
  openDialog('rename', id, '重命名对话', '给这段对话一个容易找到的名字。', currentTitle)
}

function handleKeydown(e: KeyboardEvent) {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    send()
  }
}

watch(
  [
    () => store.currentSession?.messages.length,
    () => store.streamMessage?.content,
    () => store.toolCalls.map((t) => `${t.id}:${t.status}:${t.detail ?? ''}:${t.resultSummary ?? ''}`).join('|'),
  ],
  () => {
    if (atBottom.value) void scrollToBottom()
  },
)
watch(
  () => store.currentSession?.id,
  () => {
    atBottom.value = true
    void scrollToBottom()
  },
)
watch([inputContent, inputExpanded], resizeInput)

onMounted(async () => {
  await reloadConversations()
  await resizeInput()
})

async function reloadConversations() {
  uiError.value = ''
  uiErrorKind.value = 'load'
  try {
    await Promise.all([store.fetchProjects(), store.searchSessions(searchQuery.value)])
    if (!store.currentSession && store.sessions.length > 0) await selectSession(store.sessions[0].id)
  } catch {
    uiError.value = '聊天加载失败，请检查网络后重试。'
  }
}
</script>

<template>
  <div class="chat-shell" data-testid="chat-shell" :class="isWorkMode ? 'is-mode-work' : 'is-mode-chat'" :data-chat-mode="uiMode" @keydown.esc="closeSessionMenu">
    <div v-if="renderError" class="chat-render-error" role="alert">
      <p>{{ renderError }}</p>
      <button class="chat-btn" @click="renderError = null">重试</button>
    </div>

    <AppDialog
      :open="showSessionList && isNarrow"
      title="对话列表"
      size="navigation"
      @close="showSessionList = false"
      ><div id="chat-mobile-navigation" class="chat-mobile-navigation"
    /></AppDialog>
    <Teleport to="#chat-mobile-navigation" :disabled="!isNarrow" defer>
      <aside
        id="chat-navigation"
        class="chat-sidebar"
        data-testid="chat-sidebar"
        :class="{ 'is-open': showSessionList, 'is-collapsed': sidebarCollapsed }"
        aria-label="对话导航"
      >
        <div class="chat-sidebar-top">
          <div class="chat-sidebar-heading">
            <span>对话</span><span class="chat-count">{{ store.sessions.length }}</span
            ><button
              class="chat-icon chat-mobile-close"
              aria-label="关闭对话列表"
              @click="showSessionList = false"
            >
              <IconX :size="18" />
            </button>
          </div>
          <button
            class="chat-new"
            data-testid="chat-new"
            :disabled="store.sending || creatingSession || uploadingFiles.size > 0"
            @click="newSession"
          >
            <IconLoader2 v-if="creatingSession" :size="17" class="chat-spin" /><IconPlus
              v-else
              :size="18"
            /><span>新对话</span>
          </button>
          <div class="chat-search">
            <IconSearch :size="16" />
            <input
              ref="searchRef"
              v-model="searchQuery"
              data-testid="chat-search"
              aria-label="搜索对话"
              placeholder="搜索对话与消息"
              @input="scheduleSearch"
              @compositionstart="searchComposing = true"
              @compositionend="onSearchCompositionEnd"
              @keydown.enter.prevent="runSearch"
            />
            <button
              v-if="searchQuery"
              data-testid="chat-search-clear"
              class="chat-icon"
              aria-label="清空搜索"
              @click="clearSearch"
            >
              <IconX :size="15" />
            </button>
          </div>
          <div class="chat-project-label">
            <label for="chat-project">项目空间</label
            ><button
              class="chat-icon"
              aria-label="管理项目"
              title="管理项目"
              :disabled="store.sending"
              @click="showProjectManager = true"
            >
              <IconPlus :size="16" />
            </button>
          </div>
          <div class="chat-project-select">
            <IconFolder :size="16" /><select
              id="chat-project"
              :value="store.selectedProjectId ?? ''"
              :disabled="store.sending || uploadingFiles.size > 0"
              @change="changeProject"
            >
              <option value="">全部对话</option>
              <option v-for="project in store.projects" :key="project.id" :value="project.id">
                {{ project.icon }} {{ project.name }} · {{ project.sessionCount }}
              </option>
            </select>
          </div>
        </div>
        <div class="chat-session-scroll">
          <p class="chat-tz-hint" :title="`历史分组按账户时区 ${ACCOUNT_TIMEZONE} 日切`">{{ ACCOUNT_TIMEZONE_HINT }}</p>
          <div v-if="store.sessionsLoading" class="chat-list-state" role="status">
            <IconLoader2 :size="18" class="chat-spin" />正在查找对话…
          </div>
          <div v-else-if="store.sessions.length === 0" class="chat-list-state">
            <IconMessageCircle :size="24" />
            <p>{{ searchQuery ? '没有找到相关对话' : '还没有对话' }}</p>
            <button v-if="searchQuery" class="chat-link" @click="clearSearch">清空搜索</button>
            <p v-else class="chat-subtle">从一个问题开始吧</p>
          </div>
          <section v-for="group in groupedSessions" :key="group.label" class="chat-session-group">
            <h2>{{ group.label }}</h2>
            <div
              v-for="s in group.sessions"
              :key="s.id"
              class="chat-session"
              :class="{ 'is-active': store.currentSession?.id === s.id }"
            >
              <button
                class="chat-session-select"
                :disabled="uploadingFiles.size > 0 || creatingSession"
                :aria-current="store.currentSession?.id === s.id ? 'true' : undefined"
                :title="s.title"
                @click="selectSession(s.id)"
              >
                <div class="chat-session-title">
                  <IconPin v-if="s.isPinned" :size="13" /><span>{{ s.title }}</span>
                </div>
                <p v-if="s.matchSnippet" class="chat-match">{{ s.matchSnippet }}</p>
                <span class="chat-session-date">{{ fmtSessionDate(s.updatedAt) }}</span>
              </button>
              <button
                class="chat-icon chat-session-more"
                :aria-label="`更多操作：${s.title}`"
                :aria-expanded="menuOpenId === s.id"
                :disabled="store.sending"
                title="更多操作"
                @click="toggleSessionMenu(s.id, $event)"
              >
                <IconDots :size="18" />
              </button>
              <div v-if="menuOpenId === s.id" class="chat-session-menu" @click.stop>
                <button @click="startRename(s.id, s.title, $event)"><IconPencil :size="15" />重命名</button>
                <button @click="togglePinned(s.id, s.isPinned)">
                  <IconPin :size="15" />{{ s.isPinned ? '取消置顶' : '置顶' }}
                </button>
                <label :for="`move-project-${s.id}`">移动到项目</label>
                <select
                  :id="`move-project-${s.id}`"
                  :value="s.projectId ?? ''"
                  @change="moveSession(s.id, $event)"
                >
                  <option value="">无项目</option>
                  <option v-for="project in store.projects" :key="project.id" :value="project.id">
                    {{ project.name }}
                  </option>
                </select>
                <button @click="archiveSession(s.id, $event)"><IconArchive :size="15" />归档</button>
                <button class="chat-danger-text" @click="deleteSession(s.id, $event)">
                  <IconTrash :size="15" />删除
                </button>
              </div>
            </div>
          </section>
        </div>
        <div class="chat-sidebar-footer">
          <button
            :class="{ 'is-selected': store.isTemporary }"
            :disabled="store.sending || creatingSession || uploadingFiles.size > 0"
            title="关闭即丢、不进列表"
            @click="newTemporarySession"
          >
            <IconCircleDashed :size="17" /><span>临时聊天</span><span class="chat-subtle">关闭即丢</span>
          </button>
          <button @click="openArchiveManager"><IconArchive :size="17" /><span>归档管理</span></button>
        </div>
      </aside>
    </Teleport>

    <section class="chat-main" aria-label="聊天内容">
      <header class="chat-header">
        <button
          class="chat-icon chat-desktop-toggle"
          data-testid="chat-sidebar-toggle"
          :aria-label="sidebarCollapsed ? '展开对话列表' : '收起对话列表'"
          :aria-expanded="!sidebarCollapsed"
          aria-controls="chat-navigation"
          @click="sidebarCollapsed = !sidebarCollapsed"
        >
          <IconLayoutSidebar :size="20" />
        </button>
        <button
          class="chat-icon chat-mobile-toggle"
          aria-label="打开对话列表"
          :aria-expanded="showSessionList"
          aria-controls="chat-navigation"
          @click="showSessionList = true"
        >
          <IconLayoutSidebar :size="20" />
        </button>
        <div
          class="chat-mode-switch"
          data-testid="chat-mode-switch"
          role="group"
          aria-label="对话模式"
        >
          <button
            type="button"
            data-testid="chat-mode-chat"
            :class="{ 'is-active': !isWorkMode }"
            :aria-pressed="!isWorkMode"
            @click="setUiMode('chat')"
          >
            对话
          </button>
          <button
            type="button"
            data-testid="chat-mode-work"
            :class="{ 'is-active': isWorkMode }"
            :aria-pressed="isWorkMode"
            @click="setUiMode('work')"
          >
            工作
          </button>
        </div>
        <div class="chat-header-title">
          <h1 :title="store.currentSession?.title">{{ store.currentSession?.title || 'Mirai Chat' }}</h1>
          <p>
            <span v-if="currentProject">{{ currentProject.name }}<span aria-hidden="true"> · </span></span
            >{{
              store.isTemporary
                ? '临时聊天 · 关闭即丢、不进列表'
                : isCurrentStreaming
                  ? '正在回复…'
                  : isWorkMode
                    ? '工作台 · 工具与文件更醒目'
                    : '给想法一点生长的空间'
            }}
          </p>
        </div>
        <button
          class="chat-files-button"
          :class="{ 'is-emphasized': isWorkMode }"
          aria-label="对话文件"
          :aria-expanded="showArtifacts"
          @click="showArtifacts = true"
        >
          <IconFolder :size="17" /><span>对话文件</span
          ><span v-if="artifacts.length" class="chat-count">{{ artifacts.length }}</span>
        </button>
      </header>
      <div v-if="uiError" class="chat-error" role="alert">
        <span>{{ uiError }}</span
        ><button class="chat-link" :disabled="store.sending || creatingSession" @click="retryFailedAction">{{ uiErrorKind === 'send' ? '重试发送' : uiErrorKind === 'create' ? '重试创建' : '重新加载' }}</button
        ><button class="chat-icon" aria-label="关闭错误提示" @click="uiError = ''">
          <IconX :size="15" />
        </button>
      </div>
      <div v-if="store.recoverableAgentRunId" class="chat-error" role="status">
        <span>任务因服务重启中断，已保留进度。</span>
        <button class="chat-link" @click="store.resumeRecoverableAgentRun">继续执行</button>
      </div>
      <div class="chat-conversation">
        <div ref="messagesContainer" class="chat-messages" data-testid="chat-messages" @scroll="handleScroll">
          <div class="chat-reading-column">
            <div v-if="store.isTemporary" class="chat-temporary-note">
              <IconCircleDashed :size="18" />
              <p>关闭即丢、不进列表。关闭或切换后内容不会保存，也不会出现在左侧对话列表。</p>
            </div>
            <div v-if="store.loading" class="chat-loading" role="status">
              <IconLoader2 :size="20" class="chat-spin" />正在打开对话…
            </div>
            <div v-else-if="displayMessages.length === 0" class="chat-welcome">
              <div class="chat-welcome-mark">
                <img src="/favicon.svg" alt="" width="48" height="48" /><span>Mirai Chat</span>
              </div>
              <h2>{{ store.isTemporary ? '此刻，想聊些什么？' : '把想法，慢慢聊清楚。' }}</h2>
              <p>梳理工作，记录灵感，或只是聊聊今天。<br />我在这里，和你一起往前想一步。</p>
              <div class="chat-starters">
                <button v-for="starter in starters" :key="starter.title" @click="useStarter(starter.prompt)">
                  <component :is="starter.icon" :size="20" :stroke-width="1.5" /><span
                    ><strong>{{ starter.title }}</strong
                    ><small>{{ starter.hint }}</small></span
                  ><IconArrowRight :size="16" />
                </button>
              </div>
              <span class="chat-welcome-footnote">从一句话开始，也很好。</span>
            </div>
            <template v-else>
              <div class="chat-conversation-start">
                <span>{{ store.isTemporary ? '临时对话' : '对话记录' }}</span>
              </div>
              <article
                v-for="msg in displayMessages"
                :key="msg.streaming ? 'stream' : msg.id"
                class="chat-message"
                :class="msg.role === 'user' ? 'is-user' : 'is-assistant'"
              >
                <div class="chat-message-heading">
                  <img
                    v-if="msg.role === 'assistant'"
                    src="/favicon.svg"
                    alt=""
                    width="24"
                    height="24"
                  /><span>{{ msg.role === 'user' ? '你' : 'Mirai' }}</span
                  ><time :datetime="msg.createdAt">{{ fmtMsgTime(msg.createdAt) }}</time>
                </div>
                <div v-if="msg.role === 'user'" class="chat-user-content">{{ msg.content }}</div>
                <div v-else class="chat-assistant-content">
                  <details v-if="msg.thinking" class="chat-thinking">
                    <summary>
                      <span>{{ msg.streaming && !msg.answer ? '正在思考' : '思考过程' }}</span>
                    </summary>
                    <div class="chat-markdown" v-html="safeMarkdown(msg.thinking)" @click="onMessageLinkClick" />
                  </details>
                  <div
                    v-if="toolEventsForMessage(msg).length && isWorkMode"
                    class="chat-tool-events"
                    data-testid="chat-tool-list"
                  >
                    <article
                      v-for="tc in toolEventsForMessage(msg)"
                      :key="tc.id"
                      class="chat-tool-card"
                      :class="'is-' + tc.status"
                      :data-tool-status="tc.status"
                      :data-tool-name="tc.name"
                    >
                      <header class="chat-tool-card-head">
                        <IconLoader2 v-if="tc.status === 'running'" :size="15" class="chat-spin" />
                        <IconCheck v-else-if="tc.status === 'success'" :size="15" />
                        <IconX v-else :size="15" />
                        <div class="chat-tool-card-title">
                          <strong>{{ tc.label }}</strong>
                          <code v-if="tc.name && tc.name !== tc.label" class="chat-tool-card-name">{{
                            tc.name
                          }}</code>
                        </div>
                        <span class="chat-tool-card-status">{{ toolStatusLabel[tc.status] }}</span>
                        <small v-if="tc.elapsedSeconds" class="chat-tool-card-elapsed"
                          >{{ tc.elapsedSeconds }}s</small
                        >
                      </header>
                      <p v-if="tc.inputSummary" class="chat-tool-card-meta">
                        <span class="chat-tool-card-k">参数</span>{{ tc.inputSummary }}
                      </p>
                      <p
                        v-if="tc.status === 'running' && tc.detail"
                        class="chat-tool-card-meta is-progress"
                      >
                        <span class="chat-tool-card-k">进度</span>{{ tc.detail }}
                      </p>
                      <p v-if="tc.resultSummary && tc.status !== 'running'" class="chat-tool-card-meta">
                        <span class="chat-tool-card-k">结果</span>{{ tc.resultSummary }}
                      </p>
                      <details
                        v-if="tc.status === 'failure' && tc.errorDetail"
                        class="chat-tool-card-error"
                      >
                        <summary>失败详情</summary>
                        <pre>{{ tc.errorDetail }}</pre>
                      </details>
                    </article>
                  </div>
                  <details
                    v-else-if="toolEventsForMessage(msg).length && !isWorkMode"
                    class="chat-tool-events is-collapsed"
                    data-testid="chat-tool-list-collapsed"
                  >
                    <summary
                      >工具过程（已折叠）· {{ toolEventsSummary(toolEventsForMessage(msg)) }}</summary
                    >
                    <article
                      v-for="tc in toolEventsForMessage(msg)"
                      :key="tc.id"
                      class="chat-tool-card is-compact"
                      :class="'is-' + tc.status"
                      :data-tool-status="tc.status"
                    >
                      <header class="chat-tool-card-head">
                        <IconLoader2 v-if="tc.status === 'running'" :size="14" class="chat-spin" />
                        <IconCheck v-else-if="tc.status === 'success'" :size="14" />
                        <IconX v-else :size="14" />
                        <div class="chat-tool-card-title">
                          <strong>{{ tc.label }}</strong>
                        </div>
                        <span class="chat-tool-card-status">{{ toolStatusLabel[tc.status] }}</span>
                      </header>
                      <p v-if="tc.inputSummary || tc.resultSummary" class="chat-tool-card-meta">
                        {{ tc.inputSummary || tc.resultSummary }}
                      </p>
                    </article>
                  </details>
                  <div v-if="msg.answer" class="chat-markdown" v-html="safeMarkdown(msg.answer)" @click="onMessageLinkClick" />
                  <div v-if="msg.streaming && !msg.answer" class="chat-generation-status" role="status">
                    <IconLoader2 :size="16" class="chat-spin" /><span>{{
                      store.currentToolCall || (msg.thinking ? '正在组织回答…' : '正在思考，请稍候…')
                    }}</span>
                  </div>
                </div>
                <div v-if="!msg.streaming" class="chat-message-actions">
                  <button
                    :aria-label="copiedId === msg.id ? '已复制消息' : '复制消息'"
                    title="复制消息"
                    @click="copyMessage(msg)"
                  >
                    <IconCheck v-if="copiedId === msg.id" :size="15" /><IconCopy v-else :size="15" /><span>{{
                      copiedId === msg.id ? '已复制' : '复制'
                    }}</span>
                  </button>
                  <template v-if="!store.isTemporary && msg.id > 0">
                    <button
                      v-if="msg.role === 'user'"
                      :disabled="store.sending"
                      @click="editUserMessage(msg)"
                    >
                      <IconPencil :size="15" /><span>编辑</span>
                    </button>
                    <button v-else :disabled="store.sending" @click="retryAssistantMessage(msg)">
                      <IconRefresh :size="15" /><span>重新回答</span>
                    </button>
                    <button :disabled="store.sending" @click="branchFromMessage(msg)">
                      <IconGitBranch :size="15" /><span>从这里分支</span>
                    </button>
                  </template>
                </div>
              </article>
            </template>
          </div>
        </div>
        <button
          v-if="!atBottom && displayMessages.length"
          class="chat-latest"
          data-testid="chat-latest"
          @click="scrollToBottom"
        >
          <IconArrowDown :size="15" />回到最新消息
        </button>
      </div>
      <div class="chat-composer-wrap">
        <div v-if="store.sending && !isCurrentStreaming" class="chat-background-status" role="status">
          另一段对话正在回复，请稍候，或<button
            class="chat-link"
            @click="store.streamSessionId != null && selectSession(store.streamSessionId)"
          >
            返回查看</button
          >。
        </div>
        <div
          class="chat-composer"
          data-testid="chat-composer"
          :class="{ 'is-generating': isCurrentStreaming }"
        >
          <div
            v-if="store.pendingAttachments.length || uploadingFiles.size"
            class="chat-attachments"
            aria-label="待发送附件"
          >
            <div v-for="name in uploadingFiles" :key="name" class="chat-attachment" role="status">
              <IconLoader2 :size="16" class="chat-spin" /><span>{{ name }}</span
              ><small>读取中</small>
            </div>
            <div v-for="(att, idx) in store.pendingAttachments" :key="idx" class="chat-attachment">
              <span class="chat-file-type">{{ getFileIcon(att.fileType) }}</span
              ><span :title="att.fileName">{{ att.fileName }}</span
              ><button
                class="chat-icon"
                :aria-label="`移除附件 ${att.fileName}`"
                @click="removeAttachment(idx)"
              >
                <IconX :size="14" />
              </button>
            </div>
          </div>
          <label for="chat-input" class="sr-only">输入消息</label>
          <textarea
            class="resize-none"
            id="chat-input"
            ref="inputRef"
            v-model="inputContent"
            data-testid="chat-input"
            :placeholder="store.sending ? '可以先写下一个想法…' : '写下你的问题，或附上一份文件…'"
            rows="2"
            :disabled="creatingSession"
            @keydown="handleKeydown"
            @paste="handlePaste"
          />
          <div class="chat-composer-tools">
            <input
              ref="fileInputRef"
              type="file"
              :accept="ACCEPTED_TYPES"
              multiple
              class="hidden"
              @change="handleFileSelect"
            />
            <button
              class="chat-icon"
              aria-label="上传文件"
              title="上传 PDF、Word、Excel 或文本文件"
              :disabled="store.sending || creatingSession"
              @click="triggerFileInput"
            >
              <IconPaperclip :size="19" />
            </button>
            <button
              class="chat-icon"
              aria-label="从工作区选择文件"
              title="从工作区选择文件"
              :disabled="store.sending || creatingSession"
              @click="showWorkspaceBrowser = true"
            >
              <IconFolder :size="19" />
            </button>
            <span class="chat-composer-label">{{
              store.isTemporary ? '临时聊天' : currentProject ? currentProject.name : 'Mirai 助手'
            }}</span>
            <button
              class="chat-icon chat-expand-input"
              :aria-label="inputExpanded ? '收起输入框' : '展开输入框'"
              :title="inputExpanded ? '收起输入框' : '展开输入框'"
              :aria-pressed="inputExpanded"
              @click="inputExpanded = !inputExpanded"
            >
              <IconMinimize v-if="inputExpanded" :size="17" /><IconMaximize v-else :size="17" />
            </button>
            <button
              v-if="store.sending"
              class="chat-send is-stop"
              aria-label="停止生成"
              title="停止生成"
              @click="store.stopGeneration"
            >
              <IconSquare :size="16" /><span>停止</span>
            </button>
            <button
              v-else
              class="chat-send"
              data-testid="chat-send"
              aria-label="发送消息"
              title="发送消息"
              :disabled="
                (!inputContent.trim() && !store.pendingAttachments.length) ||
                creatingSession ||
                uploadingFiles.size > 0
              "
              @click="send"
            >
              <IconLoader2 v-if="creatingSession" :size="19" class="chat-spin" /><IconArrowUp
                v-else
                :size="20"
              />
            </button>
          </div>
        </div>
        <div class="chat-composer-help">
          <span>支持 PDF、Word、Excel 和文本</span
          ><span
            v-if="store.contextUsage"
            class="chat-context"
            :title="`上下文已使用 ${store.contextUsage.percentUsed}%，共 ${store.contextUsage.messageCount} 条消息`"
            >上下文 {{ store.contextUsage.percentUsed }}%</span
          ><span class="chat-keyboard-help"><kbd>Enter</kbd> 发送 · <kbd>Shift + Enter</kbd> 换行</span>
        </div>
      </div>
      <p class="sr-only" role="status" aria-live="polite">
        {{ isCurrentStreaming ? 'Mirai 正在回复' : '可以发送消息' }}
      </p>
    </section>

    <AppDialog
      :open="showWorkspaceBrowser"
      title="工作区文件"
      size="drawer"
      @close="showWorkspaceBrowser = false"
      ><WorkspaceBrowser
        v-if="showWorkspaceBrowser"
        @attach="onWorkspaceAttach"
        @close="showWorkspaceBrowser = false"
    /></AppDialog>
    <AppDialog
      :open="showArtifacts"
      title="对话文件"
      description="这段对话中生成的文件，集中放在这里。"
      size="drawer"
      @close="showArtifacts = false"
    >
      <div class="chat-artifacts">
        <div v-if="!artifacts.length" class="chat-panel-empty">
          <IconFolder :size="36" :stroke-width="1.3" /><strong>文件会在这里相遇</strong>
          <p>请 Mirai 生成一份文档或表格后，<br />就可以在这里预览和下载。</p>
        </div>
        <article v-for="artifact in artifacts" :key="artifact.url" class="chat-artifact">
          <div>
            <span class="chat-file-type">{{
              artifact.extension.replace('.', '').toUpperCase() || 'FILE'
            }}</span>
            <p :title="artifact.name">
              {{ artifact.name }}<small>{{ fmtMsgTime(artifact.createdAt) }}</small>
            </p>
            <button
              type="button"
              class="chat-btn chat-artifact-copy"
              :aria-label="copiedArtifactUrl === artifact.url ? `已复制 ${artifact.name} 路径` : `复制 ${artifact.name} 路径`"
              :title="copiedArtifactUrl === artifact.url ? '已复制路径' : '复制路径'"
              @click="copyArtifactPath(artifact.url)"
            >
              <IconCheck v-if="copiedArtifactUrl === artifact.url" :size="15" />
              <IconCopy v-else :size="15" />
              <span>{{ copiedArtifactUrl === artifact.url ? '已复制' : '复制路径' }}</span>
            </button>
            <a
              :href="staticUrl(artifact.url)"
              download
              :aria-label="`下载 ${artifact.name}`"
              class="chat-icon"
              @click.prevent="downloadExportFile(artifact.url, artifact.name)"
              ><IconDownload :size="18"
            /></a>
          </div>
          <code class="chat-artifact-path" :title="artifactPath(artifact.url)">{{
            artifactPath(artifact.url)
          }}</code>
          <template v-if="isExportPreviewable(artifact.extension)">
            <p
              v-if="!artifactPreviewOf(artifact.url) || artifactPreviewOf(artifact.url)?.status === 'loading'"
              class="chat-artifact-note"
            >
              预览加载中…
            </p>
            <p
              v-else-if="artifactPreviewOf(artifact.url)?.status === 'error'"
              class="chat-artifact-note"
            >
              预览失败，仍可下载
            </p>
            <template v-else-if="artifactPreviewOf(artifact.url)?.status === 'ready'">
              <iframe
                v-if="artifactPreviewKind(artifact.url) === 'pdf' && artifactPreviewObjectUrl(artifact.url)"
                :src="artifactPreviewObjectUrl(artifact.url)"
                title="PDF 预览"
              />
              <div
                v-else-if="isMarkdownExport(artifact.extension) && artifactPreviewText(artifact.url)"
                class="chat-artifact-preview chat-markdown"
                v-html="safeMarkdown(artifactPreviewText(artifact.url))"
              />
              <pre
                v-else-if="artifactPreviewText(artifact.url)"
                class="chat-artifact-preview"
              >{{ artifactPreviewText(artifact.url) }}</pre>
              <p v-else class="chat-artifact-note">此格式暂不支持预览，请下载后查看。</p>
            </template>
          </template>
        </article>
      </div>
    </AppDialog>
    <AppDialog
      :open="showProjectManager"
      title="项目空间"
      description="把相关对话放在一起，让每一次交流都有共同的背景。"
      size="lg"
      :busy="actionBusy"
      @close="showProjectManager = false"
    >
      <div class="chat-project-manager">
        <nav aria-label="项目列表">
          <button class="chat-btn" :disabled="actionBusy" @click="resetProjectForm">
            <IconPlus :size="16" />新建项目</button
          ><button
            v-for="project in store.projects"
            :key="project.id"
            :disabled="actionBusy"
            :class="{ 'is-selected': editingProjectId === project.id }"
            @click="editProject(project)"
          >
            <span>{{ project.icon }}</span
            ><span>{{ project.name }}</span
            ><small>{{ project.sessionCount }}</small>
          </button>
          <p v-if="!store.projects.length" class="chat-subtle">还没有项目，试着建一个吧。</p>
        </nav>
        <form class="chat-project-form" novalidate @submit.prevent="saveProject">
          <h3>{{ editingProjectId ? '编辑项目' : '新建项目' }}</h3>
          <fieldset :disabled="actionBusy">
            <div class="chat-project-fields">
              <label>图标<input v-model="projectForm.icon" aria-label="项目图标" maxlength="10" /></label
              ><label
                >项目名称<input
                  v-model="projectForm.name"
                  aria-label="项目名称"
                  maxlength="100"
                  placeholder="例如：产品设计"
                  :aria-invalid="!!projectError"
                  aria-describedby="project-error"
              /></label>
            </div>
            <label>项目颜色<input v-model="projectForm.color" type="color" aria-label="项目颜色" /></label
            ><label
              >项目专属指令<textarea
                class="resize-none"
                v-model="projectForm.instructions"
                aria-label="项目专属指令"
                maxlength="4000"
                placeholder="例如：回答优先使用中文；讨论方案时先给结论，再列出下一步。"
              />
            </label>
            <p class="chat-subtle">专属指令会加入该项目下普通聊天的上下文。</p>
          </fieldset>
          <p id="project-error" class="chat-field-error" role="alert">{{ projectError }}</p>
          <div class="chat-form-actions">
            <button
              v-if="editingProjectId"
              type="button"
              class="chat-btn chat-danger-text"
              :disabled="actionBusy"
              @click="removeEditingProject"
            >
              删除项目</button
            ><button class="chat-btn chat-primary" :disabled="actionBusy">
              {{ actionBusy ? '保存中…' : '保存项目' }}
            </button>
          </div>
        </form>
      </div>
    </AppDialog>
    <AppDialog
      :open="showArchiveManager"
      title="归档管理"
      description="暂时收起的对话，需要时可以继续。"
      @close="showArchiveManager = false"
      ><div class="chat-archive-list">
        <div v-if="store.archivedLoading" class="chat-panel-empty" role="status">
          <IconLoader2 :size="24" class="chat-spin" />正在加载…
        </div>
        <div v-else-if="!store.archivedSessions.length" class="chat-panel-empty">
          <IconArchive :size="32" /><strong>还没有归档对话</strong>
          <p>归档的对话会在这里保留。</p>
        </div>
        <div v-for="s in store.archivedSessions" :key="s.id">
          <p :title="s.title">
            {{ s.title }}<small>{{ fmtSessionDate(s.updatedAt) }}</small>
          </p>
          <button class="chat-btn" :disabled="restoringId === s.id" @click="restoreSession(s.id)">
            {{ restoringId === s.id ? '还原中…' : '还原' }}
          </button>
        </div>
      </div></AppDialog
    >
    <AppDialog
      :open="!!store.pendingConfirm"
      :title="store.pendingConfirm?.riskLevel === 'dangerous' ? '确认执行此操作？' : '确认保存更改？'"
      description="请查看本次操作内容，确认后继续。"
      @close="store.confirmToolCall(false)"
      ><template v-if="store.pendingConfirm"
        ><p class="chat-subtle">{{ store.pendingConfirm.toolName }}</p>
        <pre class="chat-confirm-details">{{ store.pendingConfirm.arguments }}</pre></template
      ><template #footer
        ><button class="chat-btn" data-dialog-autofocus @click="store.confirmToolCall(false)">取消</button
        ><button class="chat-btn chat-primary" @click="store.confirmToolCall(true)">
          确认执行
        </button></template
      ></AppDialog
    >
    <AppDialog
      :open="!!dialog"
      :title="dialog?.title || ''"
      :description="dialog?.description"
      :busy="actionBusy"
      @close="dialog = null"
      ><div v-if="dialog?.kind === 'rename' || dialog?.kind === 'edit'" class="chat-dialog-field">
        <label :for="dialog.kind === 'edit' ? 'chat-edit-message' : 'chat-rename'">{{
          dialog.kind === 'edit' ? '消息内容' : '对话名称'
        }}</label
        ><textarea
          class="resize-none"
          v-if="dialog.kind === 'edit'"
          id="chat-edit-message"
          v-model="dialogText"
          data-dialog-autofocus
          :disabled="actionBusy"
          :aria-invalid="!!dialogError"
          aria-describedby="chat-dialog-error"
        /><input
          v-else
          id="chat-rename"
          v-model="dialogText"
          maxlength="200"
          data-dialog-autofocus
          :disabled="actionBusy"
          :aria-invalid="!!dialogError"
          aria-describedby="chat-dialog-error"
          @keydown.enter="!$event.isComposing && submitDialog()"
        />
      </div>
      <p id="chat-dialog-error" class="chat-field-error" role="alert">{{ dialogError }}</p>
      <template #footer
        ><button class="chat-btn" :disabled="actionBusy" data-dialog-autofocus @click="dialog = null">
          取消</button
        ><button
          class="chat-btn"
          :class="
            dialog?.kind === 'delete' || dialog?.kind === 'project-delete' ? 'chat-danger' : 'chat-primary'
          "
          :disabled="actionBusy"
          @click="submitDialog"
        >
          {{
            actionBusy
              ? '处理中…'
              : dialog?.kind === 'delete' || dialog?.kind === 'project-delete'
                ? '删除'
                : dialog?.kind === 'archive'
                  ? '归档'
                  : dialog?.kind === 'edit'
                    ? '创建分支并发送'
                    : '保存名称'
          }}
        </button></template
      ></AppDialog
    >
  </div>
</template>

<style scoped src="./chat.css"></style>
