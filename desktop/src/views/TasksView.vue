<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { memoApi, type Memo } from '@/api/data'
import { useUiStore } from '@/stores/ui'
import { fmtDue, localDateStr, overdueDays, PRIORITY_TEXT, remindMethodsText } from '@/utils/format'

// 任务（M1 精简版，数据 = 现有 Memo）· 视觉基准 docs/m1-ui-mockups.html ① 的任务卡扩展为管理页
// 今日到期 / 逾期 / 全部（未完成）分组 + 完成/稍后/置顶 + 💬 侧边对话挂载
const route = useRoute()
const router = useRouter()
const ui = useUiStore()

const items = ref<Memo[]>([])
const loading = ref(true)
const error = ref('')
const sectionFilter = ref<'all' | 'work' | 'life'>('all')
const showDone = ref(false)
const acting = ref<Set<number>>(new Set())

const filtered = computed(() => {
  let list = items.value
  if (sectionFilter.value !== 'all') list = list.filter((m) => m.section === sectionFilter.value)
  if (!showDone.value) list = list.filter((m) => !m.isDone)
  return list
})

const today = computed(() => filtered.value.filter((m) => !m.isDone && m.remindAt?.startsWith(localDateStr())))
const overdue = computed(() =>
  filtered.value.filter((m) => !m.isDone && m.remindAt && m.remindAt.slice(0, 10) < localDateStr()),
)
const upcoming = computed(() =>
  filtered.value.filter((m) => !m.isDone && (!m.remindAt || m.remindAt.slice(0, 10) > localDateStr())),
)
const doneList = computed(() => filtered.value.filter((m) => m.isDone))

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await memoApi.list()
    if (route.query.id) {
      const id = Number(route.query.id)
      if (items.value.some((m) => m.id === id)) {
        showDone.value = true
      }
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

async function toggleDone(m: Memo) {
  if (acting.value.has(m.id)) return
  acting.value.add(m.id)
  try {
    const updated = await memoApi.patchStatus(m.id, { isDone: !m.isDone })
    items.value = items.value.map((x) => (x.id === m.id ? updated : x))
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    acting.value.delete(m.id)
  }
}

async function togglePin(m: Memo) {
  if (acting.value.has(m.id)) return
  acting.value.add(m.id)
  try {
    const updated = await memoApi.patchStatus(m.id, { isPinned: !m.isPinned })
    items.value = items.value.map((x) => (x.id === m.id ? updated : x))
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    acting.value.delete(m.id)
  }
}

/** 稍后：提醒顺延 1 小时（现有 memos 更新接口） */
async function later(m: Memo) {
  if (acting.value.has(m.id) || !m.remindAt) return
  acting.value.add(m.id)
  try {
    const next = new Date(new Date(m.remindAt).getTime() + 3600000).toISOString()
    const updated = await memoApi.update(m.id, { content: m.content, remindAt: next, priority: m.priority })
    items.value = items.value.map((x) => (x.id === m.id ? updated : x))
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    acting.value.delete(m.id)
  }
}

function openDiscuss(m: Memo) {
  ui.openContext({ type: 'memo', id: m.id, title: m.content.slice(0, 18) })
}

const PRIO_CLS: Record<number, string> = {
  1: 'border-ink-faint/30 text-ink-sub',
  2: 'border-amber-300 text-amber-700',
  3: 'border-red-300 text-red-600',
}

onMounted(load)
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-3">
    <div class="flex flex-wrap items-center gap-3">
      <h1 class="text-base font-bold">任务</h1>
      <div class="flex overflow-hidden rounded-lg border border-ink-faint/25 text-xs">
        <button
          v-for="s in [{ v: 'all', t: '全部' }, { v: 'work', t: '工作' }, { v: 'life', t: '生活' }] as const"
          :key="s.v"
          class="px-3 py-1"
          :class="sectionFilter === s.v ? 'bg-brand text-white' : 'bg-paper-card text-ink-sub hover:text-brand'"
          @click="sectionFilter = s.v"
        >
          {{ s.t }}
        </button>
      </div>
      <label class="flex cursor-pointer items-center gap-1.5 text-[11px] text-ink-sub">
        <input v-model="showDone" type="checkbox" class="accent-brand" />
        显示已完成
      </label>
      <span class="ml-auto text-[11px] text-ink-faint">今日 {{ today.length }} · 逾期 {{ overdue.length }} · 待办 {{ upcoming.length }}</span>
    </div>

    <div v-if="error" class="rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700">
      {{ error }}
      <button class="ml-2 underline" @click="load">重试</button>
    </div>

    <div v-if="loading" class="space-y-2">
      <div v-for="i in 4" :key="i" class="h-14 animate-pulse rounded-xl bg-paper-card" />
    </div>

    <template v-else>
      <!-- 逾期 -->
      <template v-if="overdue.length">
        <div class="flex items-center gap-2 pt-1 text-xs font-bold text-red-600">⛔ 已逾期（{{ overdue.length }}）</div>
        <div v-for="m in overdue" :key="m.id" class="rounded-xl border border-l-[3px] border-l-red-500 border-ink-faint/20 bg-paper-card p-3">
          <div class="flex items-center gap-2.5">
            <button
              class="h-4 w-4 shrink-0 rounded-full border-2"
              :class="m.isDone ? 'border-emerald-500 bg-emerald-500' : 'border-ink-faint/50 hover:border-emerald-500'"
              title="完成"
              @click="toggleDone(m)"
            />
            <span class="truncate text-[13px]">{{ m.content }}</span>
            <span v-if="m.isPinned" title="置顶">📌</span>
            <span class="shrink-0 rounded border px-1 text-[10px]" :class="PRIO_CLS[m.priority]">{{ PRIORITY_TEXT[m.priority] }}</span>
            <span class="shrink-0 text-[11px] text-red-500">已逾期 {{ m.remindAt ? overdueDays(m.remindAt) : 1 }} 天</span>
            <span class="ml-auto flex shrink-0 gap-1.5">
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="later(m)">稍后 1h</button>
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="openDiscuss(m)">💬</button>
            </span>
          </div>
        </div>
      </template>

      <!-- 今日到期 -->
      <template v-if="today.length">
        <div class="flex items-center gap-2 pt-1 text-xs font-bold text-warn">⚑ 今日到期（{{ today.length }}）</div>
        <div v-for="m in today" :key="m.id" class="rounded-xl border border-l-[3px] border-l-warn border-ink-faint/20 bg-paper-card p-3">
          <div class="flex items-center gap-2.5">
            <button
              class="h-4 w-4 shrink-0 rounded-full border-2"
              :class="m.isDone ? 'border-emerald-500 bg-emerald-500' : 'border-ink-faint/50 hover:border-emerald-500'"
              title="完成"
              @click="toggleDone(m)"
            />
            <span class="truncate text-[13px]" :class="m.isDone ? 'text-ink-faint line-through' : ''">{{ m.content }}</span>
            <span v-if="m.isPinned" title="置顶">📌</span>
            <span class="shrink-0 rounded border px-1 text-[10px]" :class="PRIO_CLS[m.priority]">{{ PRIORITY_TEXT[m.priority] }}</span>
            <span class="shrink-0 text-[11px] text-ink-faint">{{ fmtDue(m.remindAt) }} · {{ remindMethodsText(m.remindMethods) }}</span>
            <span class="ml-auto flex shrink-0 gap-1.5">
              <button class="rounded-lg bg-brand px-2 py-0.5 text-[11px] text-white hover:bg-brand-dark" @click="toggleDone(m)">完成</button>
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="later(m)">稍后 1h</button>
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="togglePin(m)">📌</button>
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="openDiscuss(m)">💬</button>
            </span>
          </div>
        </div>
      </template>

      <!-- 未来 / 无提醒 -->
      <template v-if="upcoming.length">
        <div class="flex items-center gap-2 pt-1 text-xs font-bold text-ink-sub">待办（{{ upcoming.length }}）</div>
        <div v-for="m in upcoming" :key="m.id" class="rounded-xl border border-ink-faint/20 bg-paper-card p-3">
          <div class="flex items-center gap-2.5">
            <button
              class="h-4 w-4 shrink-0 rounded-full border-2"
              :class="m.isDone ? 'border-emerald-500 bg-emerald-500' : 'border-ink-faint/50 hover:border-emerald-500'"
              title="完成"
              @click="toggleDone(m)"
            />
            <span class="min-w-0 flex-1 truncate text-[13px]">{{ m.content }}</span>
            <span v-if="m.isPinned" title="置顶">📌</span>
            <span class="shrink-0 rounded border px-1 text-[10px]" :class="PRIO_CLS[m.priority]">{{ PRIORITY_TEXT[m.priority] }}</span>
            <span class="shrink-0 text-[11px] text-ink-faint">{{ fmtDue(m.remindAt) }}</span>
            <span class="ml-auto flex shrink-0 gap-1.5">
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="togglePin(m)">📌</button>
              <button class="rounded-lg border border-ink-faint/30 px-2 py-0.5 text-[11px] hover:border-brand hover:text-brand" @click="openDiscuss(m)">💬</button>
            </span>
          </div>
        </div>
      </template>

      <!-- 已完成 -->
      <template v-if="doneList.length">
        <div class="flex items-center gap-2 pt-1 text-xs font-bold text-ink-faint">已完成（{{ doneList.length }}）</div>
        <div v-for="m in doneList" :key="m.id" class="rounded-xl border border-ink-faint/15 bg-paper-card/60 p-3">
          <div class="flex items-center gap-2.5">
            <button
              class="h-4 w-4 shrink-0 rounded-full border-2 border-emerald-500 bg-emerald-500 text-[9px] text-white"
              title="取消完成"
              @click="toggleDone(m)"
            >
              ✓
            </button>
            <span class="min-w-0 flex-1 truncate text-[13px] text-ink-faint line-through">{{ m.content }}</span>
            <span class="shrink-0 rounded border px-1 text-[10px]" :class="PRIO_CLS[m.priority]">{{ PRIORITY_TEXT[m.priority] }}</span>
          </div>
        </div>
      </template>

      <div
        v-if="!filtered.length"
        class="rounded-xl border border-dashed border-ink-faint/30 p-12 text-center text-xs text-ink-faint"
      >
        没有任务 —— 去收件箱或捕获条丢一句话，AI 帮你建任务
      </div>
    </template>
  </div>
</template>
