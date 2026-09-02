<script setup lang="ts">
import type { Briefing } from '@/api/types'
import AiBadge from '@/components/AiBadge.vue'
import MarkdownView from '@/components/MarkdownView.vue'
import SourceChips from '@/components/SourceChips.vue'
import { fmtDateWithWeek, fmtTime } from '@/utils/format'

// 晨报卡 · 视觉稿 ①-3：Markdown 渲染 + 强制来源 chips + 每天一次可重生成
const props = defineProps<{
  briefing: Briefing | null
  briefingError: string | null
  date: string
  weekEntryCount: number
  regenerating: boolean
}>()

const emit = defineEmits<{ regenerate: [] }>()
</script>

<template>
  <section class="rounded-xl border border-ink-faint/20 bg-paper-card p-4 shadow-sm">
    <header class="flex flex-wrap items-center gap-2">
      <b>☀ 晨报</b>
      <AiBadge v-if="briefing" :label="`生成 ${fmtTime(briefing.generatedAt)}`" />
      <span class="ml-auto text-[11px] text-ink-faint">
        {{ fmtDateWithWeek(date) }} · 本周已记录 {{ weekEntryCount }} 条
      </span>
      <button
        class="ml-2 text-[11px] text-ink-sub hover:text-brand disabled:opacity-50"
        :disabled="regenerating"
        @click="emit('regenerate')"
      >
        {{ regenerating ? '生成中…' : '↻ 重新生成' }}
      </button>
    </header>

    <!-- 错误态：DeepSeek 不可用时友好提示，不影响纯数据区 -->
    <div
      v-if="briefingError && !briefing"
      class="mt-3 rounded-lg border border-warn-line bg-warn-soft p-3 text-xs leading-6 text-warn"
    >
      晨报生成失败：{{ briefingError }}
      <span class="text-ink-faint">（AI 暂时不可用不影响今日任务与动态，稍后可重新生成）</span>
    </div>

    <MarkdownView v-if="briefing" :source="briefing.content" class="mt-2" />

    <div v-if="briefing?.sources?.length" class="mt-3 border-t border-dashed border-ink-faint/20 pt-2">
      <SourceChips :sources="briefing.sources" prefix="来源：" />
    </div>
  </section>
</template>
