<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { memoApi } from '@/api/memo'
import { workLogApi } from '@/api/workLog'
import type { Memo } from '@/types/memo'
import type { WorkLog } from '@/types/workLog'

const auth = useAuthStore()
const router = useRouter()

const loading = ref(true)
const workMemos = ref<Memo[]>([])
const lifeMemos = ref<Memo[]>([])
const recentLogs = ref<WorkLog[]>([])

// 今日日期字符串
const today = new Date()
const todayStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`

// 本周起始（周一）
function weekStart(): string {
  const d = new Date(today)
  const day = d.getDay() === 0 ? 7 : d.getDay()
  d.setDate(d.getDate() - (day - 1))
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 按 remindAt 升序排列，无 remindAt 的置底；已到期的排最前
function sortByRemind(list: Memo[]): Memo[] {
  return [...list].sort((a, b) => {
    if (!a.remindAt && !b.remindAt) return 0
    if (!a.remindAt) return 1
    if (!b.remindAt) return -1
    return new Date(a.remindAt).getTime() - new Date(b.remindAt).getTime()
  })
}

const overdueCount = computed(() =>
  [...workMemos.value, ...lifeMemos.value].filter(
    (m) => m.remindAt && new Date(m.remindAt) < today && !m.isDone,
  ).length,
)

const upcomingTodayCount = computed(() =>
  [...workMemos.value, ...lifeMemos.value].filter((m) => {
    if (!m.remindAt || m.isDone) return false
    return m.remindAt.slice(0, 10) === todayStr
  }).length,
)

const sortedWorkMemos = computed(() => sortByRemind(workMemos.value).slice(0, 6))
const sortedLifeMemos = computed(() => sortByRemind(lifeMemos.value).slice(0, 4))

function isOverdue(m: Memo): boolean {
  return !!m.remindAt && new Date(m.remindAt) < today && !m.isDone
}
function isToday(m: Memo): boolean {
  return !!m.remindAt && m.remindAt.slice(0, 10) === todayStr
}

function fmtRemind(iso: string): string {
  const d = new Date(iso)
  const dd = d.getDate()
  const mm = d.getMonth() + 1
  const hh = String(d.getHours()).padStart(2, '0')
  const min = String(d.getMinutes()).padStart(2, '0')
  return `${mm}/${dd} ${hh}:${min}`
}
function fmtDate(iso: string): string {
  return iso ? iso.slice(5, 10).replace('-', '/') : ''
}

const priorityLabel = ['', '低', '中', '高']
const priorityColor = ['', 'text-gray-400', 'text-amber-500', 'text-red-500']

async function load() {
  loading.value = true
  try {
    const [wm, lm, wl] = await Promise.all([
      memoApi.list({ section: 'work', includeDone: false, includeArchived: false, page: 1, pageSize: 20 }),
      memoApi.list({ section: 'life', includeDone: false, includeArchived: false, page: 1, pageSize: 20 }),
      workLogApi.list({ page: 1, pageSize: 5, dateFrom: weekStart(), dateTo: todayStr }),
    ])
    workMemos.value = wm.items
    lifeMemos.value = lm.items
    recentLogs.value = wl.items
  } catch {
    // 错误已由全局拦截器处理
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto px-6 py-6 space-y-6">

    <!-- 顶部欢迎栏 -->
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-900">欢迎回来，{{ auth.user?.username }} 👋</h2>
        <p class="mt-1 text-sm text-gray-500">{{ todayStr.replace(/-/g, ' / ') }}</p>
      </div>
      <button
        class="h-9 px-4 rounded-md bg-indigo-600 text-white text-sm hover:bg-indigo-700 shadow-sm"
        @click="router.push('/work/logs')"
      >
        + 新建工作记录
      </button>
    </div>

    <!-- 统计卡片 -->
    <div class="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <div
        class="bg-white border border-gray-100 rounded-xl p-4 shadow-sm cursor-pointer hover:shadow-md transition"
        @click="router.push('/work/logs')"
      >
        <p class="text-xs text-gray-500">本周工作记录</p>
        <p class="mt-2 text-3xl font-bold text-indigo-600">{{ recentLogs.length }}</p>
        <p class="mt-1 text-xs text-gray-400">近 7 天</p>
      </div>
      <div
        class="bg-white border border-gray-100 rounded-xl p-4 shadow-sm cursor-pointer hover:shadow-md transition"
        @click="router.push('/work/memos')"
      >
        <p class="text-xs text-gray-500">待办工作备忘</p>
        <p class="mt-2 text-3xl font-bold text-indigo-600">{{ workMemos.length }}</p>
        <p class="mt-1 text-xs text-gray-400">未完成</p>
      </div>
      <div
        class="bg-white border border-gray-100 rounded-xl p-4 shadow-sm cursor-pointer hover:shadow-md transition"
        @click="router.push('/life/memos')"
      >
        <p class="text-xs text-gray-500">待办生活备忘</p>
        <p class="mt-2 text-3xl font-bold text-rose-500">{{ lifeMemos.length }}</p>
        <p class="mt-1 text-xs text-gray-400">未完成</p>
      </div>
      <div
        class="rounded-xl p-4 shadow-sm"
        :class="overdueCount > 0 ? 'bg-red-50 border border-red-200' : 'bg-white border border-gray-100'"
      >
        <p class="text-xs" :class="overdueCount > 0 ? 'text-red-500' : 'text-gray-500'">今日到期</p>
        <p class="mt-2 text-3xl font-bold" :class="overdueCount > 0 ? 'text-red-600' : 'text-gray-400'">
          {{ upcomingTodayCount + overdueCount }}
        </p>
        <p class="mt-1 text-xs" :class="overdueCount > 0 ? 'text-red-400' : 'text-gray-400'">
          {{ overdueCount > 0 ? `${overdueCount} 条已逾期` : '今天到期' }}
        </p>
      </div>
    </div>

    <!-- 加载中 -->
    <div v-if="loading" class="py-16 text-center text-gray-400 text-sm">加载中…</div>

    <template v-else>
      <!-- 主内容区：左右两列 -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">

        <!-- 工作备忘 -->
        <div class="bg-white border border-gray-100 rounded-xl shadow-sm overflow-hidden">
          <div class="flex items-center justify-between px-4 py-3 border-b border-gray-100">
            <h3 class="font-semibold text-gray-900 text-sm">📌 工作备忘</h3>
            <button
              class="text-xs text-indigo-600 hover:underline"
              @click="router.push('/work/memos')"
            >
              全部 →
            </button>
          </div>
          <ul v-if="sortedWorkMemos.length" class="divide-y divide-gray-50">
            <li
              v-for="m in sortedWorkMemos"
              :key="m.id"
              class="px-4 py-3 hover:bg-gray-50 transition cursor-pointer"
              @click="router.push('/work/memos')"
            >
              <div class="flex items-start gap-2">
                <span
                  class="mt-0.5 shrink-0 text-xs font-medium"
                  :class="priorityColor[m.priority]"
                  title="优先级"
                >{{ priorityLabel[m.priority] }}</span>
                <div class="min-w-0 flex-1">
                  <p class="text-sm text-gray-800 line-clamp-2">{{ m.content }}</p>
                  <div class="mt-1 flex items-center gap-2 flex-wrap">
                    <span v-if="m.remindAt" class="text-xs" :class="isOverdue(m) ? 'text-red-500 font-medium' : isToday(m) ? 'text-amber-600' : 'text-gray-400'">
                      🔔 {{ fmtRemind(m.remindAt) }}{{ isOverdue(m) ? ' 已逾期' : '' }}
                    </span>
                    <span v-if="m.isPinned" class="text-xs text-indigo-500">📌 已置顶</span>
                  </div>
                </div>
              </div>
            </li>
          </ul>
          <div v-else class="px-4 py-8 text-center text-sm text-gray-400">
            暂无待办工作备忘
          </div>
        </div>

        <!-- 最近工作记录 -->
        <div class="bg-white border border-gray-100 rounded-xl shadow-sm overflow-hidden">
          <div class="flex items-center justify-between px-4 py-3 border-b border-gray-100">
            <h3 class="font-semibold text-gray-900 text-sm">📝 本周工作记录</h3>
            <button
              class="text-xs text-indigo-600 hover:underline"
              @click="router.push('/work/logs')"
            >
              全部 →
            </button>
          </div>
          <ul v-if="recentLogs.length" class="divide-y divide-gray-50">
            <li
              v-for="log in recentLogs"
              :key="log.id"
              class="px-4 py-3 hover:bg-gray-50 transition cursor-pointer"
              @click="router.push('/work/logs')"
            >
              <div class="flex items-center gap-2">
                <span class="text-xs font-mono text-gray-400 shrink-0">{{ fmtDate(log.logDate) }}</span>
                <span v-if="log.category" class="text-xs px-1.5 py-0.5 rounded bg-indigo-50 text-indigo-600 shrink-0">{{ log.category }}</span>
                <p class="text-sm text-gray-800 font-medium truncate">{{ log.title }}</p>
              </div>
              <p v-if="log.purpose" class="mt-0.5 ml-16 text-xs text-gray-400 truncate">{{ log.purpose }}</p>
            </li>
          </ul>
          <div v-else class="px-4 py-8 text-center text-sm text-gray-400">
            本周还没有工作记录，
            <button class="text-indigo-600 hover:underline" @click="router.push('/work/logs')">去记录一条</button>
          </div>
        </div>

        <!-- 生活备忘 -->
        <div class="bg-white border border-gray-100 rounded-xl shadow-sm overflow-hidden">
          <div class="flex items-center justify-between px-4 py-3 border-b border-gray-100">
            <h3 class="font-semibold text-gray-900 text-sm">🌸 生活备忘</h3>
            <button
              class="text-xs text-rose-500 hover:underline"
              @click="router.push('/life/memos')"
            >
              全部 →
            </button>
          </div>
          <ul v-if="sortedLifeMemos.length" class="divide-y divide-gray-50">
            <li
              v-for="m in sortedLifeMemos"
              :key="m.id"
              class="px-4 py-3 hover:bg-gray-50 transition cursor-pointer"
              @click="router.push('/life/memos')"
            >
              <div class="flex items-start gap-2">
                <span class="mt-0.5 shrink-0 text-xs font-medium" :class="priorityColor[m.priority]">
                  {{ priorityLabel[m.priority] }}
                </span>
                <div class="min-w-0 flex-1">
                  <p class="text-sm text-gray-800 line-clamp-2">{{ m.content }}</p>
                  <span v-if="m.remindAt" class="text-xs mt-0.5 inline-block" :class="isOverdue(m) ? 'text-red-500 font-medium' : isToday(m) ? 'text-amber-600' : 'text-gray-400'">
                    🔔 {{ fmtRemind(m.remindAt) }}{{ isOverdue(m) ? ' 已逾期' : '' }}
                  </span>
                </div>
              </div>
            </li>
          </ul>
          <div v-else class="px-4 py-8 text-center text-sm text-gray-400">
            暂无待办生活备忘
          </div>
        </div>

        <!-- 快捷导航 -->
        <div class="bg-white border border-gray-100 rounded-xl shadow-sm p-4">
          <h3 class="font-semibold text-gray-900 text-sm mb-3">⚡ 快捷入口</h3>
          <div class="grid grid-cols-2 gap-3">
            <button
              class="flex items-center gap-2 rounded-lg border border-indigo-100 bg-indigo-50 px-3 py-3 text-sm text-indigo-700 hover:bg-indigo-100 transition text-left"
              @click="router.push('/work/logs')"
            >
              <span class="text-base">📝</span>
              <div>
                <p class="font-medium text-xs">工作记录</p>
                <p class="text-xs text-indigo-400 mt-0.5">记录今日工作</p>
              </div>
            </button>
            <button
              class="flex items-center gap-2 rounded-lg border border-indigo-100 bg-indigo-50 px-3 py-3 text-sm text-indigo-700 hover:bg-indigo-100 transition text-left"
              @click="router.push('/work/memos')"
            >
              <span class="text-base">📌</span>
              <div>
                <p class="font-medium text-xs">工作备忘</p>
                <p class="text-xs text-indigo-400 mt-0.5">添加提醒事项</p>
              </div>
            </button>
            <button
              class="flex items-center gap-2 rounded-lg border border-rose-100 bg-rose-50 px-3 py-3 text-sm text-rose-700 hover:bg-rose-100 transition text-left"
              @click="router.push('/life/logs')"
            >
              <span class="text-base">🌅</span>
              <div>
                <p class="font-medium text-xs">生活记录</p>
                <p class="text-xs text-rose-400 mt-0.5">记录今日心情</p>
              </div>
            </button>
            <button
              class="flex items-center gap-2 rounded-lg border border-purple-100 bg-purple-50 px-3 py-3 text-sm text-purple-700 hover:bg-purple-100 transition text-left"
              @click="router.push('/work/reports')"
            >
              <span class="text-base">✨</span>
              <div>
                <p class="font-medium text-xs">AI 周报</p>
                <p class="text-xs text-purple-400 mt-0.5">一键生成周报</p>
              </div>
            </button>
          </div>
        </div>

      </div>
    </template>

  </div>
</template>
