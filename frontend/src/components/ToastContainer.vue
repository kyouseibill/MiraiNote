<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const toast = useToast()

const colorMap: Record<string, string> = {
  success: 'bg-green-50 text-green-800 border-green-200',
  error: 'bg-red-50 text-red-800 border-red-200',
  info: 'bg-blue-50 text-blue-800 border-blue-200',
  warning: 'bg-yellow-50 text-yellow-800 border-yellow-200',
}
</script>

<template>
  <div class="fixed top-4 right-4 z-50 space-y-2 w-80 max-w-[90vw]">
    <transition-group name="toast">
      <div
        v-for="t in toast.list"
        :key="t.id"
        class="border rounded-md shadow-sm px-4 py-3 text-sm flex items-start gap-2"
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
