<script setup lang="ts">
import { computed } from 'vue'
import { renderMarkdown } from '@/composables/useMarkdown'

// Markdown 安全渲染容器（marked + DOMPurify）
const props = defineProps<{ source: string | null | undefined }>()

const html = computed(() => renderMarkdown(props.source))
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html — 内容经 DOMPurify 净化 -->
  <div class="md" v-html="html" />
</template>

<style scoped>
.md :deep(h1),
.md :deep(h2),
.md :deep(h3),
.md :deep(h4) {
  @apply mb-2 mt-3 text-sm font-bold text-ink;
}
.md :deep(h4:first-child),
.md :deep(p:first-child) {
  @apply mt-0;
}
.md :deep(p) {
  @apply mb-2 text-[13px] leading-7 text-ink;
}
.md :deep(ul),
.md :deep(ol) {
  @apply mb-2 list-disc pl-5 text-[12.5px] leading-7 text-ink;
}
.md :deep(code) {
  @apply rounded bg-paper px-1 py-0.5 text-[12px] text-ink-sub;
}
.md :deep(pre) {
  @apply overflow-x-auto rounded-lg bg-paper p-3 text-[12px];
}
.md :deep(blockquote) {
  @apply my-2 border-l-2 border-brand-line pl-3 text-xs text-ink-sub;
}
.md :deep(a) {
  @apply text-brand underline;
}
.md :deep(img) {
  @apply max-w-full rounded-lg;
}
.md :deep(hr) {
  @apply my-3 border-ink-faint/20;
}
.md :deep(table) {
  @apply my-2 w-full border-collapse text-xs;
}
.md :deep(th),
.md :deep(td) {
  @apply border border-ink-faint/20 px-2 py-1 text-left;
}
</style>
