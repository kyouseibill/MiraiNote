<script setup lang="ts">
// 通用二次确认弹窗（PRD §6：写操作确认；指令面板 Confirm 事件复用此壳）
const props = withDefaults(
  defineProps<{
    open: boolean
    title?: string
    confirmText?: string
    cancelText?: string
    danger?: boolean
    busy?: boolean
  }>(),
  { title: '确认操作', confirmText: '确认', cancelText: '取消', danger: false, busy: false },
)

const emit = defineEmits<{ confirm: []; cancel: [] }>()

function onKeydown(e: KeyboardEvent) {
  if (!props.open) return
  if (e.key === 'Escape') {
    e.stopPropagation()
    emit('cancel')
  }
  if (e.key === 'Enter' && !props.busy) emit('confirm')
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-[70] flex items-center justify-center bg-black/40 p-4"
      @keydown="onKeydown"
      @click.self="emit('cancel')"
    >
      <div class="w-[420px] overflow-hidden rounded-xl bg-white shadow-2xl" role="dialog" aria-modal="true">
        <div class="px-5 pt-4 pb-1 text-sm font-bold" :class="danger ? 'text-red-600' : 'text-ink'">
          {{ danger ? '⚠ ' : '' }}{{ title }}
        </div>
        <div class="px-5 pb-4 pt-2 text-xs leading-6 text-ink-sub">
          <slot />
        </div>
        <div class="flex justify-end gap-2 border-t border-ink-faint/10 bg-paper/60 px-5 py-3">
          <button
            class="rounded-lg border border-ink-faint/30 bg-white px-3 py-1.5 text-xs hover:border-ink-sub"
            @click="emit('cancel')"
          >
            {{ cancelText }}
          </button>
          <button
            class="rounded-lg px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50"
            :class="danger ? 'bg-red-600 hover:bg-red-700' : 'bg-brand hover:bg-brand-dark'"
            :disabled="busy"
            @click="emit('confirm')"
          >
            {{ busy ? '处理中…' : confirmText }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
