<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  IconArrowRight,
  IconBulb,
  IconFileText,
  IconLoader2,
  IconPlus,
} from '@tabler/icons-vue'
import { useToast } from '@/composables/useToast'
import { memoApi } from '@/api/memo'
import { workLogApi } from '@/api/workLog'
import { welcomeApi } from '@/api/welcome'
import type { Memo } from '@/types/memo'
import type { WorkLog } from '@/types/workLog'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const loading = ref(true)
const greeting = ref('今天，安静地推进')
const quickCapture = ref('')
const capturing = ref(false)
const workMemos = ref<Memo[]>([])
const lifeMemos = ref<Memo[]>([])
const recentLogs = ref<WorkLog[]>([])
const isDesignPreview = computed(() => import.meta.env.DEV && route.query.designPreview === '1')

const now = new Date()
const todayStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
const weekday = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'][now.getDay()]
const dateLabel = `${now.getMonth() + 1}月${now.getDate()}日 · ${weekday}`

const previewWorkMemos: Memo[] = [
  {
    id: -1, section: 'work', content: '完成 MiraiNote 用户体验优化方案', remindAt: `${todayStr}T11:00:00`,
    remindMethods: 0, emailReminderSent: false, popupAcknowledged: false, remindedAt: null,
    priority: 3, isPinned: true, isDone: false, isArchived: false, createdAt: `${todayStr}T08:12:00`, updatedAt: `${todayStr}T08:12:00`,
  },
  {
    id: -2, section: 'work', content: '与设计团队评审新版原型', remindAt: `${todayStr}T15:30:00`,
    remindMethods: 0, emailReminderSent: false, popupAcknowledged: false, remindedAt: null,
    priority: 2, isPinned: false, isDone: false, isArchived: false, createdAt: `${todayStr}T08:30:00`, updatedAt: `${todayStr}T08:30:00`,
  },
]

const previewLifeMemos: Memo[] = [
  {
    id: -3, section: 'life', content: '整理本周阅读笔记并输出摘要', remindAt: `${todayStr}T20:00:00`,
    remindMethods: 0, emailReminderSent: false, popupAcknowledged: false, remindedAt: null,
    priority: 2, isPinned: false, isDone: false, isArchived: false, createdAt: `${todayStr}T09:10:00`, updatedAt: `${todayStr}T09:10:00`,
  },
  {
    id: -4, section: 'life', content: '周末阅读：《纳瓦尔宝典》摘录', remindAt: null,
    remindMethods: 0, emailReminderSent: false, popupAcknowledged: false, remindedAt: null,
    priority: 1, isPinned: false, isDone: false, isArchived: false,
    createdAt: new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString(),
    updatedAt: new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString(),
  },
]

const previewLogs: WorkLog[] = [
  { id: -1, title: 'MiraiNote 用户体验优化方案', purpose: null, content: null, tags: null, category: '工作', logDate: `${todayStr}T10:24:00`, status: 1, statusRemark: null, createdAt: `${todayStr}T10:24:00`, updatedAt: `${todayStr}T10:24:00` },
  { id: -2, title: '下周项目推进计划', purpose: null, content: null, tags: null, category: '工作', logDate: `${todayStr}T09:16:00`, status: 0, statusRemark: null, createdAt: `${todayStr}T09:16:00`, updatedAt: `${todayStr}T09:16:00` },
  { id: -3, title: '设计系统颜色规范更新', purpose: null, content: null, tags: null, category: '工作', logDate: `${todayStr}T07:35:00`, status: 0, statusRemark: null, createdAt: `${todayStr}T07:35:00`, updatedAt: `${todayStr}T07:35:00` },
]

