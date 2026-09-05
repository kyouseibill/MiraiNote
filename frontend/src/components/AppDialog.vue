<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { IconX } from '@tabler/icons-vue'

const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    description?: string
    size?: 'sm' | 'lg' | 'drawer' | 'navigation'
    busy?: boolean
  }>(),
  {
    description: '',
    size: 'sm',
    busy: false,
  },
)

const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const heading = ref<HTMLHeadingElement | null>(null)
const id = useId()
const titleId = `${id}-title`
const descriptionId = `${id}-description`

let returnFocusTo: HTMLElement | null = null
let closeRequested = false
let pointerStartedOnBackdrop = false
let disposed = false

function restoreFocus() {
  const target = returnFocusTo
  returnFocusTo = null
  if (target?.isConnected) target.focus({ preventScroll: true })
}

async function syncOpenState() {
  await nextTick()
  const element = dialog.value
  if (disposed || !element) return

  if (!props.open) {
    closeRequested = false
    pointerStartedOnBackdrop = false
    if (element.open) element.close()
    restoreFocus()
    return
  }

  if (element.open) return
  closeRequested = false
  returnFocusTo ??= document.activeElement instanceof HTMLElement ? document.activeElement : null
  element.showModal()

  await nextTick()
  if (disposed || !props.open || !element.open) return
  const preferredTarget = element.querySelector<HTMLElement>(
    '[data-dialog-autofocus]:not(:disabled):not([inert])',
  )
  ;(preferredTarget ?? heading.value)?.focus({ preventScroll: true })
}

function requestClose() {
  if (props.busy || !props.open || closeRequested) return
  closeRequested = true
  emit('close')
  // Keep repeated cancel/close events from emitting twice in the same update.
  void nextTick(() => {
    closeRequested = false
  })
}

function isBackdropEvent(event: MouseEvent | PointerEvent) {
  const element = dialog.value
  if (!element || event.target !== element) return false
  const bounds = element.getBoundingClientRect()
  return (
    event.clientX < bounds.left ||
    event.clientX > bounds.right ||
    event.clientY < bounds.top ||
    event.clientY > bounds.bottom
  )
}

function onPointerDown(event: PointerEvent) {
  pointerStartedOnBackdrop = isBackdropEvent(event)
}

function onBackdropClick(event: MouseEvent) {
  const shouldClose = pointerStartedOnBackdrop && isBackdropEvent(event)
  pointerStartedOnBackdrop = false
  if (shouldClose) requestClose()
}

function onNativeClose() {
  if (disposed || dialog.value?.open) return
  if (props.open && props.busy) {
    void syncOpenState()
    return
  }
  restoreFocus()
  if (props.open) requestClose()
}

watch(() => props.open, syncOpenState, { flush: 'post' })
onMounted(syncOpenState)
onBeforeUnmount(() => {
  disposed = true
  if (dialog.value?.open) dialog.value.close()
  restoreFocus()
})
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="app-dialog"
      :class="`app-dialog--${size}`"
      :aria-labelledby="titleId"
      :aria-describedby="description ? descriptionId : undefined"
      :aria-busy="busy || undefined"
      @cancel.prevent="requestClose"
      @close="onNativeClose"
      @pointerdown="onPointerDown"
      @pointercancel="pointerStartedOnBackdrop = false"
      @click="onBackdropClick"
    >
      <header class="app-dialog__header">
        <div class="app-dialog__heading">
          <h2 :id="titleId" ref="heading" class="app-dialog__title" tabindex="-1">{{ title }}</h2>
          <p v-if="description" :id="descriptionId" class="app-dialog__description">{{ description }}</p>
        </div>
        <button
          type="button"
          class="app-dialog__close"
          aria-label="关闭对话框"
          title="关闭对话框"
          :disabled="busy"
          @click="requestClose"
        >
          <IconX :size="19" :stroke-width="1.7" aria-hidden="true" />
        </button>
      </header>

      <div class="app-dialog__body">
        <slot />
      </div>

      <footer v-if="$slots.footer" class="app-dialog__footer">
        <slot name="footer" />
      </footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.app-dialog {
  position: fixed;
  inset: 0;
  width: min(460px, calc(100% - 32px));
  max-width: none;
  max-height: calc(100dvh - 32px - env(safe-area-inset-top) - env(safe-area-inset-bottom));
  margin: auto;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--mn-line);
  border-radius: 14px;
  background: var(--mn-paper-light);
  color: var(--mn-ink);
  box-shadow: 0 24px 80px rgb(38 37 33 / 18%);
}

.app-dialog[open] {
  display: flex;
  flex-direction: column;
}

.app-dialog::backdrop {
  background: rgb(38 37 33 / 32%);
}

.app-dialog--lg {
  width: min(760px, calc(100% - 32px));
}

.app-dialog--drawer {
  inset: 0 0 0 auto;
  width: min(380px, calc(100% - 16px));
  height: 100dvh;
  max-height: 100dvh;
  margin: 0;
  border-radius: 14px 0 0 14px;
  padding-top: env(safe-area-inset-top);
  padding-right: env(safe-area-inset-right);
  padding-bottom: env(safe-area-inset-bottom);
}

.app-dialog--navigation {
  inset: 0 auto 0 0;
  width: min(304px, 86vw);
  height: 100dvh;
  max-height: 100dvh;
  margin: 0;
  border-radius: 0 12px 12px 0;
  background: var(--mn-paper);
}

.app-dialog--navigation .app-dialog__header {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip-path: inset(50%);
}

.app-dialog--navigation .app-dialog__header button {
  display: none;
}
.app-dialog--navigation .app-dialog__body {
  display: flex;
  padding: 0;
  overflow: hidden;
  scrollbar-gutter: auto;
}

.app-dialog__header {
  display: flex;
  flex: none;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
  padding: 22px 24px 18px;
  border-bottom: 1px solid var(--mn-line);
}

.app-dialog__heading {
  min-width: 0;
}

.app-dialog__title {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  line-height: 1.6;
  overflow-wrap: anywhere;
  outline: none;
}

.app-dialog__description {
  margin: 7px 0 0;
  color: var(--mn-muted);
  font-size: 12px;
  line-height: 1.7;
  overflow-wrap: anywhere;
}

.app-dialog__close {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  margin: -4px -6px 0 0;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  color: var(--mn-indigo);
  cursor: pointer;
  transition:
    background 150ms ease,
    border-color 150ms ease;
}

.app-dialog__close:hover:not(:disabled) {
  border-color: var(--mn-line);
  background: var(--mn-paper);
}

.app-dialog__close:active:not(:disabled) {
  background: var(--mn-line);
}

.app-dialog__close:focus-visible {
  outline: 2px solid var(--mn-indigo);
  outline-offset: 2px;
}

.app-dialog__close:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.app-dialog__body {
  flex: 1 1 auto;
  min-height: 0;
  padding: 22px 24px;
  overflow: auto;
  overscroll-behavior: contain;
  overflow-wrap: anywhere;
  scrollbar-gutter: stable;
}

.app-dialog__footer {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
  padding: 16px 24px;
  border-top: 1px solid var(--mn-line);
}

:global(body:has(dialog.app-dialog:modal)) {
  overflow: hidden;
}

@media (max-width: 480px) {
  .app-dialog__header {
    gap: 12px;
    padding: 18px 18px 16px;
  }

  .app-dialog__body {
    padding: 18px;
  }

  .app-dialog__footer {
    padding: 14px 18px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .app-dialog__close {
    transition: none;
  }
}
</style>
