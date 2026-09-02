<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { workLogApi, WORKLOG_STATUS_TEXT, type WorkLog, type WorkLogPayload, type WorkLogStatus } from '@/api/data'
import AiBadge from '@/components/AiBadge.vue'
import MarkdownView from '@/components/MarkdownView.vue'
import { useUiStore } from '@/stores/ui'

// 工作流（M1 精简版）· 视觉基准 docs/m1-ui-mockups.html ⑤ 左主区
// 列表 + 详情 + 基础编辑 + 💬 挂载侧边对话（现有 API 直读）
const route = useRoute()
const router = useRouter()
const ui = useUiStore()

const items = ref<WorkLog[]>([])
const loading = ref(true)
const error = ref('')
const keyword = ref('')

const selectedId = ref<number | null>(null)
const selected = computed(() => items.value.find((w) => w.id === selectedId.value) ?? null)

const STATUSES: { v: WorkLogStatus; t: string }[] = [
  { v: 0, t: '未标记' },
  { v: 1, t: '进行中' },
  { v: 2, t: '已完成' },
  { v: 3, t: '已延期' },
]

const filtered = computed(() => {
  const k = keyword.value.trim().toLowerCase()
  if (!k) return items.value
  return items.value.filter(
    (w) => w.title.toLowerCase().includes(k) || (w.tags ?? '').toLowerCase().includes(k) || (w.content ?? '').toLowerCase().includes(k),
  )
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await workLogApi.list()
    const want = route.query.id ? Number(route.query.id) : null
    selectedId.value = want && items.value.some((w) => w.id === want) ? want : (items.value[0]?.id ?? null)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

function select(w: WorkLog) {
  selectedId.value = w.id
  router.replace({ query: { ...route.query, id: String(w.id) } }).catch(() => {})
}

function openDiscuss(w: WorkLog) {
  ui.openContext({ type: 'worklog', id: w.id, title: w.title })
}

// 视觉稿 ⑤：「→」展开当前选中记录的侧边讨论（编辑抽屉打开时忽略；收起逻辑在 App.vue）
function onKeydown(e: KeyboardEvent) {
  if (e.key !== 'ArrowRight' || ui.contextTarget || editOpen.value || !selected.value) return
  const t = e.target as HTMLElement | null
  if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return
  openDiscuss(selected.value)
}

const tagsOf = (w: WorkLog) => (w.tags ?? '').split(/[,，]/).map((t) => t.trim()).filter(Boolean)

// ---- 编辑抽屉 ----
const editOpen = ref(false)
const saving = ref(false)
const form = ref<WorkLogPayload & { id?: number }>(emptyForm())

function emptyForm(): WorkLogPayload {
  return { title: '', purpose: null, content: '', tags: '', category: null, logDate: new Date().toISOString().slice(0, 10), status: 0, statusRemark: null }
}

function openCreate() {
  form.value = emptyForm()
  editOpen.value = true
}

function openEdit(w: WorkLog) {
  form.value = {
    id: w.id,
    title: w.title,
    purpose: w.purpose,
    content: w.content ?? '',
    tags: w.tags ?? '',
    category: w.category,
    logDate: w.logDate,
    status: w.status,
    statusRemark: w.statusRemark,
  }
  editOpen.value = true
}

async function save() {
  if (!form.value.title.trim() || saving.value) return
  saving.value = true
  try {
    const saved = await workLogApi.save(form.value)
    editOpen.value = false
    await load()
    selectedId.value = saved.id
  } catch (e) {
    error.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  load()
  window.addEventListener('keydown', onKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="flex h-full gap-4">
    <!-- 左：列表 -->
    <aside class="flex w-64 shrink-0 flex-col">
      <div class="flex items-center gap-2">
        <h1 class="text-base font-bold">工作流</h1>
        <span class="text-[11px] text-ink-faint">{{ items.length }} 条</span>
        <button class="ml-auto rounded-lg bg-brand px-2.5 py-1 text-xs text-white hover:bg-brand-dark" @click="openCreate">＋ 新建</button>
      </div>
      <input
        v-model="keyword"
        class="mt-3 w-full rounded-lg border border-ink-faint/25 bg-paper-card px-3 py-1.5 text-xs outline-none focus:border-brand"
        placeholder="搜索标题 / 标签 / 内容…"
      />
      <div class="mt-3 min-h-0 flex-1 space-y-2 overflow-y-auto pr-0.5">
        <div v-if="loading" class="space-y-2">
          <div v-for="i in 4" :key="i" class="h-16 animate-pulse rounded-lg bg-paper-card" />
        </div>
        <button
          v-for="w in filtered"
          :key="w.id"
          class="block w-full rounded-lg border p-3 text-left"
          :class="selectedId === w.id ? 'border-brand bg-brand-soft/60 ring-1 ring-brand' : 'border-ink-faint/20 bg-paper-card hover:border-brand/50'"
          @click="select(w)"
        >
          <div class="truncate text-xs font-semibold">{{ w.title }}</div>
          <div class="mt-1 flex items-center gap-1.5">
            <span class="rounded px-1 text-[10px]" :class="{
              'bg-blue-50 text-blue-600': w.status === 1,
              'bg-emerald-50 text-emerald-700': w.status === 2,
              'bg-red-50 text-red-600': w.status === 3,
              'bg-gray-100 text-ink-faint': w.status === 0,
            }">{{ WORKLOG_STATUS_TEXT[w.status] }}</span>
            <span class="text-[10px] text-ink-faint">{{ w.logDate.slice(5) }}</span>
          </div>
        </button>
        <div v-if="!loading && !filtered.length" class="rounded-lg border border-dashed border-ink-faint/30 p-6 text-center text-xs text-ink-faint">
          没有匹配的工作记录
        </div>
      </div>
    </aside>

    <!-- 右：详情 -->
    <section class="min-w-0 flex-1">
      <div v-if="error" class="rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700">
        {{ error }}
        <button class="ml-2 underline" @click="load">重试</button>
      </div>
      <div v-else-if="loading" class="space-y-3 pt-1">
        <div class="h-8 w-2/3 animate-pulse rounded bg-paper-card" />
        <div class="h-40 animate-pulse rounded-xl bg-paper-card" />
      </div>
      <div v-else-if="selected" class="rounded-xl border border-ink-faint/20 bg-paper-card p-5">
        <div class="flex flex-wrap items-center gap-2">
          <b class="text-[15px]">{{ selected.title }}</b>
          <span
            v-for="t in tagsOf(selected)"
            :key="t"
            class="rounded-full border border-brand-line bg-brand-soft px-2 text-[10px] text-brand"
          >
            {{ t }}
          </span>
          <span class="text-[11px] text-ink-faint">
            {{ selected.logDate }} · {{ WORKLOG_STATUS_TEXT[selected.status] }}<template v-if="selected.category"> · {{ selected.category }}</template>
          </span>
          <span class="ml-auto flex gap-2">
            <button class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs hover:border-brand hover:text-brand" @click="openEdit(selected)">✎ 编辑</button>
            <button
              class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs hover:border-brand hover:text-brand"
              title="侧边讨论（快捷键 →）"
              @click="openDiscuss(selected)"
            >
              💬 讨论 <kbd class="rounded border border-ink-faint/30 px-1 text-[9px] text-ink-faint">→</kbd>
            </button>
          </span>
        </div>

        <p v-if="selected.purpose" class="mt-2 text-xs text-ink-sub">目的：{{ selected.purpose }}</p>

        <MarkdownView :source="selected.content" class="mt-3" />

        <div v-if="selected.aiSummary" class="mt-3 flex items-start gap-1 border-t border-dashed border-ink-faint/20 pt-2 text-[11.5px] text-ink-sub">
          <AiBadge /> 摘要：{{ selected.aiSummary }}
          <span class="ml-auto shrink-0 text-ink-faint">· AI 生成</span>
        </div>
      </div>
      <div v-else class="flex h-full items-center justify-center rounded-xl border border-dashed border-ink-faint/30 text-xs text-ink-faint">
        选择左侧记录查看详情
      </div>
    </section>

    <!-- 编辑抽屉 -->
    <Teleport to="body">
      <div v-if="editOpen" class="fixed inset-0 z-50 flex justify-end bg-black/30" @click.self="editOpen = false">
        <div class="flex h-full w-[460px] flex-col bg-white shadow-xl">
          <div class="flex items-center justify-between border-b border-ink-faint/15 px-5 py-3.5">
            <b class="text-sm">{{ form.id ? '编辑工作记录' : '新建工作记录' }}</b>
            <button class="text-ink-faint hover:text-ink" @click="editOpen = false">✕</button>
          </div>
          <div class="min-h-0 flex-1 space-y-3 overflow-y-auto px-5 py-4">
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">标题 *</span>
              <input v-model="form.title" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
            </label>
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">目的</span>
              <input v-model="form.purpose" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
            </label>
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">内容（支持 Markdown）</span>
              <textarea v-model="form.content" rows="8" class="w-full rounded-lg border border-ink-faint/30 px-3 py-2 text-sm leading-6 outline-none focus:border-brand" />
            </label>
            <div class="grid grid-cols-2 gap-3">
              <label class="block">
                <span class="mb-1 block text-xs font-medium text-ink-sub">标签（逗号分隔）</span>
                <input v-model="form.tags" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
              </label>
              <label class="block">
                <span class="mb-1 block text-xs font-medium text-ink-sub">分类</span>
                <input v-model="form.category" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
              </label>
              <label class="block">
                <span class="mb-1 block text-xs font-medium text-ink-sub">日期</span>
                <input v-model="form.logDate" type="date" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
              </label>
              <label class="block">
                <span class="mb-1 block text-xs font-medium text-ink-sub">状态</span>
                <select v-model.number="form.status" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand">
                  <option v-for="s in STATUSES" :key="s.v" :value="s.v">{{ s.t }}</option>
                </select>
              </label>
            </div>
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">状态备注</span>
              <input v-model="form.statusRemark" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
            </label>
          </div>
          <div class="flex justify-end gap-2 border-t border-ink-faint/15 px-5 py-3">
            <button class="rounded-lg border border-ink-faint/30 px-4 py-1.5 text-xs" @click="editOpen = false">取消</button>
            <button class="rounded-lg bg-brand px-4 py-1.5 text-xs font-semibold text-white hover:bg-brand-dark disabled:opacity-50" :disabled="saving || !form.title.trim()" @click="save">
              {{ saving ? '保存中…' : '保存' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>
