<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const toast = useToast()

const colorMap: Record<string, string> = {
  success: 'bg-emerald-50/95 text-emerald-900 border-emerald-200',
  error: 'bg-red-50/95 text-red-900 border-red-200',
  info: 'bg-sky-50/95 text-sky-900 border-sky-200',
  warning: 'bg-amber-50/95 text-amber-900 border-amber-200',
}
</script>

<template>
  <div class="fixed right-4 top-4 z-50 w-80 max-w-[90vw] space-y-2 sm:right-6 sm:top-6">
    <transition-group name="toast">
      <div
        v-for="t in toast.list"
        :key="t.id"
        class="flex items-start gap-3 rounded-xl border px-4 py-3.5 text-sm shadow-float backdrop-blur-xl"
        :class="colorMap[t.type]"
      >
        <span class="flex-1 break-words">{{ t.message }}</span>
        <button
          class="opacity-60 hover:opacity-100 text-xs"
          @click="toast.dismiss(t.id)"
        >
          ✕
        </button>
      </div>
    </transition-group>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: all 0.25s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateX(20px);
}
</style>
