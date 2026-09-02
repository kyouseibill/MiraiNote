<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { staticUrl } from '@/utils/url'

// 图片查看器 · 复用 frontend/src/views/life/LifeLogView.vue 的查看器逻辑
// （缩放/旋转/上一张下一张/缩略图/键盘操作），适配桌面端 M1 视觉。
const props = defineProps<{
  open: boolean
  images: string[]
  index: number
}>()

const emit = defineEmits<{
  'update:open': [v: boolean]
  'update:index': [v: number]
}>()

const scale = ref(1)
const rotation = ref(0)

const currentUrl = computed(() => {
  const path = props.images[props.index]
  return path ? staticUrl(path) : ''
})

watch(
  () => props.open,
  (v) => {
    if (v) {
      scale.value = 1
      rotation.value = 0
    }
  },
)

function close() {
  emit('update:open', false)
}

function select(i: number) {
  scale.value = 1
  rotation.value = 0
  emit('update:index', Math.min(Math.max(i, 0), props.images.length - 1))
}

function prev() {
  if (!props.images.length) return
  select((props.index - 1 + props.images.length) % props.images.length)
}

function next() {
  if (!props.images.length) return
  select((props.index + 1) % props.images.length)
}

function zoom(delta: number) {
  scale.value = Math.min(3, Math.max(0.5, Number((scale.value + delta).toFixed(2))))
}

function reset() {
  scale.value = 1
  rotation.value = 0
}

function rotate() {
  rotation.value = (rotation.value + 90) % 360
}

function onKey(e: KeyboardEvent) {
  if (!props.open) return
  if (e.key === 'Escape') close()
  else if (e.key === 'ArrowLeft') prev()
  else if (e.key === 'ArrowRight') next()
  else if (e.key === '+' || e.key === '=') zoom(0.25)
  else if (e.key === '-') zoom(-0.25)
  else if (e.key === '0') reset()
}

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-[80] flex items-center justify-center bg-black/85 p-2 backdrop-blur-[2px] sm:p-5"
      role="dialog"
      aria-modal="true"
      aria-label="生活照片查看器"
      @click.self="close"
    >
      <div class="relative flex h-[min(92vh,900px)] w-[min(94vw,1180px)] flex-col overflow-hidden rounded-lg border border-white/10 bg-[#242321] shadow-2xl">
        <div class="relative flex min-h-0 flex-1 items-center justify-center overflow-hidden px-4 pb-4 pt-16 sm:px-14 sm:pb-6 sm:pt-20">
          <!-- 工具条 -->
          <div class="absolute right-3 top-3 z-20 flex items-center overflow-hidden rounded-md border border-white/15 bg-black/60 text-xs text-white/75 shadow-lg">
            <button class="flex h-9 w-9 items-center justify-center border-r border-white/10 hover:bg-white/10 hover:text-white" title="适应屏幕" @click="reset">⤢</button>
            <button class="flex h-9 w-9 items-center justify-center border-r border-white/10 hover:bg-white/10 hover:text-white" title="缩小" @click="zoom(-0.25)">−</button>
            <span class="flex h-9 min-w-12 items-center justify-center border-r border-white/10 px-1 tabular-nums">{{ Math.round(scale * 100) }}%</span>
            <button class="flex h-9 w-9 items-center justify-center border-r border-white/10 hover:bg-white/10 hover:text-white" title="放大" @click="zoom(0.25)">＋</button>
            <button class="hidden h-9 w-9 items-center justify-center border-r border-white/10 hover:bg-white/10 hover:text-white sm:flex" title="顺时针旋转" @click="rotate">↻</button>
            <button class="flex h-9 w-9 items-center justify-center hover:bg-white/10 hover:text-white" title="关闭（Esc）" @click="close">✕</button>
          </div>

          <button
            v-if="images.length > 1"
            class="absolute left-2 top-1/2 z-10 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full border border-white/15 bg-black/30 text-lg text-white/70 hover:bg-white/10 hover:text-white"
            title="上一张（←）"
            @click="prev"
          >
            ‹
          </button>

          <img
            :src="currentUrl"
            :alt="`生活照片 ${index + 1}`"
            class="max-h-full max-w-full select-none object-contain transition-transform duration-200 ease-out"
            :style="{ transform: `scale(${scale}) rotate(${rotation}deg)` }"
            draggable="false"
          />

          <button
            v-if="images.length > 1"
            class="absolute right-2 top-1/2 z-10 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full border border-white/15 bg-black/30 text-lg text-white/70 hover:bg-white/10 hover:text-white"
            title="下一张（→）"
            @click="next"
          >
            ›
          </button>
        </div>

        <!-- 缩略图条 -->
        <div class="shrink-0 border-t border-white/10 bg-[#f8f6f0] px-4 py-3">
          <div v-if="images.length > 1" class="mx-auto flex max-w-full items-center justify-center gap-2 overflow-x-auto pb-1">
            <button
              v-for="(path, i) in images"
              :key="path + i"
              class="relative h-14 w-14 shrink-0 rounded-md border bg-white p-0.5 sm:h-16 sm:w-16"
              :class="i === index ? 'border-brand opacity-100' : 'border-[#ddd8cf] opacity-70 hover:opacity-100'"
              :aria-label="`查看第 ${i + 1} 张图片`"
              @click="select(i)"
            >
              <img :src="staticUrl(path)" :alt="`缩略图 ${i + 1}`" class="h-full w-full rounded object-cover" loading="lazy" />
              <span v-if="i === index" class="absolute -right-1 -top-1 h-2.5 w-2.5 rounded-full border-2 border-[#f8f6f0] bg-brand" />
            </button>
          </div>
          <p class="mt-2 text-center text-[11px] tabular-nums tracking-widest text-ink-faint">
            {{ index + 1 }} / {{ images.length }} · ←→ 切换 · +/− 缩放 · 0 复位 · Esc 关闭
          </p>
        </div>
      </div>
    </div>
  </Teleport>
</template>
