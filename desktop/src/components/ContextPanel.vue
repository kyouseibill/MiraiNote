<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { memoryApi, workLogApi } from '@/api/data'
import { chatApi, type ChatMessage } from '@/api/chat'
import AiBadge from '@/components/AiBadge.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import MarkdownView from '@/components/MarkdownView.vue'
import SourceChips from '@/components/SourceChips.vue'
import { useUiStore } from '@/stores/ui'

// 侧边对话（ContextPanel）· 视觉基准 docs/m1-ui-mockups.html ⑤
// sessionType='context' 会话（自动携带对象全文 + 标签，服务端注入）+ messages/stream；
// 动作：【转为记录】（生成新工作记录）【存入记忆】（写入 AgentMemory）。
const ui = useUiStore()

const TYPE_LABEL: Record<string, string> = {
  worklog: '工作记录',
  lifelog: '生活记录',
  memo: '任务',
  inbox: '收件箱条目',
  briefing: '晨报',
}

const sessionId = ref<number | null>(null)
const messages = ref<ChatMessage[]>([])
const input = ref('')
const sending = ref(false)
const streamText = ref('')
const errorMsg = ref('')
const toast = ref('')
let abortController: AbortController | null = null
let toastTimer: ReturnType<typeof setTimeout> | null = null

const lastAssistant = computed(() => [...messages.value].reverse().find((m) => m.role === 'assistant'))
const canConvert = computed(() => !!lastAssistant.value)

// 挂载对象变化 → 重置并懒建 context 会话
watch(
  () => ui.contextSeq,
  () => {
    abortController?.abort()
    sessionId.value = null
    messages.value = []
    streamText.value = ''
    input.value = ''
    errorMsg.value = ''
    void ensureSession()
  },
  { immediate: true },
)

async function ensureSession() {
  if (sessionId.value != null || !ui.contextTarget) return
  const target = ui.contextTarget
  try {
    const session = await chatApi.createSession({
      sessionType: 'context',
      attachToType: target.type,
      attachToObjectId: target.id,
      title: `讨论：${target.title}`,
    })
    // 面板已切到其他对象时丢弃过期会话
    if (ui.contextTarget?.id !== target.id || ui.contextTarget?.type !== target.type) return
    sessionId.value = session.id
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '创建会话失败'
  }
}

async function send() {
  const q = input.value.trim()
  if (!q || sending.value || sessionId.value == null) return
  input.value = ''
  errorMsg.value = ''
  messages.value.push({ id: -Date.now(), role: 'user', content: q, createdAt: new Date().toISOString() })
  sending.value = true
  streamText.value = ''
  abortController = new AbortController()
  try {
    await chatApi.sendMessageStream(
      sessionId.value,
      q,
      (event) => {
        if (event.type === 'token') streamText.value += String(event.data.content ?? '')
        else if (event.type === 'done') {
          messages.value.push({
            id: Number(event.data.messageId ?? -Date.now() - 1),
            role: 'assistant',
            content: String(event.data.content ?? streamText.value),
            createdAt: new Date().toISOString(),
          })
          streamText.value = ''
        } else if (event.type === 'error') {
          errorMsg.value = String(event.data.message ?? '对话出错，请重试')
        }
      },
      abortController.signal,
    )
  } catch (e) {
    if ((e as Error)?.name !== 'AbortError') errorMsg.value = e instanceof Error ? e.message : '连接中断'
  } finally {
    sending.value = false
    abortController = null
  }
}

// ---- 动作：转为记录（二次确认 → 创建工作记录） ----
const convertConfirm = ref(false)
const converting = ref(false)
async function convertToWorklog() {
  const m = lastAssistant.value
  if (!m || converting.value) return
  converting.value = true
  try {
    const target = ui.contextTarget
    const created = await workLogApi.save({
      title: `讨论结论：${target?.title?.slice(0, 20) ?? '侧边对话'}`,
      content: m.content,
      tags: 'AI讨论',
      logDate: new Date().toISOString().slice(0, 10),
    })
    convertConfirm.value = false
    showToast(`已转为工作记录 #${created.id}「${created.title}」`)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '转为记录失败'
  } finally {
    converting.value = false
  }
}

// ---- 动作：存入记忆（AgentMemory；PRD §6 写操作二次确认） ----
const storing = ref(false)
const storeConfirm = ref(false)
async function saveToMemory() {
  const m = lastAssistant.value
  if (!m || storing.value) return
  storing.value = true
  try {
    const target = ui.contextTarget
    await memoryApi.create({
      key: `context/${target?.type ?? 'note'}/${target?.id ?? Date.now()}`,
      value: m.content,
      category: 'context',
      source: 'desktop-context-panel',
    })
    storeConfirm.value = false
    showToast('已存入记忆（后续对话可自动引用）')
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '存入记忆失败'
  } finally {
    storing.value = false
  }
}

function showToast(text: string) {
  toast.value = text
  if (toastTimer) clearTimeout(toastTimer)
  toastTimer = setTimeout(() => (toast.value = ''), 3500)
}

onUnmounted(() => {
  abortController?.abort()
  if (toastTimer) clearTimeout(toastTimer)
})
</script>