function weekStart(): string {
  const d = new Date(now)
  const day = d.getDay() === 0 ? 7 : d.getDay()
  d.setDate(d.getDate() - (day - 1))
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function sortByRemind(list: Memo[]): Memo[] {
  return [...list].sort((a, b) => {
    if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1
    if (!a.remindAt && !b.remindAt) return 0
    if (!a.remindAt) return 1
    if (!b.remindAt) return -1
    return new Date(a.remindAt).getTime() - new Date(b.remindAt).getTime()
  })
}

const focusItems = computed(() => sortByRemind([...workMemos.value, ...lifeMemos.value]).slice(0, 3))

const recentEntries = computed(() => {
  const logs = recentLogs.value.map((item) => ({
    id: `log-${item.id}`,
    title: item.title,
    kind: '工作',
    section: 'work' as const,
    date: item.createdAt || item.logDate,
  }))
  const life = lifeMemos.value.slice(0, 2).map((item) => ({
    id: `memo-${item.id}`,
    title: item.content,
    kind: '生活',
    section: 'life' as const,
    date: item.updatedAt || item.createdAt,
  }))
  return [...logs, ...life]
    .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
    .slice(0, 5)
})

function fmtTime(iso: string | null): string {
  if (!iso) return '今天'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '今天'
  const day = d.toDateString() === now.toDateString() ? '今天' : `${d.getMonth() + 1}/${d.getDate()}`
  return `${day} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

function focusTime(item: Memo): string {
  if (!item.remindAt) return '今天'
  const d = new Date(item.remindAt)
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')} 前`
}

async function load() {
  loading.value = true
  if (isDesignPreview.value) {
    workMemos.value = previewWorkMemos
    lifeMemos.value = previewLifeMemos
    recentLogs.value = previewLogs
    loading.value = false
    return
  }

  try {
    const [wm, lm, wl, welcome] = await Promise.all([
      memoApi.list({ section: 'work', includeDone: false, includeArchived: false, page: 1, pageSize: 20 }),
      memoApi.list({ section: 'life', includeDone: false, includeArchived: false, page: 1, pageSize: 20 }),
      workLogApi.list({ page: 1, pageSize: 5, dateFrom: weekStart(), dateTo: todayStr }),
      welcomeApi.getGreeting().catch(() => null),
    ])
    workMemos.value = wm.items
    lifeMemos.value = lm.items
    recentLogs.value = wl.items
    if (welcome?.content) greeting.value = welcome.content
  } finally {
    loading.value = false
  }
}

async function createQuickMemo() {
  const content = quickCapture.value.trim()
  if (!content || capturing.value) return
  capturing.value = true
  try {
    if (isDesignPreview.value) {
      workMemos.value = [
        {
          ...previewWorkMemos[0],
          id: -Date.now(),
          content,
          remindAt: null,
          isPinned: false,
          priority: 2,
        },
        ...workMemos.value,
      ]
    } else {
      const created = await memoApi.create({ section: 'work', content, priority: 2 })
      workMemos.value = [created, ...workMemos.value]
    }
    quickCapture.value = ''
    toast.success('已记录到工作备忘')
  } finally {
    capturing.value = false
  }
}

async function toggleMemo(item: Memo) {
  const next = !item.isDone
  if (isDesignPreview.value || item.id < 0) {
    item.isDone = next
    return
  }
  const updated = await memoApi.patchStatus(item.id, { isDone: next })
  const target = item.section === 'life' ? lifeMemos : workMemos
  const index = target.value.findIndex((memo) => memo.id === item.id)
  if (index >= 0) target.value[index] = updated
}

onMounted(load)
</script>

<template>
  <div class="mx-auto w-full max-w-[1160px] px-6 py-12 sm:px-10 lg:px-14 lg:pb-16 lg:pt-[120px]">
    <div class="grid gap-12 xl:grid-cols-[minmax(0,1fr)_300px] xl:gap-16">
      <section class="min-w-0 xl:pr-2">
        <header>
          <div class="flex items-center gap-3 font-serif text-[15px] text-[#4a4945]">
            <span class="h-[9px] w-[9px] shrink-0 rounded-full bg-[#b4493f]" />
            <span>{{ dateLabel }}</span>
          </div>
          <h1 class="mt-4 font-serif text-[27px] font-medium tracking-[0.04em] text-[#262521] sm:text-[30px]">
            {{ greeting }}
          </h1>
        </header>

        <form class="mt-12 flex h-[52px] w-full" @submit.prevent="createQuickMemo">
          <div class="relative min-w-0 flex-1">
            <input
              v-model="quickCapture"
              type="text"
              class="h-full w-full rounded-l-[5px] rounded-r-none border border-r-0 border-[#d8d3ca] bg-white/60 px-4 pr-24 text-[13px] text-[#34322f] shadow-none placeholder:text-[#aaa59d] focus:z-10"
              placeholder="记录此刻…"
              aria-label="快速记录"
            />
            <span class="pointer-events-none absolute right-4 top-1/2 -translate-y-1/2 text-[11px] text-[#a29d95]">⌘ + Enter</span>
          </div>
          <button
            type="submit"
            class="flex h-[52px] w-[52px] shrink-0 items-center justify-center rounded-r-[5px] bg-[#4c6178] text-white transition hover:bg-[#384b60] disabled:opacity-50"
            :disabled="capturing"
            aria-label="添加记录"
          >
            <IconLoader2 v-if="capturing" :size="19" :stroke-width="1.5" class="animate-spin" />
            <IconPlus v-else :size="22" :stroke-width="1.4" />
          </button>
        </form>

        <div class="mt-24">
          <h2 class="font-serif text-[17px] font-medium tracking-[0.04em] text-[#2f2d29]">今日焦点</h2>
          <div class="mt-5 border-y border-[#e1dcd4]">
            <div v-if="loading" class="flex h-40 items-center justify-center text-[12px] text-[#99938b]">
              <IconLoader2 :size="18" :stroke-width="1.4" class="mr-2 animate-spin" />
              正在整理今天
            </div>
            <template v-else-if="focusItems.length">
              <label
                v-for="item in focusItems"
                :key="item.id"
                class="group grid min-h-[64px] cursor-pointer grid-cols-[18px_minmax(0,1fr)_62px_72px] items-center gap-3 border-b border-[#e8e3dc] py-3 last:border-b-0 lg:grid-cols-[18px_minmax(0,280px)_70px_minmax(84px,1fr)] lg:gap-4"
              >
                <input
                  type="checkbox"
                  class="h-[18px] w-[18px] shrink-0 rounded-[4px] border-[#a9a49c]"
                  :checked="item.isDone"
                  @change="toggleMemo(item)"
                />
                <span class="min-w-0 truncate text-[13px] text-[#3e3b37] group-hover:text-[#384b60]" :class="item.isDone ? 'line-through opacity-45' : ''">
                  {{ item.content }}
                </span>
                <span class="flex shrink-0 items-center gap-2 text-[11px] text-[#979189]">
                  <span class="h-[5px] w-[5px] rounded-full" :class="item.section === 'life' ? 'bg-[#b4493f]' : 'bg-[#4c6178]'" />
                  {{ item.section === 'life' ? '生活' : '工作' }}
                </span>
                <span class="text-right text-[11px] tabular-nums text-[#7f7a72]">{{ focusTime(item) }}</span>
              </label>
            </template>
            <div v-else class="flex h-36 flex-col items-center justify-center text-[12px] text-[#99938b]">
              <p>今天还没有待办</p>
              <button class="mt-3 text-[#4c6178] hover:underline" @click="router.push('/work/memos')">添加第一条备忘</button>
            </div>
          </div>
          <button class="mt-7 inline-flex items-center gap-2 text-[12px] text-[#4c6178] hover:text-[#384b60]" @click="router.push('/work/memos')">
            查看全部记录
            <IconArrowRight :size="15" :stroke-width="1.4" />
          </button>
        </div>
      </section>

      <aside class="border-t border-[#e1dcd4] pt-10 xl:border-l xl:border-t-0 xl:pl-12 xl:pt-14">
        <section>
          <h2 class="font-serif text-[16px] font-medium tracking-[0.04em] text-[#2f2d29]">最近记录</h2>
          <div class="mt-7 space-y-7">
            <button
              v-for="entry in recentEntries"
              :key="entry.id"
              class="group flex min-h-[44px] w-full items-start gap-3 text-left"
              @click="router.push(entry.section === 'life' ? '/life/memos' : '/work/logs')"
            >
              <IconFileText :size="18" :stroke-width="1.35" class="mt-0.5 shrink-0 text-[#69717a]" />
              <span class="min-w-0 flex-1">
                <span class="block truncate font-serif text-[13px] text-[#3b3935] group-hover:text-[#384b60]">{{ entry.title }}</span>
                <span class="mt-1.5 flex items-center gap-2 text-[10px] text-[#99938b]">
                  {{ fmtTime(entry.date) }}
                  <span class="h-[4px] w-[4px] rounded-full" :class="entry.section === 'life' ? 'bg-[#b4493f]' : 'bg-[#4c6178]'" />
                  {{ entry.kind }}
                </span>
              </span>
            </button>
          </div>
          <button class="mt-7 inline-flex items-center gap-2 text-[12px] text-[#4c6178] hover:text-[#384b60]" @click="router.push('/work/logs')">
            查看全部记录
            <IconArrowRight :size="15" :stroke-width="1.4" />
          </button>
        </section>

        <section class="mt-10 border-t border-[#e1dcd4] pt-9">
          <div class="flex items-center gap-3">
            <IconBulb :size="19" :stroke-width="1.35" class="text-[#69717a]" />
            <h2 class="font-serif text-[16px] font-medium tracking-[0.04em] text-[#2f2d29]">Mirai 建议</h2>
          </div>
          <p class="mt-6 text-[12px] leading-7 text-[#66615a]">
            为重要任务预留 90 分钟专注时间，今天的深度工作会更有成效。
          </p>
          <button class="mt-6 inline-flex items-center gap-2 text-[12px] text-[#4c6178] hover:text-[#384b60]" @click="router.push('/chat')">
            专注时间建议
            <IconArrowRight :size="15" :stroke-width="1.4" />
          </button>
        </section>
      </aside>
    </div>
  </div>
</template>
