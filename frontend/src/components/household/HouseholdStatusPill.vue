<script setup lang="ts">
import type { ItemStatusTone } from '@/utils/householdFormat'

defineProps<{
  label: string
  tone: ItemStatusTone
}>()

const toneClass: Record<ItemStatusTone, string> = {
  overdue: 'text-[#b4493f]',
  soon: 'text-[#4c6178]',
  ok: 'text-[#7f7a72]',
  paused: 'text-[#7f7a72]',
  archived: 'text-[#7f7a72]',
  none: 'text-[#7f7a72]',
}

const dotClass: Record<ItemStatusTone, string> = {
  overdue: 'bg-[#b4493f]',
  soon: 'bg-[#4c6178]',
  ok: 'bg-[#c8c2b8]',
  paused: 'bg-[#c8c2b8]',
  archived: 'bg-[#c8c2b8]',
  none: 'bg-[#c8c2b8]',
}
</script>

<template>
  <span class="inline-flex items-center gap-1.5 text-[11px]" :class="toneClass[tone]">
    <span class="h-1.5 w-1.5 rounded-full" :class="dotClass[tone]" />
    {{ label }}
  </span>
</template>
