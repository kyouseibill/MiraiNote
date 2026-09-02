<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { miraiApi } from '@/api/mirai'
import { InboxSource, INBOX_STATUS_TEXT, type CreatedRef, type DispatchResult, type InboxItem } from '@/api/types'
import AiBadge from '@/components/AiBadge.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import TriageSuggestionCard from '@/components/TriageSuggestionCard.vue'
import { fmtDateTime } from '@/utils/format'

// 收件箱 · AI 分拣 · 视觉基准 docs/m1-ui-mockups.html ②
// 完整交互：字段级建议卡 / 勾选与全选 / overrides 行内编辑 / 纠错重分拣 / 分发 + 30s 撤销 / 丢弃
const route = useRoute()
const router = useRouter()

const items = ref<InboxItem[]>([])
const selectedId = ref<number | null>(null)
const loading = ref(true)
const error = ref('')

const checked = ref<Set<string>>(new Set())
/** suggestionId → 行内编辑产生的 overrides（dispatch 时随请求发送，契约 §2.4） */
const overridesMap = ref<Record<string, Record<string, unknown>>>({})

const dispatching = ref(false)
const retriaging = ref(false)
const retriageOpen = ref(false)
const retriageText = ref('')
const discardConfirm = ref(false)
const discarding = ref(false)
const opError = ref('')

/** 分发结果 + 30s 撤销入口（契约 §2.6：UI 展示 30 秒快捷入口，服务端不强制时间窗） */
const undoState = ref<{ inboxId: number; result: DispatchResult; left: number } | null>(null)
let undoTimer: ReturnType<typeof setInterval> | null = null

const selected = computed(() => items.value.find((it) => it.id === selectedId.value) ?? null)
const suggestions = computed(() => selected.value?.aiParse?.items ?? [])
const dispatchable = computed(() => suggestions.value.filter((s) => s.type !== 'ignore' && s.type !== 'knowledge'))

const SOURCE_TEXT: Record<number, string> = {
  [InboxSource.HotkeyCapture]: '全局热键捕获',
  [InboxSource.TodayBar]: '今日流捕获条',
  [InboxSource.Manual]: '收件箱手输',
  [InboxSource.Retriage]: '纠错重分拣',
}

const ST_CLS: Record<number, string> = {
  0: 'bg-gray-100 text-ink-sub',
  1: 'bg-amber-100 text-amber-700',
  2: 'bg-amber-100 text-amber-800',
  3: 'bg-emerald-100 text-emerald-800',
  4: 'bg-gray-100 text-ink-faint',
  5: 'bg-red-100 text-red-700',
}

const createdRoute = (c: CreatedRef) =>
  c.type === 'task' ? `/tasks?id=${c.id}` : c.type === 'worklog' ? `/work?id=${c.id}` : `/life?id=${c.id}`

const createdTypeText = (t: string) => (t === 'task' ? '任务' : t === 'worklog' ? '工作记录' : '生活记录')

