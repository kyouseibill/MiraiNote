<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { agentApi, type AgentConfirmData, type RetrievedRecord } from '@/api/agent'
import { chatApi, type ChatMessage } from '@/api/chat'
import AiBadge from '@/components/AiBadge.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import MarkdownView from '@/components/MarkdownView.vue'
import SourceChips from '@/components/SourceChips.vue'
import { useUiStore } from '@/stores/ui'

// 指令面板 · 视觉基准 docs/m1-ui-mockups.html ③
// sessionType='command' 会话 → agent SSE 流（token/tool_call/tool_result/confirm/done）
// mock 模式：本地模拟事件流（agent.ts runMockAgentStream），含 Confirm 拦截演示。
const ui = useUiStore()
const route = useRoute()

const input = ref('')
const inputEl = ref<HTMLInputElement | null>(null)

const sessionId = ref<number | null>(null)
const messages = ref<ChatMessage[]>([])
const streaming = ref(false)
const streamText = ref('')
const errorMsg = ref('')

interface ToolChip {
  id: string
  name: string
  label: string
  status: 'run' | 'ok'
  detail: string
}
const tools = ref<ToolChip[]>([])
const sources = ref<RetrievedRecord[]>([])
const sourcesOpen = ref(true)
const pendingConfirm = ref<AgentConfirmData | null>(null)
const confirming = ref(false)

let abortController: AbortController | null = null

const TOOL_META: Record<string, { icon: string; label: string }> = {
  search_work_logs: { icon: '🔍', label: '查询工作记录' },
  search_life_logs: { icon: '🔍', label: '查询生活记录' },
  search_memos: { icon: '🔍', label: '查询备忘' },
  get_record_overview: { icon: '📄', label: '记录概览' },
  get_weekly_reports: { icon: '¶', label: '获取周报' },
  export_file: { icon: '📄', label: '导出文件' },
  create_work_log: { icon: '✎', label: '创建工作记录' },
  update_work_log: { icon: '✎', label: '更新工作记录' },
  delete_work_logs: { icon: '🗑', label: '删除工作记录' },
  delete_work_log: { icon: '🗑', label: '删除工作记录' },
  search_internet: { icon: '🌐', label: '搜索互联网' },
  fetch_web_page: { icon: '🌐', label: '分析网页' },
  call_http_api: { icon: '🔌', label: '调用 API' },
}
const toolMeta = (name: string) => TOOL_META[name] ?? { icon: '🔧', label: name }

function onKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    ui.togglePalette()
  } else if (e.key === 'Escape' && ui.paletteOpen) {
    e.preventDefault()
    closePalette()
  }
}

function closePalette() {
  abortController?.abort()
  abortController = null
  pendingConfirm.value = null
  ui.closePalette()
}

async function onOpen() {
  void nextTick(() => inputEl.value?.focus())
}

/** 面板打开时聚焦；来源 chip 触发路由跳转时自动收起 */
watch(
  () => ui.paletteOpen,
  (open) => {
    if (open) onOpen()
  },
)
watch(
  () => route.fullPath,
  () => {
    if (ui.paletteOpen) closePalette()
  },
)

async function ask() {
  const q = input.value.trim()
  if (!q || streaming.value) return
  input.value = ''
  errorMsg.value = ''
  messages.value.push({ id: -Date.now(), role: 'user', content: q, createdAt: new Date().toISOString() })

  if (sessionId.value == null) {
    try {
      const session = await chatApi.createSession({ sessionType: 'command', title: q.slice(0, 20) })
      sessionId.value = session.id
    } catch (e) {
      errorMsg.value = e instanceof Error ? e.message : '创建会话失败'
      return
    }
  }

  streaming.value = true
  streamText.value = ''
  tools.value = []
  sources.value = []
  abortController = new AbortController()

  try {
    await agentApi.sendAgentMessageStream(
      sessionId.value,
      { content: q },
      (event) => handleEvent(event.type, event.data as Record<string, unknown>),
      abortController.signal,
    )
  } catch (e) {
    if ((e as Error)?.name !== 'AbortError') {
      errorMsg.value = e instanceof Error ? e.message : '连接中断，请重试'
    }
  } finally {
    streaming.value = false
    abortController = null
    pendingConfirm.value = null
  }
}

