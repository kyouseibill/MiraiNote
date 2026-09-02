<script setup lang="ts">
// 悬浮捕获小窗（设计 §5.2 / 视觉稿 ④）：
//   全局热键唤起 → 输入一句 → createInbox(source=HotkeyCapture) →
//   "分拣完成：N 条建议"气泡（5s 自动消失）→ 广播 mirai:inbox-updated（收件箱角标）。
// 该窗常驻（Rust 侧隐藏保活），同时承担到期通知轮询（§5.2 / 视觉稿 ⑥）：
//   30s 轮询 /memos/due-popups → invoke show_task_notification → 系统通知；每日 ≤5 条、按天去重。
// Esc / 失焦隐藏由本页与 Rust 窗口事件共同完成。
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { emit, listen, type UnlistenFn } from '@tauri-apps/api/event'
import { invoke } from '@tauri-apps/api/core'
import { http, unwrap } from '@/api/client'
import { miraiApi } from '@/api/mirai'
import { InboxSource } from '@/api/types'
import type { TriageSuggestion } from '@/api/types'

const USE_MOCK = import.meta.env.MIRAI_USE_MOCK === '1'
const POLL_INTERVAL_MS = 30_000
const DAILY_NOTIFY_LIMIT = 5
const BUBBLE_MS = 5_000

type Phase = 'input' | 'submitting' | 'done' | 'error'
const phase = ref<Phase>('input')
const text = ref('')
const suggestionCount = ref(0)
const bubbleDetail = ref('')
const errorMessage = ref('')
const inputEl = ref<HTMLInputElement | null>(null)
let hideTimer: number | null = null

// ---------------- 提交流程 ----------------

function formatLocalTime(local: string): string {
  const d = new Date(local)
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return `${d.getMonth() + 1}月${d.getDate()}日 ${hh}:${mm}`
}

/** 气泡副文案：优先取任务建议的提醒时间（视觉稿 ④ 的示例文案） */
function suggestionSummary(items: TriageSuggestion[]): string {
  const task = items.find((s) => s.type === 'task')
  const remind = task?.fields && 'remindAtLocal' in task.fields ? task.fields.remindAtLocal : null
  if (remind) return `提醒设为 ${formatLocalTime(remind)}，请到收件箱核对`
  const first = items[0]
  if (first?.fields && 'title' in first.fields && first.fields.title) {
    return `「${first.fields.title}」已就绪，请到收件箱核对`
  }
  return '已进入收件箱，请核对分拣建议'
}

async function submit() {
  const raw = text.value.trim()
  if (!raw || phase.value === 'submitting') return
  phase.value = 'submitting'
  try {
    const item = await miraiApi.createInbox(raw, InboxSource.HotkeyCapture)
    const items = (item.aiParse?.items ?? []).filter((s) => s.type !== 'ignore')
    suggestionCount.value = items.length
    bubbleDetail.value = suggestionSummary(items)
    phase.value = 'done'
    // 收件箱角标事件：主窗（UI 流）监听 mirai:inbox-updated 即可刷新角标
    await emit('mirai:inbox-updated', { inboxItemId: item.id, suggestions: items.length }).catch(() => {})
    scheduleHide(BUBBLE_MS)
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
    phase.value = 'error'
    scheduleHide(6_000)
  }
}

function scheduleHide(ms: number) {
  if (hideTimer) window.clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => void hide(), ms)
}

/** 隐藏窗口并复位表单（浏览器 dev 下无 Tauri 窗口，仅复位） */
async function hide() {
  if (hideTimer) {
    window.clearTimeout(hideTimer)
    hideTimer = null
  }
  try {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    await getCurrentWindow().hide()
  } catch {
    /* 非 Tauri 环境 */
  }
  resetForm()
}

function resetForm() {
  phase.value = 'input'
  text.value = ''
  errorMessage.value = ''
  nextTick(() => inputEl.value?.focus())
}

async function viewInbox() {
  await invoke('open_main', { path: '/inbox' }).catch(() => {})
  await hide()
}

