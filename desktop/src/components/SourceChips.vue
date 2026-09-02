<script setup lang="ts">
import { useRouter } from 'vue-router'

// 溯源 chips（PRD §6：溯源 chips 必须可点击跳转）。
// 用于晨报来源、到期卡 AI 上下文、指令面板「数据来源」折叠区。
export interface ChipSource {
  type: string
  id: number
  title: string
}

const props = defineProps<{
  sources: ChipSource[]
  /** 纯展示态（不可跳转的类型自动降级） */
  prefix?: string
}>()

const router = useRouter()

const TYPE_TEXT: Record<string, string> = {
  worklog: '工作记录',
  lifelog: '生活记录',
  memo: '任务',
  inbox: '收件箱',
  briefing: '晨报',
  chat: '聊天记录',
}

/** M1 可跳转类型；chat 尚无前台页面（M2 开放），渲染为禁用样式 */
function routeOf(s: ChipSource): string | null {
  switch (s.type) {
    case 'worklog':
      return `/work?id=${s.id}`
    case 'lifelog':
      return `/life?id=${s.id}`
    case 'memo':
      return `/tasks?id=${s.id}`
    case 'inbox':
      return `/inbox?id=${s.id}`
    case 'briefing':
      return '/'
    default:
      return null
  }
}

function jump(s: ChipSource) {
  const route = routeOf(s)
  if (route) router.push(route)
}
</script>

<template>
  <span v-if="sources.length" class="inline">
    <span v-if="prefix" class="text-[11px] text-ink-faint">{{ prefix }}</span>
    <component
      :is="routeOf(s) ? 'button' : 'span'"
      v-for="s in sources"
      :key="`${s.type}-${s.id}`"
      :type="routeOf(s) ? 'button' : undefined"
      class="mr-1 mb-1 inline-block rounded-full border px-2 py-0.5 align-middle text-[11px]"
      :class="routeOf(s)
        ? 'cursor-pointer border-brand-line bg-brand-soft text-brand hover:border-brand hover:underline'
        : 'cursor-default border-ink-faint/30 bg-paper text-ink-faint'"
      :title="routeOf(s) ? `${TYPE_TEXT[s.type] ?? s.type} #${s.id} · 点击查看` : `${TYPE_TEXT[s.type] ?? s.type}（M2 开放跳转）`"
      @click="jump(s)"
    >
      {{ s.title }}<span class="opacity-60"> #{{ s.id }}</span>
    </component>
  </span>
</template>
