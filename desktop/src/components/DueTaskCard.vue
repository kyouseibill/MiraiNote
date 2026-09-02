<script setup lang="ts">
import type { DueTask } from '@/api/types'
import { miraiApi } from '@/api/mirai'
import AiBadge from '@/components/AiBadge.vue'
import SourceChips from '@/components/SourceChips.vue'
import { fmtDue, overdueDays, PRIORITY_TEXT } from '@/utils/format'

// 到期/逾期任务卡 · 视觉稿 ①-4：附 AI 关联上下文（客户端从关联记录提取；无数据则隐藏）
const props = withDefaults(defineProps<{ task: DueTask; overdue?: boolean }>(), { overdue: false })

const emit = defineEmits<{ done: [task: DueTask]; later: [task: DueTask] }>()

// 客户端 AI 上下文（mock：任务↔关联记录；真实模式无此数据 → 行隐藏）
const ctx = miraiApi.taskContextOf(props.task.id)
</script>

<template>
  <section
    class="rounded-xl border border-ink-faint/20 border-l-[3px] bg-paper-card p-4 shadow-sm"
    :class="overdue ? 'border-l-red-500' : 'border-l-warn'"
  >
    <div class="flex items-start gap-3">
      <span>{{ overdue ? '⛔' : '⚑' }}</span>
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center gap-2">
          <b class="truncate">{{ task.content }}</b>
          <span v-if="task.isPinned" title="已置顶">📌</span>
        </div>
        <div class="mt-0.5 text-[11px]" :class="overdue ? 'text-red-600' : 'text-ink-faint'">
          <template v-if="overdue">已逾期 {{ task.remindAt ? overdueDays(task.remindAt) : 1 }} 天 ·</template>
          <template v-else>{{ fmtDue(task.remindAt) }} 到期 ·</template>
          优先级 {{ PRIORITY_TEXT[task.priority] ?? task.priority }} ·
          {{ task.section === 'work' ? '工作' : '生活' }}
        </div>
        <div v-if="ctx" class="mt-1.5 flex flex-wrap items-center gap-1 text-[12px] text-ink-sub">
          <AiBadge label="上下文" />
          <span class="mr-1">{{ ctx.text }}</span>
          <SourceChips :sources="[{ type: 'worklog', id: ctx.worklogId, title: '关联记录' }]" />
        </div>
      </div>
      <div class="flex shrink-0 gap-2">
        <button class="rounded-lg bg-brand px-3 py-1 text-xs text-white hover:bg-brand-dark" @click="emit('done', task)">
          完成
        </button>
        <button
          class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs hover:border-brand hover:text-brand"
          @click="emit('later', task)"
        >
          稍后
        </button>
      </div>
    </div>
  </section>
</template>
