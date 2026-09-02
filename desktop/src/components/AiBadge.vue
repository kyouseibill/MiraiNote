<script setup lang="ts">
// AI 产出角标（PRD §6：AI 产出一律带「AI」角标）
defineProps<{
  /** 角标补充文案，如「AI 生成 07:30」 */
  label?: string
}>()
</script>

<template>
  <span
    class="ml-1 inline-block rounded border border-ai-line bg-ai-soft px-1 align-[1px] text-[10px] text-ai"
    title="由 AI 生成，可能需人工核对"
  >
    {{ label ? `AI ${label}` : 'AI' }}
  </span>
</template>