<template>
  <aside
    v-if="ui.contextTarget"
    class="flex w-[300px] shrink-0 flex-col border-l border-ink-faint/20 bg-paper-card"
  >
    <header class="flex items-center gap-2 border-b border-ink-faint/20 px-4 py-3">
      <b class="text-sm">💬 讨论</b>
      <span class="truncate text-[11px] text-ink-faint">{{ ui.contextTarget.title }}</span>
      <button class="ml-auto text-xs text-ink-faint hover:text-brand" title="收起（→）" @click="ui.closeContext()">✕</button>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto px-4 py-3">
      <!-- 自动附带上下文提示（服务端 context 会话注入对象快照） -->
      <div class="mb-3 rounded-lg border border-dashed border-ink-faint/30 bg-paper p-2.5 text-[11.5px] leading-5 text-ink-sub">
        📎 已自动附带上下文：{{ TYPE_LABEL[ui.contextTarget.type] }} #{{ ui.contextTarget.id }}「{{ ui.contextTarget.title }}」全文 + 标签
      </div>

      <!-- 消息区 -->
      <template v-for="(m, i) in messages" :key="`${m.id}-${i}`">
        <div v-if="m.role === 'user'" class="mb-2.5 ml-auto w-fit max-w-[88%] rounded-xl rounded-br-sm bg-brand px-3 py-2 text-[12.5px] leading-[1.7] text-white">
          {{ m.content }}
        </div>
        <div v-else class="mb-2.5 max-w-[92%]">
          <div class="mb-0.5 flex items-center gap-1"><AiBadge /></div>
          <MarkdownView :source="m.content" class="rounded-xl rounded-bl-sm bg-paper px-3 py-2 text-[12.5px] leading-[1.7]" />
        </div>
      </template>

      <!-- 流式回复 -->
      <div v-if="streamText" class="mb-2.5 max-w-[92%]">
        <div class="mb-0.5 flex items-center gap-1"><AiBadge /></div>
        <div class="rounded-xl rounded-bl-sm bg-paper px-3 py-2">
          <MarkdownView :source="streamText" />
          <span class="ml-0.5 inline-block h-3 w-[6px] animate-pulse bg-brand align-[-2px]" />
        </div>
      </div>
      <div v-if="sending && !streamText" class="mb-2.5 flex items-center gap-2 text-[11px] text-ink-faint">
        <span class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-brand-line border-t-brand" />
        思考中…
      </div>

      <!-- 引用示例（mock 场景：回答中的历史以 chips 引用） -->
      <div v-if="lastAssistant && /参考/.test(lastAssistant.content)" class="mb-2.5">
        <SourceChips :sources="[{ type: 'chat', id: 13, title: '聊天 8/13' }]" />
      </div>

      <div v-if="errorMsg" class="rounded-lg border border-warn-line bg-warn-soft p-2.5 text-[11px] leading-5 text-warn">
        ⚠ {{ errorMsg }}
      </div>
      <div
        v-if="!messages.length && !sending"
        class="rounded-lg border border-dashed border-ink-faint/30 p-4 text-center text-[11px] leading-6 text-ink-faint"
      >
        针对这条记录追问，回答会自动带上下文。<br />有价值的结论可一键【转为记录】或【存入记忆】。
      </div>
    </div>

    <!-- 动作 + 输入 -->
    <div class="border-t border-ink-faint/20 px-4 py-3">
      <div class="mb-2.5 flex gap-2">
        <button
          class="rounded-lg border border-ink-faint/30 bg-white px-2.5 py-1 text-[11px] hover:border-brand hover:text-brand disabled:opacity-40"
          :disabled="!canConvert || converting"
          :title="canConvert ? '把上一条 AI 回答生成为一条新工作记录' : '有 AI 回答后可用'"
          @click="convertConfirm = true"
        >
          ↗ 转为记录
        </button>
        <button
          class="rounded-lg border border-ink-faint/30 bg-white px-2.5 py-1 text-[11px] hover:border-brand hover:text-brand disabled:opacity-40"
          :disabled="!canConvert || storing"
          :title="canConvert ? '写入 AgentMemory，助理长期记住' : '有 AI 回答后可用'"
          @click="storeConfirm = true"
        >
          {{ storing ? '保存中…' : '◈ 存入记忆' }}
        </button>
      </div>
      <div class="flex items-center gap-2 rounded-lg border border-ink-faint/25 px-2.5 py-2">
        <input
          v-model="input"
          class="min-w-0 flex-1 bg-transparent text-[12.5px] outline-none placeholder:text-ink-faint"
          placeholder="针对这条记录追问…"
          :disabled="sending"
          @keydown.enter.prevent="send"
        />
        <kbd class="rounded border border-ink-faint/30 bg-paper px-1 text-[10px] text-ink-faint">Enter</kbd>
      </div>
      <div v-if="toast" class="mt-2 rounded-lg border border-brand-line bg-brand-soft px-2.5 py-1.5 text-[11px] text-brand">
        ✓ {{ toast }}
      </div>
    </div>
  </aside>

  <!-- 转为记录：二次确认（PRD §6 写操作确认） -->
  <ConfirmDialog
    :open="convertConfirm"
    title="转为工作记录？"
    confirm-text="创建记录"
    :busy="converting"
    @confirm="convertToWorklog"
    @cancel="convertConfirm = false"
  >
    将把最近一条 AI 回答生成为新工作记录（标题自动取「讨论结论：…」，创建后可在工作流页面编辑）。
  </ConfirmDialog>

  <!-- 存入记忆：二次确认（PRD §6 写操作确认） -->
  <ConfirmDialog
    :open="storeConfirm"
    title="存入记忆？"
    confirm-text="写入记忆"
    :busy="storing"
    @confirm="saveToMemory"
    @cancel="storeConfirm = false"
  >
    将把最近一条 AI 回答写入 AgentMemory（助理长期记住，后续对话自动引用；可在记忆库管理，前台化随 M2 上线）。
  </ConfirmDialog>
</template>
