<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { memoApi } from '@/api/data'
import { miraiApi } from '@/api/mirai'
import type { DayOverview, DueTask, FeedItem } from '@/api/types'
import AiBadge from '@/components/AiBadge.vue'
import BriefingCard from '@/components/BriefingCard.vue'
import CaptureBar from '@/components/CaptureBar.vue'
import DueTaskCard from '@/components/DueTaskCard.vue'
import { fmtTime, localDateStr } from '@/utils/format'

// 今日流 · 视觉基准 docs/m1-ui-mockups.html ①
const router = useRouter()

const overview = ref<DayOverview | null>(null)
const loading = ref(true)
const error = ref('')
const regenerating = ref(false)
const acting = ref<Set<number>>(new Set())

async function load() {
  loading.value = true
  error.value = ''
  try {
    overview.value = await miraiApi.dayOverview(localDateStr())
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

async function regenerate() {
  if (regenerating.value || !overview.value) return
  regenerating.value = true
  try {
    overview.value = { ...overview.value, briefing: await miraiApi.regenerateBriefing(localDateStr()), briefingError: null }
  } catch (e) {
    // 429 每日限额 / AI 不可用 → 错误态（友好提示，不影响纯数据区）
    overview.value = { ...overview.value!, briefing: null, briefingError: e instanceof Error ? e.message : '重生成失败' }
  } finally {
    regenerating.value = false
  }
}

/** 到期卡：完成（现有 memos 状态接口） */
async function complete(task: DueTask) {
  if (acting.value.has(task.id)) return
  acting.value.add(task.id)
  try {
    await memoApi.patchStatus(task.id, { isDone: true })
    overview.value = {
      ...overview.value!,
      dueTasks: overview.value!.dueTasks.filter((t) => t.id !== task.id),
      overdueTasks: overview.value!.overdueTasks.filter((t) => t.id !== task.id),
    }
  } finally {
    acting.value.delete(task.id)
  }
}

/** 到期卡：稍后（提醒顺延 1 小时） */
async function later(task: DueTask) {
  if (acting.value.has(task.id) || !task.remindAt) return
  acting.value.add(task.id)
  try {
    const next = new Date(new Date(task.remindAt).getTime() + 3600000).toISOString()
    await memoApi.update(task.id, { content: task.content, remindAt: next, priority: task.priority })
    overview.value = {
      ...overview.value!,
      dueTasks: overview.value!.dueTasks.map((t) => (t.id === task.id ? { ...t, remindAt: next } : t)),
      overdueTasks: overview.value!.overdueTasks.map((t) => (t.id === task.id ? { ...t, remindAt: next } : t)),
    }
  } finally {
    acting.value.delete(task.id)
  }
}

const FEED_ICON: Record<FeedItem['kind'], string> = {
  capture: '✎',
  worklog: '▤',
  lifelog: '☘',
  memo: '✓',
  task: '⚑',
  briefing: '☀',
}

/** 今日动态点击 → 跳转对应对象 */
function feedRoute(f: FeedItem): string | null {
  if (f.refId == null) return null
  switch (f.kind) {
    case 'worklog':
      return `/work?id=${f.refId}`
    case 'lifelog':
      return `/life?id=${f.refId}`
    case 'capture':
      return `/inbox?id=${f.refId}`
    case 'briefing':
      return '/'
    default:
      return `/tasks?id=${f.refId}`
  }
}

const sortedFeed = computed(() => [...(overview.value?.todayFeed ?? [])].reverse())

onMounted(load)
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <CaptureBar />

    <!-- 加载骨架 -->
    <template v-if="loading">
      <div class="h-28 animate-pulse rounded-xl bg-paper-card" />
      <div class="h-20 animate-pulse rounded-xl bg-paper-card" />
      <div class="h-40 animate-pulse rounded-xl bg-paper-card" />
    </template>

    <!-- 错误态 -->
    <div v-else-if="error" class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
      今日流加载失败：{{ error }}
      <button class="ml-2 rounded-lg border border-red-300 px-3 py-1 text-xs hover:bg-red-100" @click="load">重试</button>
    </div>

    <template v-else-if="overview">
      <!-- 收件箱积压提示（视觉稿 ①：晨报内「收件箱积压 3 条」） -->
      <button
        v-if="overview.inboxPendingCount > 0"
        class="flex w-full items-center gap-2 rounded-xl border border-warn-line bg-warn-soft px-4 py-2 text-left text-xs text-warn hover:border-warn"
        @click="router.push('/inbox')"
      >
        <span>⚠</span>
        收件箱积压 {{ overview.inboxPendingCount }} 条待确认，建议抽空处理
        <span class="ml-auto text-ink-faint">去处理 →</span>
      </button>

      <!-- 晨报 -->
      <BriefingCard
        :briefing="overview.briefing"
        :briefing-error="overview.briefingError"
        :date="overview.date"
        :week-entry-count="overview.weekEntryCount"
        :regenerating="regenerating"
        @regenerate="regenerate"
      />

      <!-- 逾期区 -->
      <template v-if="overview.overdueTasks.length">
        <div class="flex items-center gap-2 pt-1 text-xs font-bold text-red-600">⛔ 已逾期（{{ overview.overdueTasks.length }}）</div>
        <DueTaskCard v-for="t in overview.overdueTasks" :key="t.id" :task="t" overdue @done="complete" @later="later" />
      </template>

      <!-- 今日到期 -->
      <div v-if="overview.dueTasks.length" class="flex items-center gap-2 pt-1 text-xs font-bold text-warn">
        ⚑ 今日到期（{{ overview.dueTasks.length }}）
      </div>
      <DueTaskCard v-for="t in overview.dueTasks" :key="t.id" :task="t" @done="complete" @later="later" />
      <div
        v-if="!overview.dueTasks.length && !overview.overdueTasks.length"
        class="rounded-xl border border-dashed border-ink-faint/30 bg-paper-card/50 p-5 text-center text-xs text-ink-faint"
      >
        今天没有到期任务 ☀
      </div>

      <!-- 今日动态（把「AI 在背后做了什么」外化，建立信任） -->
      <section class="rounded-xl border border-ink-faint/20 bg-paper-card p-4 shadow-sm">
        <b class="text-xs text-ink-sub">今日动态</b>
        <div v-if="sortedFeed.length" class="mt-3 ml-1 border-l-2 border-ink-faint/20 pl-4">
          <div v-for="(f, i) in sortedFeed" :key="i" class="relative mb-3 last:mb-0">
            <span class="absolute -left-[21px] top-1 h-2 w-2 rounded-full bg-brand-line" />
            <component
              :is="feedRoute(f) ? 'button' : 'span'"
              :type="feedRoute(f) ? 'button' : undefined"
              class="text-left text-xs leading-6"
              :class="feedRoute(f) ? 'cursor-pointer hover:text-brand' : ''"
              @click="feedRoute(f) && router.push(feedRoute(f)!)"
            >
              <span class="mr-1">{{ FEED_ICON[f.kind] }}</span>
              <span class="mr-2 text-ink-faint">{{ fmtTime(f.time) }}</span>{{ f.title }}
              <AiBadge v-if="f.aiSummary" label="已摘要" />
            </component>
          </div>
        </div>
        <div v-else class="mt-3 rounded-lg border border-dashed border-ink-faint/30 p-4 text-center text-xs text-ink-faint">
          今天还没有动态，去捕获第一条吧
        </div>
      </section>
    </template>
  </div>
</template>