function handleEvent(type: string, data: Record<string, unknown>) {
  switch (type) {
    case 'user_msg':
      break
    case 'token':
      streamText.value += String(data.content ?? '')
      break
    case 'tool_call': {
      const meta = toolMeta(String(data.name ?? ''))
      tools.value.push({
        id: String(data.id ?? data.name ?? tools.value.length),
        name: String(data.name ?? ''),
        label: meta.label,
        status: 'run',
        detail: '执行中…',
      })
      break
    }
    case 'tool_progress': {
      const chip = tools.value.find((t) => t.id === String(data.id ?? '') || t.name === String(data.name ?? ''))
      if (chip) chip.detail = String(data.message ?? '执行中…')
      break
    }
    case 'tool_result': {
      const chip = tools.value.find((t) => t.id === String(data.toolCallId ?? '') || t.name === String(data.name ?? ''))
      if (chip) {
        chip.status = 'ok'
        chip.detail = String(data.result ?? '完成')
      }
      // 收集被检索记录 → 「数据来源」折叠区
      const records = data.records
      if (Array.isArray(records)) {
        for (const r of records as RetrievedRecord[]) {
          if (r && typeof r.id === 'number' && !sources.value.some((s) => s.type === r.type && s.id === r.id)) {
            sources.value.push(r)
          }
        }
      }
      break
    }
    case 'confirm':
      pendingConfirm.value = {
        toolName: String(data.toolName ?? ''),
        riskLevel: String(data.riskLevel ?? 'medium'),
        arguments: String(data.arguments ?? ''),
      }
      break
    case 'done': {
      const final = String(data.content ?? streamText.value)
      messages.value.push({
        id: Number(data.messageId ?? -Date.now() - 1),
        role: 'assistant',
        content: final,
        createdAt: String(data.createdAt ?? new Date().toISOString()),
      })
      streamText.value = ''
      if (sources.value.length) sourcesOpen.value = true
      break
    }
    case 'error':
      errorMsg.value = String(data.message ?? 'AI 服务暂不可用')
      break
    default:
      // heartbeat / plan / reflection / context 等事件 M1 桌面端不渲染
      break
  }
}

async function decideConfirm(confirmed: boolean) {
  if (!pendingConfirm.value || confirming.value) return
  confirming.value = true
  try {
    await agentApi.confirmToolCall(sessionId.value!, confirmed)
    pendingConfirm.value = null
  } finally {
    confirming.value = false
  }
}