async function load(preselect?: number) {
  loading.value = true
  error.value = ''
  try {
    const page = await miraiApi.inboxList()
    items.value = page.items
    const want = preselect ?? (route.query.id ? Number(route.query.id) : undefined) ?? selectedId.value
    selectedId.value = page.items.some((it) => it.id === want) ? (want as number) : (page.items[0]?.id ?? null)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
  syncSelection()
  notifyBadge()
}

function syncSelection() {
  const it = selected.value
  checked.value = new Set(it?.aiParse ? it.aiParse.items.filter((s) => s.type !== 'ignore' && s.type !== 'knowledge').map((s) => s.suggestionId) : [])
  overridesMap.value = {}
  undoState.value = null
}

function select(it: InboxItem) {
  if (selectedId.value === it.id) return
  selectedId.value = it.id
  router.replace({ query: { ...route.query, id: String(it.id) } }).catch(() => {})
  syncSelection()
}

function toggle(suggestionId: string) {
  const next = new Set(checked.value)
  if (next.has(suggestionId)) next.delete(suggestionId)
  else next.add(suggestionId)
  checked.value = next
}

const allChecked = computed(() => dispatchable.value.length > 0 && dispatchable.value.every((s) => checked.value.has(s.suggestionId)))

function toggleAll() {
  checked.value = allChecked.value ? new Set() : new Set(dispatchable.value.map((s) => s.suggestionId))
}

function patchOverride(suggestionId: string, key: string, value: unknown) {
  const cur = { ...(overridesMap.value[suggestionId] ?? {}) }
  cur[key] = value
  overridesMap.value = { ...overridesMap.value, [suggestionId]: cur }
}

/** 分发按钮文案（视觉稿：「采纳勾选的 2 项（将创建 2 个任务）」） */
const dispatchLabel = computed(() => {
  const n = checked.value.size
  if (!n) return '采纳勾选的 0 项'
  const tasks = dispatchable.value.filter((s) => checked.value.has(s.suggestionId) && s.type === 'task').length
  const rest = n - tasks
  const what = rest === 0 ? `${tasks} 个任务` : tasks === 0 ? `${rest} 条记录` : `${tasks} 个任务 + ${rest} 条记录`
  return `采纳勾选的 ${n} 项（将创建 ${what}）`
})

async function dispatch() {
  const it = selected.value
  if (!it?.aiParse || !checked.value.size || dispatching.value) return
  dispatching.value = true
  opError.value = ''
  try {
    const result = await miraiApi.dispatch(it.id, {
      items: [...checked.value].map((suggestionId) => {
        const ov = overridesMap.value[suggestionId]
        const cleaned = ov ? Object.fromEntries(Object.entries(ov).filter(([, v]) => v !== undefined)) : undefined
        return { suggestionId, overrides: cleaned && Object.keys(cleaned).length ? cleaned : undefined }
      }),
    })
    await load(it.id)
    startUndo(it.id, result)
  } catch (e) {
    opError.value = e instanceof Error ? e.message : '分发失败'
  } finally {
    dispatching.value = false
  }
}

function startUndo(inboxId: number, result: DispatchResult) {
  stopUndo()
  undoState.value = { inboxId, result, left: 30 }
  undoTimer = setInterval(() => {
    if (!undoState.value) return stopUndo()
    undoState.value.left--
    if (undoState.value.left <= 0) stopUndo()
  }, 1000)
}

function stopUndo() {
  if (undoTimer) clearInterval(undoTimer)
  undoTimer = null
  if (undoState.value && undoState.value.left <= 0) undoState.value = null
}

function dismissUndo() {
  stopUndo()
  undoState.value = null
}

async function undo() {
  const st = undoState.value
  if (!st) return
  const id = st.inboxId
  stopUndo()
  undoState.value = null
  try {
    await miraiApi.undo(id)
    await load(id)
  } catch (e) {
    opError.value = e instanceof Error ? e.message : '撤销失败'
  }
}

async function retriage() {
  const it = selected.value
  if (!it || retriaging.value) return
  retriaging.value = true
  opError.value = ''
  try {
    const updated = await miraiApi.retriage(it.id, { correction: retriageText.value.trim() || undefined })
    retriageOpen.value = false
    retriageText.value = ''
    await load(updated.id)
  } catch (e) {
    opError.value = e instanceof Error ? e.message : '重分拣失败'
  } finally {
    retriaging.value = false
  }
}

async function retryFailed() {
  await retriage()
}

async function discardAll() {
  const it = selected.value
  if (!it || discarding.value) return
  discarding.value = true
  try {
    await miraiApi.discard(it.id)
    discardConfirm.value = false
    await load()
  } catch (e) {
    opError.value = e instanceof Error ? e.message : '丢弃失败'
  } finally {
    discarding.value = false
  }
}

function notifyBadge() {
  window.dispatchEvent(new Event('mirai:inbox-changed'))
}

onMounted(() => load())
onBeforeUnmount(stopUndo)
</script>

<template>
  <!-- 加载骨架 -->
  <div v-if="loading" class="flex h-full gap-4">
    <div class="w-72 shrink-0 space-y-2">
      <div v-for="i in 3" :key="i" class="h-16 animate-pulse rounded-lg border border-ink-faint/10 bg-paper-card" />
    </div>
    <div class="min-w-0 flex-1 space-y-3">
      <div class="h-20 animate-pulse rounded-xl bg-paper-card" />
      <div class="h-28 animate-pulse rounded-xl bg-paper-card" />
      <div class="h-28 animate-pulse rounded-xl bg-paper-card" />
    </div>
  </div>

  <!-- 错误态 -->
  <div v-else-if="error" class="flex h-full items-center justify-center">
    <div class="rounded-xl border border-red-200 bg-red-50 p-6 text-center text-sm text-red-700">
      {{ error }}
      <button class="ml-3 rounded-lg border border-red-300 px-3 py-1 text-xs hover:bg-red-100" @click="load()">重试</button>
    </div>
  </div>

  <!-- 空态 -->
  <div v-else-if="!items.length" class="flex h-full items-center justify-center">
    <div class="max-w-sm text-center">
      <div class="text-4xl">✦</div>
      <div class="mt-3 text-sm font-bold">收件箱是空的</div>
      <p class="mt-2 text-xs leading-6 text-ink-sub">
        丢一句话进来（今日流顶部捕获条，或任何应用内 Ctrl+Shift+Space），<br />AI 会帮你分拣成任务 / 工作记录 / 生活记录。
      </p>
      <RouterLink to="/" class="mt-4 inline-block rounded-lg bg-brand px-4 py-1.5 text-xs text-white hover:bg-brand-dark">
        去捕获一条
      </RouterLink>
    </div>
  </div>

  <div v-else class="flex h-full gap-0 overflow-hidden rounded-xl border border-ink-faint/20 bg-paper-card">
    <!-- 左：条目列表（按状态流转） -->
    <aside class="flex w-72 shrink-0 flex-col border-r border-ink-faint/20">
      <div class="border-b border-ink-faint/20 px-4 py-3 text-sm font-bold">
        ✦ 收件箱
        <span class="ml-1 text-xs font-normal text-warn">● {{ items.filter((i) => i.status !== 3 && i.status !== 4).length }} 待处理</span>
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto">
        <button
          v-for="it in items"
          :key="it.id"
          class="block w-full border-b border-ink-faint/10 px-4 py-2.5 text-left"
          :class="selectedId === it.id ? 'bg-brand-soft' : 'hover:bg-paper'"
          @click="select(it)"
        >
          <div class="flex items-start gap-2">
            <span class="mt-0.5 shrink-0 text-[10px]" :class="it.status === 2 ? 'text-warn' : it.status === 3 ? 'text-emerald-500' : 'text-ink-faint'">
              {{ it.status === 2 || it.status === 3 ? '●' : '○' }}
            </span>
            <div class="min-w-0 flex-1">
              <div class="truncate text-xs font-semibold">{{ it.raw }}</div>
              <div class="mt-1">
                <span class="rounded px-1.5 py-px text-[10px]" :class="ST_CLS[it.status]">
                  {{ it.status === 1 ? '分拣中…' : INBOX_STATUS_TEXT[it.status] }}
                </span>
              </div>
            </div>
          </div>
        </button>
      </div>
    </aside>

    <!-- 右：分拣预览 -->
    <section v-if="selected" class="min-w-0 flex-1 space-y-3 overflow-y-auto bg-paper/60 p-4">
      <!-- 原始输入 -->
      <div class="rounded-xl border border-ink-faint/20 bg-paper-card p-4">
        <b class="text-xs text-ink-sub">原始输入</b>
        <p class="mt-1.5 leading-6 text-ink">{{ selected.raw }}</p>
        <div class="mt-1.5 text-[11px] text-ink-faint">
          来源：{{ SOURCE_TEXT[selected.source] ?? selected.source }} · {{ fmtDateTime(selected.createdAt) }} · 已按你的时区解析时间
          <span v-if="selected.correctionNote" class="ml-2 rounded border border-amber-300 bg-amber-50 px-1 text-amber-700">
            纠错备注：{{ selected.correctionNote }}
          </span>
        </div>
      </div>

      <!-- 已分发态 -->
      <div v-if="selected.status === 3" class="rounded-xl border border-emerald-200 bg-emerald-50/60 p-4">
        <b class="text-xs text-emerald-700">✓ 已分发入库</b>
        <p class="mt-1 text-xs text-ink-sub">该条目已完成分拣确认，创建的对象可在对应页面查看。</p>
      </div>

      <!-- 分拣失败态 -->
      <div v-else-if="selected.status === 5" class="rounded-xl border border-red-200 bg-red-50 p-4">
        <b class="text-xs text-red-700">分拣失败</b>
        <p class="mt-1 text-xs leading-6 text-red-600">{{ selected.error }}</p>
        <p class="mt-1 text-[11px] text-ink-faint">原始内容已保留，可重试（DeepSeek 偶发超时属正常现象）。</p>
        <button
          class="mt-2 rounded-lg bg-brand px-3 py-1 text-xs text-white hover:bg-brand-dark disabled:opacity-50"
          :disabled="retriaging"
          @click="retryFailed"
        >
          {{ retriaging ? '重试中…' : '↻ 重试分拣' }}
        </button>
      </div>

      <!-- 分拣中态 -->
      <div v-else-if="!selected.aiParse" class="flex flex-col items-center gap-3 py-14 text-ink-faint">
        <span class="h-6 w-6 animate-spin rounded-full border-2 border-brand-line border-t-brand" />
        <span class="text-xs">AI 分拣中（P95 &lt; 8s）…</span>
      </div>

      <!-- 建议区 -->
      <template v-else>
        <div class="flex items-center gap-2 pt-1">
          <b>AI 建议（{{ selected.aiParse.items.length }} 个对象）</b>
          <AiBadge />
          <label class="ml-3 flex cursor-pointer items-center gap-1.5 text-[11px] text-ink-sub">
            <input type="checkbox" class="accent-emerald-600" :checked="allChecked" @change="toggleAll" />
            全选
          </label>
        </div>

        <TriageSuggestionCard
          v-for="s in selected.aiParse.items"
          :key="s.suggestionId"
          :suggestion="s"
          :checked="checked.has(s.suggestionId)"
          :overrides="overridesMap[s.suggestionId] ?? {}"
          @toggle="toggle(s.suggestionId)"
          @patch="(k, v) => patchOverride(s.suggestionId, k, v)"
        />

        <!-- 低置信度必须显式说明 -->
        <div
          v-for="(u, i) in selected.aiParse.uncertain"
          :key="i"
          class="rounded-lg border border-warn-line bg-warn-soft p-2.5 text-xs leading-6 text-warn"
        >
          ⚠ 不确定：{{ u }}
        </div>

        <!-- 分发结果 + 撤销入口 -->
        <div v-if="undoState" class="rounded-xl border border-emerald-200 bg-emerald-50/70 p-3.5">
          <div class="flex items-center gap-2">
            <b class="text-xs text-emerald-700">✓ 分发完成：创建了 {{ undoState.result.created.length }} 个对象</b>
            <span class="ml-auto text-[11px] text-ink-faint">{{ undoState.left }}s 后撤销入口关闭</span>
          </div>
          <div class="mt-2 flex flex-wrap items-center gap-2">
            <RouterLink
              v-for="c in undoState.result.created"
              :key="c.suggestionId"
              :to="createdRoute(c)"
              class="rounded-full border border-brand-line bg-white px-2 py-0.5 text-[11px] text-brand hover:border-brand"
            >
              {{ createdTypeText(c.type) }} · {{ c.title }} #{{ c.id }}
            </RouterLink>
          </div>
          <div class="mt-2.5 flex gap-2">
            <button
              class="rounded-lg border border-red-300 bg-white px-3 py-1 text-xs text-red-600 hover:bg-red-50"
              @click="undo"
            >
              ↩ 撤销本次分发（{{ undoState.left }}s）
            </button>
            <button class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs text-ink-sub hover:border-ink-sub" @click="dismissUndo()">
              知道了
            </button>
          </div>
        </div>

        <div v-if="opError" class="rounded-lg border border-red-200 bg-red-50 p-2.5 text-xs text-red-700">{{ opError }}</div>

        <!-- 纠错重分拣输入区 -->
        <div v-if="retriageOpen" class="rounded-xl border border-ai-line bg-ai-soft/60 p-3">
          <div class="text-xs text-ai">纠错重分拣：告诉 AI 哪里不对，例如「第二条不是任务」「截止不是周三」</div>
          <div class="mt-2 flex gap-2">
            <input
              v-model="retriageText"
              class="min-w-0 flex-1 rounded-lg border border-ink-faint/30 bg-white px-2.5 py-1.5 text-xs outline-none focus:border-ai"
              placeholder="输入一句话纠正 AI…"
              :disabled="retriaging"
              @keydown.enter.prevent="retriage"
            />
            <button class="rounded-lg bg-ai px-3 py-1.5 text-xs font-semibold text-white hover:opacity-90 disabled:opacity-50" :disabled="retriaging" @click="retriage">
              {{ retriaging ? '重新分拣中…' : '重新分拣' }}
            </button>
            <button class="rounded-lg border border-ink-faint/30 px-3 py-1.5 text-xs text-ink-sub" :disabled="retriaging" @click="retriageOpen = false">
              取消
            </button>
          </div>
        </div>

        <!-- 操作行 -->
        <div class="flex flex-wrap items-center gap-3 pb-4 pt-1">
          <button
            class="rounded-lg bg-brand px-4 py-2 text-xs font-semibold text-white hover:bg-brand-dark disabled:opacity-50"
            :disabled="!checked.size || dispatching"
            @click="dispatch"
          >
            {{ dispatching ? '分发中…' : dispatchLabel }}
          </button>
          <button
            class="rounded-lg border border-ink-faint/30 px-4 py-2 text-xs hover:border-brand hover:text-brand"
            :disabled="retriaging"
            @click="retriageOpen = !retriageOpen"
          >
            纠错重分拣（输入一句话）
          </button>
          <button class="rounded-lg px-2 py-2 text-xs text-ink-faint hover:text-red-600" @click="discardConfirm = true">全部丢弃</button>
          <span class="ml-auto text-[11px] text-ink-faint">采纳后 30 秒内可撤销</span>
        </div>
      </template>
    </section>
  </div>

  <!-- 丢弃二次确认（PRD §6：写操作确认） -->
  <ConfirmDialog
    :open="discardConfirm"
    title="丢弃这条捕获？"
    confirm-text="确认丢弃"
    danger
    :busy="discarding"
    @confirm="discardAll"
    @cancel="discardConfirm = false"
  >
    将丢弃「{{ selected?.raw?.slice(0, 24) }}…」及其全部 AI 建议，此操作不可恢复。
  </ConfirmDialog>
</template>
