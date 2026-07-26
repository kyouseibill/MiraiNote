<script setup lang="ts">
import { computed } from 'vue'
import { useReminderStore } from '@/stores/reminder'

const store = useReminderStore()

const visible = computed(() => store.queue.length > 0)
const current = computed(() => store.queue[0])

function fmt(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function ok() {
  if (!current.value) return
  await store.acknowledge(current.value.id)
}
</script>

<template>
  <Teleport to="body">
    <transition name="fade">
      <div
        v-if="visible && current"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm px-4"
      >
        <div class="w-full max-w-md overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-float">
          <div
            class="px-5 py-3 text-white font-semibold flex items-center gap-2"
            :class="current.section === 'life' ? 'bg-rose-500' : 'bg-slate-900'"
          >
            <span>🔔</span>
            <span>{{ current.section === 'life' ? '生活备忘提醒' : '工作备忘提醒' }}</span>
            <span class="ml-auto text-xs font-normal opacity-80">
              还有 {{ store.queue.length }} 条
            </span>
          </div>
          <div class="p-5">
            <p class="text-xs text-gray-500">提醒时间</p>
            <p class="text-sm font-medium text-gray-900 mt-0.5">{{ fmt(current.remindAt) }}</p>
            <div class="mt-3 p-3 rounded-lg bg-gray-50 text-sm text-gray-800 whitespace-pre-wrap break-words max-h-60 overflow-y-auto">
              {{ current.content }}
            </div>
          </div>
          <div class="px-5 py-3 border-t border-gray-100 flex justify-end gap-2">
            <button
              class="h-9 px-4 rounded-md text-white text-sm"
              :class="current.section === 'life' ? 'bg-rose-500 hover:bg-rose-600' : 'bg-teal-600 hover:bg-teal-700'"
              @click="ok"
            >
              我知道了
            </button>
          </div>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