function newConversation() {
  abortController?.abort()
  sessionId.value = null
  messages.value = []
  streamText.value = ''
  tools.value = []
  sources.value = []
  errorMsg.value = ''
  pendingConfirm.value = null
  void nextTick(() => inputEl.value?.focus())
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition duration-150"
      enter-from-class="opacity-0"
      leave-active-class="transition duration-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="ui.paletteOpen"
        class="fixed inset-0 z-[60] flex items-start justify-center bg-gradient-to-b from-[#2b3a42]/80 to-[#1f2937]/80 pt-[10vh] backdrop-blur-[2px]"
        @click.self="closePalette"
      >
        <div class="flex max-h-[76vh] w-[680px] flex-col overflow-hidden rounded-2xl bg-white shadow-2xl">
          <!-- 输入行 -->
          <div class="flex items-center gap-3 border-b border-ink-faint/15 px-5 py-3.5">
            <span class="text-brand">◈</span>
            <input
              ref="inputEl"
              v-model="input"
              class="min-w-0 flex-1 text-sm outline-none placeholder:text-ink-faint"
              placeholder="说点什么：查询 / 创建 / 整理导出 / 闲聊…（输入「删除」可体验危险操作确认）"
              :disabled="streaming"
              @keydown.enter.prevent="ask"
              @keydown.esc.prevent="closePalette"
            />
            <span v-if="streaming" class="h-4 w-4 animate-spin rounded-full border-2 border-brand-line border-t-brand" />
            <kbd v-else class="rounded border border-ink-faint/30 bg-paper px-1.5 text-[10px] text-ink-faint">Esc</kbd>
          </div>

          <!-- 会话区 -->
          <div class="min-h-[180px] flex-1 overflow-y-auto px-5 py-4">
            <!-- 空态引导 -->
            <div
              v-if="!messages.length && !streamText && !streaming"
              class="flex h-full flex-col items-center justify-center gap-2 py-8 text-center"
            >
              <div class="text-2xl text-brand">◈</div>
              <div class="text-xs text-ink-sub">随时说点什么 —— 查询记录、创建任务、整理导出一页、或只是闲聊</div>
              <div class="mt-2 flex flex-wrap justify-center gap-2">
                <button
                  v-for="tip in ['把上周关于迁移的记录整理成一页', '今天有哪些到期任务？', '删除上周的测试记录']"
                  :key="tip"
                  class="rounded-full border border-ink-faint/25 px-3 py-1 text-[11px] text-ink-sub hover:border-brand hover:text-brand"
                  @click="input = tip"
                >
                  {{ tip }}
                </button>
              </div>
            </div>

            <!-- 消息列表 -->
            <template v-for="(m, i) in messages" :key="`${m.id}-${i}`">
              <div v-if="m.role === 'user'" class="mb-3 ml-auto w-fit max-w-[85%] rounded-xl rounded-br-sm bg-brand px-3.5 py-2 text-[13px] leading-6 text-white">
                {{ m.content }}
              </div>
              <div v-else class="mb-3 max-w-[92%]">
                <div class="mb-1 flex items-center gap-1 text-[10px] text-ai"><span class="rounded border border-ai-line bg-ai-soft px-1">AI</span></div>
                <MarkdownView :source="m.content" class="rounded-xl rounded-bl-sm bg-paper px-3.5 py-2.5" />
              </div>
            </template>

            <!-- 流式回复区 -->
            <div v-if="streaming || streamText" class="max-w-[92%]">
              <div class="mb-1 flex items-center gap-1 text-[10px] text-ai"><span class="rounded border border-ai-line bg-ai-soft px-1">AI</span></div>
              <div class="rounded-xl rounded-bl-sm bg-paper px-3.5 py-2.5">
                <MarkdownView :source="streamText" />
                <span v-if="streaming" class="ml-0.5 inline-block h-3.5 w-[7px] animate-pulse bg-brand align-[-2px]" />
              </div>
            </div>

            <!-- 工具轨迹 chips（完成 ✓ / 进行中转圈） -->
            <div v-if="tools.length" class="mb-2 mt-1">
              <span
                v-for="t in tools"
                :key="t.id"
                class="mr-1.5 mb-1.5 inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px]"
                :class="t.status === 'ok'
                  ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                  : 'border-blue-200 bg-blue-50 text-blue-700'"
                :title="`${t.name}: ${t.detail}`"
              >
                <span>{{ toolMeta(t.name).icon }}</span>
                <span>{{ t.label }}</span>
                <span v-if="t.status === 'run'" class="h-2.5 w-2.5 animate-spin rounded-full border-2 border-blue-200 border-t-blue-600" />
                <span v-else>✓ {{ t.detail }}</span>
              </span>
            </div>

            <!-- 数据来源折叠区（M1 溯源形态；M2 升级为正文内联引用） -->
            <div v-if="sources.length" class="mt-3 border-t border-dashed border-ink-faint/20 pt-2">
              <button class="text-[11px] text-ink-faint hover:text-brand" @click="sourcesOpen = !sourcesOpen">
                {{ sourcesOpen ? '▾' : '▸' }} 数据来源（{{ sources.length }}）：
              </button>
              <div v-if="sourcesOpen" class="mt-1">
                <SourceChips :sources="sources" />
              </div>
            </div>

            <!-- 错误态（DeepSeek 不可用 / 断网：友好提示） -->
            <div v-if="errorMsg" class="mt-2 rounded-lg border border-warn-line bg-warn-soft p-3 text-xs leading-6 text-warn">
              ⚠ {{ errorMsg }}
            </div>
          </div>

          <!-- 底栏 -->
          <div class="flex items-center gap-3 border-t border-ink-faint/15 bg-paper/60 px-5 py-2 text-[11px] text-ink-faint">
            <button class="hover:text-brand" @click="newConversation">＋ 新话题</button>
            <span class="ml-auto">Ctrl+K 开合 · Esc 即走</span>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>

  <!-- Confirm 拦截弹窗（涉及删除/shell 等高危工具先确认，复用现有 Confirm 机制） -->
  <ConfirmDialog
    :open="!!pendingConfirm"
    :title="`确认执行高危操作：${pendingConfirm?.toolName ?? ''}`"
    confirm-text="确认执行"
    danger
    :busy="confirming"
    @confirm="decideConfirm(true)"
    @cancel="decideConfirm(false)"
  >
    <div class="space-y-2">
      <div>
        风险等级：
        <span class="rounded border border-red-200 bg-red-50 px-1.5 py-0.5 font-semibold text-red-600">
          {{ pendingConfirm?.riskLevel }}
        </span>
      </div>
      <div>该操作由 AI 发起，执行后可能不可恢复，请核对参数：</div>
      <pre class="max-h-40 overflow-auto rounded-lg bg-paper p-2.5 text-[11px] leading-5 text-ink-sub">{{ pendingConfirm?.arguments }}</pre>
    </div>
  </ConfirmDialog>
</template>
