<script setup lang="ts">
import { ref } from 'vue'
import { reportApi, type WeeklyReport } from '@/api/data'
import MarkdownView from '@/components/MarkdownView.vue'
import { fmtDate } from '@/utils/format'

// 周报预留入口 · 视觉基准 docs/m1-ui-mockups.html ⑦
// M2 上线三区共创；M1 提供历史周报只读列表（直读现有 API）+ 生成引导回 Web 端。
const showHistory = ref(false)
const loading = ref(false)
const reports = ref<WeeklyReport[]>([])
const expandedId = ref<number | null>(null)
const error = ref('')

async function fetchReports() {
  loading.value = true
  error.value = ''
  try {
    reports.value = await reportApi.list()
  } catch (e) {
    error.value = e instanceof Error ? e.message : '历史周报加载失败'
  } finally {
    loading.value = false
  }
}

async function toggleHistory() {
  showHistory.value = !showHistory.value
  if (showHistory.value && !reports.value.length) await fetchReports()
}

function openReport(r: WeeklyReport) {
  expandedId.value = expandedId.value === r.id ? null : r.id
}

function openWebApp() {
  window.open('/', '_blank')
}
</script>

<template>
  <div class="mx-auto max-w-3xl">
    <!-- M2 预告 -->
    <div class="flex min-h-[320px] flex-col items-center justify-center rounded-xl border border-ink-faint/20 bg-paper-card p-8 text-center">
      <div class="text-[44px]">¶</div>
      <div class="mt-2 text-base font-bold">周报 · 新版即将上线（M2）</div>
      <p class="mt-3 max-w-md leading-7 text-ink-sub">
        M2 将提供「事实区 + 叙事区 + 追问区」的 AI 共创周报，周五下午草稿自动就绪。<br />
        在此之前，历史周报可正常查看，生成功能继续在 Web 端使用。
      </p>
      <div class="mt-5 flex gap-3">
        <button class="rounded-lg bg-brand px-4 py-2 text-xs font-semibold text-white hover:bg-brand-dark" @click="toggleHistory">
          {{ showHistory ? '收起历史周报' : `查看历史周报${reports.length ? `（${reports.length} 篇）` : ''}` }}
        </button>
        <button
          class="rounded-lg border border-ink-faint/30 px-4 py-2 text-xs hover:border-brand hover:text-brand"
          title="在浏览器打开 Web 端"
          @click="openWebApp"
        >
          在 Web 端生成 ↗
        </button>
      </div>
      <div class="mt-3 text-[11px] text-ink-faint">历史周报只读 · 与 Web 端数据完全同步</div>
    </div>

    <!-- 历史周报（只读） -->
    <div v-if="showHistory" class="mt-4 space-y-2 pb-4">
      <div v-if="loading" class="space-y-2">
        <div v-for="i in 4" :key="i" class="h-14 animate-pulse rounded-xl bg-paper-card" />
      </div>
      <div v-else-if="error" class="rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700">
        {{ error }}
        <button class="ml-2 underline" @click="fetchReports">重试</button>
      </div>
      <template v-else>
        <div
          v-for="r in reports"
          :key="r.id"
          class="rounded-xl border border-ink-faint/20 bg-paper-card p-3.5"
          :class="expandedId === r.id ? 'border-brand/50' : ''"
        >
          <button class="flex w-full items-center gap-3 text-left" @click="openReport(r)">
            <b class="text-[13px]">{{ r.weekStart.slice(0, 7) }} · {{ fmtDate(r.weekStart) }} 起</b>
            <span class="text-[11px] text-ink-faint">{{ fmtDate(r.weekStart) }} ~ {{ fmtDate(r.weekEnd) }}</span>
            <span v-if="r.isEdited" class="rounded border border-ink-faint/25 px-1 text-[10px] text-ink-faint">已人工编辑</span>
            <span class="ml-auto text-[11px] text-ink-faint">{{ expandedId === r.id ? '收起 ▴' : '查看 ▾' }}</span>
          </button>
          <div v-if="expandedId === r.id" class="mt-2 border-t border-dashed border-ink-faint/20 pt-2">
            <MarkdownView :source="r.content" />
          </div>
        </div>
        <div v-if="!reports.length" class="rounded-xl border border-dashed border-ink-faint/30 p-8 text-center text-xs text-ink-faint">
          还没有历史周报
        </div>
      </template>
    </div>
  </div>
</template>
