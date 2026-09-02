<script setup lang="ts">
import { onUnmounted, ref } from 'vue'
import { miraiApi } from '@/api/mirai'
import { InboxSource, type InboxItem } from '@/api/types'

// 快速捕获条 · 视觉基准 docs/m1-ui-mockups.html ①-2 / ④（桌面内嵌版本）
// 全局热键悬浮窗由 SHELL 流实现（Tauri WebViewWindow）；本组件为应用内版本。
const text = ref('')
const busy = ref(false)
const bubble = ref<{ ok: boolean; text: string; item?: InboxItem } | null>(null)
let bubbleTimer: ReturnType<typeof setTimeout> | null = null

async function submit() {
  if (!text.value.trim() || busy.value) return
  busy.value = true
  bubble.value = null
  try {
    const item = await miraiApi.createInbox(text.value.trim(), InboxSource.TodayBar)
    if (item.status === 5) {
      bubble.value = { ok: false, text: `分拣失败：${item.error ?? '未知原因'}，已存入收件箱，可重试` }
    } else {
      const tasks = item.aiParse?.items.filter((s) => s.type === 'task').length ?? 0
      const notes = (item.aiParse?.items.length ?? 0) - tasks
      const parts = [tasks ? `${tasks} 条任务` : '', notes ? `${notes} 条记录草稿` : ''].filter(Boolean).join(' + ')
      bubble.value = { ok: true, text: `分拣完成：${parts || '未识别出对象'}`, item }
    }
    text.value = ''
    window.dispatchEvent(new Event('mirai:inbox-changed'))
    if (bubbleTimer) clearTimeout(bubbleTimer)
    bubbleTimer = setTimeout(() => (bubble.value = null), 6000)
  } catch (e) {
    bubble.value = { ok: false, text: e instanceof Error ? e.message : '捕获失败，请检查网络' }
  } finally {
    busy.value = false
  }
}

onUnmounted(() => {
  if (bubbleTimer) clearTimeout(bubbleTimer)
})
</script>

<template>
  <div>
    <form class="flex items-center gap-2 rounded-xl border border-brand-line bg-brand-soft px-4 py-2.5" @submit.prevent="submit">
      <span class="text-brand">✎</span>
      <input
        v-model="text"
        class="min-w-0 flex-1 bg-transparent text-ink outline-none placeholder:text-ink-faint"
        placeholder="随手丢一句话，助理帮你分拣…（任何应用内按 Ctrl+Shift+Space）"
        :disabled="busy"
      />
      <span v-if="busy" class="h-3.5 w-3.5 shrink-0 animate-spin rounded-full border-2 border-brand-line border-t-brand" />
      <button
        v-else
        type="submit"
        class="shrink-0 cursor-pointer rounded border border-brand-line bg-white px-1.5 py-0 text-[10px] text-brand hover:bg-brand-soft"
        title="提交并分拣（Enter）"
      >Enter</button>
    </form>
    <!-- 分拣结果轻气泡（视觉稿 ④：不打断，点击跳收件箱） -->
    <div
      v-if="bubble"
      class="mt-2 flex items-center gap-3 rounded-xl bg-white px-4 py-3 text-xs shadow-lg"
      :class="bubble.ok ? 'text-ink' : 'text-red-600'"
    >
      <span class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-sm" :class="bubble.ok ? 'bg-brand-soft text-brand' : 'bg-red-50 text-red-500'">
        {{ bubble.ok ? '✓' : '!' }}
      </span>
      <div class="min-w-0 flex-1">
        <b>{{ bubble.text }}</b>
        <div v-if="bubble.item?.aiParse?.uncertain.length" class="mt-0.5 text-[11px] text-warn">
          {{ bubble.item.aiParse.uncertain[0] }}
        </div>
      </div>
      <RouterLink to="/inbox" class="shrink-0 rounded-lg bg-brand px-3 py-1 text-white hover:bg-brand-dark">查看</RouterLink>
      <button class="shrink-0 text-ink-faint hover:text-ink-sub" @click="bubble = null">稍后</button>
    </div>
  </div>
</template>