function retry() {
  phase.value = 'input'
  nextTick(() => inputEl.value?.focus())
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.preventDefault()
    void hide()
  }
}

// ---------------- 到期通知轮询（§5.2 / 视觉稿 ⑥） ----------------

interface DueMemo {
  id: number
  content: string
  remindAt: string | null
  section?: string
  priority?: number
}

interface NotifState {
  date: string
  count: number
  ids: number[]
}

const shellPrefs = { enabled: true, paused: false }
let pollTimer: number | null = null
const unlisteners: UnlistenFn[] = []

function dayKey(): string {
  const d = new Date()
  return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`
}

function notifState(): NotifState {
  try {
    const s = JSON.parse(localStorage.getItem('mirai.desktop.notif') || '') as NotifState
    if (s.date === dayKey()) return s
  } catch {
    /* 跨天/损坏 → 重置 */
  }
  return { date: dayKey(), count: 0, ids: [] }
}

function saveNotifState(s: NotifState) {
  localStorage.setItem('mirai.desktop.notif', JSON.stringify(s))
}

async function loadPrefs() {
  try {
    const p = await invoke<{ hotkey: string; notificationsEnabled: boolean; remindersPaused: boolean }>('get_prefs')
    shellPrefs.enabled = p.notificationsEnabled
    shellPrefs.paused = p.remindersPaused
  } catch {
    /* 非 Tauri 环境（纯浏览器 dev）用默认值 */
  }
}

const PRIORITY_LABEL: Record<number, string> = { 1: '低优', 2: '中优', 3: '高优' }

async function notifyMemo(memo: DueMemo) {
  const time = memo.remindAt ? new Date(memo.remindAt) : null
  const hhmm = time
    ? `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`
    : ''
  const title = `Mirai · 任务到期${hhmm ? ` ${hhmm}` : ''}`
  const ctx = [
    memo.section === 'life' ? '生活' : '工作',
    (memo.priority && PRIORITY_LABEL[memo.priority]) || '',
  ]
    .filter(Boolean)
    .join(' · ')
  const body = ctx ? `${memo.content}\n${ctx}` : memo.content
  await invoke('show_task_notification', { title, body }).catch(() => {})
}

async function pollDue() {
  if (!shellPrefs.enabled || shellPrefs.paused) return
  let st = notifState()
  if (st.count >= DAILY_NOTIFY_LIMIT) return
  if (USE_MOCK) {
    void mockNotifyOnce(st)
    return
  }
  try {
    const memos = await unwrap<DueMemo[]>(http.get('/memos/due-popups'))
    st = notifState()
    for (const m of memos) {
      if (st.count >= DAILY_NOTIFY_LIMIT) break
      if (st.ids.includes(m.id)) continue
      await notifyMemo(m)
      st.ids.push(m.id)
      st.count += 1
    }
    saveNotifState(st)
  } catch {
    /* 后端不可达 / 未登录：静默（离线容忍） */
  }
}

/** mock 模式自测：每天最多一条模拟到期通知，验证 通知→节流→持久化 全链路 */
let mockNotified = false
async function mockNotifyOnce(st: NotifState) {
  if (mockNotified || st.count > 0) return
  mockNotified = true
  await notifyMemo({
    id: 999_001,
    content: '【mock】推动安全评审排期（老王周三前要结论）',
    remindAt: new Date().toISOString(),
    section: 'work',
    priority: 3,
  })
  st.count += 1
  saveNotifState(st)
}

// ---------------- 生命周期 ----------------

onMounted(async () => {
  // 捕获窗模式：隐藏主窗外壳（本页为懒加载 chunk，样式仅随 /capture 挂载注入）
  document.body.classList.add('mirai-capture-window')
  window.addEventListener('keydown', onKeydown)
  await loadPrefs()
  try {
    unlisteners.push(
      await listen('mirai:capture-shown', () => resetForm()),
      await listen<boolean>('mirai:reminders-changed', (e) => {
        shellPrefs.paused = e.payload
      }),
    )
  } catch {
    /* 非 Tauri 环境 */
  }
  // mock 自测延迟 45s，避开 dev 启动噪声
  if (USE_MOCK) window.setTimeout(() => void pollDue(), 45_000)
  pollTimer = window.setInterval(() => void pollDue(), POLL_INTERVAL_MS)
  nextTick(() => inputEl.value?.focus())
})

onUnmounted(() => {
  document.body.classList.remove('mirai-capture-window')
  window.removeEventListener('keydown', onKeydown)
  if (pollTimer) window.clearInterval(pollTimer)
  if (hideTimer) window.clearTimeout(hideTimer)
  unlisteners.forEach((u) => u())
})
</script>

<template>
  <div class="h-full bg-paper p-2">
    <!-- 输入行（input / submitting） -->
    <div
      v-if="phase === 'input' || phase === 'submitting'"
      class="flex h-full items-center gap-3 rounded-2xl border border-brand-line bg-white px-4 shadow-xl"
    >
      <span class="text-lg text-brand">✎</span>
      <input
        ref="inputEl"
        v-model="text"
        :disabled="phase === 'submitting'"
        placeholder="随手记一句…（Enter 提交 · Esc 关闭）"
        class="h-11 min-w-0 flex-1 border-none bg-transparent text-sm text-ink outline-none placeholder:text-ink-faint"
        @keydown.enter.prevent="submit"
      />
      <span v-if="phase === 'submitting'" class="flex items-center gap-1.5 text-xs text-brand">
        <span class="inline-block h-3 w-3 animate-spin rounded-full border-2 border-brand border-t-transparent" />
        AI 分拣中…
      </span>
      <template v-else>
        <kbd class="rounded border border-ink-faint/30 bg-paper px-1.5 py-0.5 text-[10px] text-ink-faint">Enter</kbd>
        <kbd class="rounded border border-ink-faint/30 bg-paper px-1.5 py-0.5 text-[10px] text-ink-faint">Esc</kbd>
      </template>
    </div>

    <!-- 分拣完成气泡（done，替代输入行，5s 自动隐藏） -->
    <div
      v-else-if="phase === 'done'"
      class="flex h-full items-center gap-3 rounded-2xl border border-brand-line bg-white px-4 shadow-xl"
    >
      <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-soft text-sm text-brand">✓</span>
      <div class="min-w-0 flex-1">
        <b class="text-sm text-ink">分拣完成：{{ suggestionCount }} 条建议</b>
        <div class="truncate text-xs text-ink-sub">{{ bubbleDetail }}</div>
      </div>
      <button class="rounded-lg bg-brand px-3 py-1.5 text-xs text-white hover:bg-brand-dark" @click="viewInbox">查看</button>
      <button class="rounded-lg border border-ink-faint/30 px-3 py-1.5 text-xs text-ink-sub hover:text-brand" @click="hide">稍后</button>
    </div>

    <!-- 失败气泡（error，可重试） -->
    <div v-else class="flex h-full items-center gap-3 rounded-2xl border border-warn-line bg-warn-soft px-4 shadow-xl">
      <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-warn/10 text-sm text-warn">✗</span>
      <div class="min-w-0 flex-1">
        <b class="text-sm text-ink">分拣失败</b>
        <div class="truncate text-xs text-ink-sub">{{ errorMessage }}</div>
      </div>
      <button class="rounded-lg border border-ink-faint/30 px-3 py-1.5 text-xs text-ink-sub hover:text-brand" @click="retry">重试</button>
      <button class="rounded-lg border border-ink-faint/30 px-3 py-1.5 text-xs text-ink-sub hover:text-brand" @click="hide">稍后</button>
    </div>
  </div>
</template>

<style>
/* 捕获窗模式：隐藏 App.vue 主外壳（页头/导航/抽屉），让 720×120 小窗只渲染捕获条。
   本样式随 /capture 懒加载 chunk 注入，且仅当 body 带 mirai-capture-window 标记类时生效，
   主窗（不带该类）不受影响。 */
body.mirai-capture-window header,
body.mirai-capture-window nav,
body.mirai-capture-window aside {
  display: none !important;
}
body.mirai-capture-window main {
  padding: 0 !important;
  overflow: hidden !important;
}
</style>
