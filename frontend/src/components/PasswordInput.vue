<script setup lang="ts">
import { ref, nextTick } from 'vue'

defineProps<{
  modelValue: string
  placeholder?: string
  autocomplete?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  blur: []
}>()

const show = ref(false)
const inputRef = ref<HTMLInputElement | null>(null)

// 使用 v-if/v-else 替代修改 type 属性。
// 修改 type 时 Vue diff 可能跳过 value patch（因为 prop 未变），导致 DOM 与状态脱节。
// v-if/v-else 会挂载全新元素，Vue mount 时必然写入 :value，从根本消除脱节问题。
async function toggleShow() {
  show.value = !show.value
  await nextTick()
  inputRef.value?.focus()
}
</script>

<template>
  <div class="relative">
    <!-- 用 v-if/v-else 而非修改 type 属性，确保每次切换都是全新 DOM，:value 必然被写入 -->
    <input
      v-if="!show"
      ref="inputRef"
      type="password"
      :value="modelValue"
      :placeholder="placeholder ?? '••••••••'"
      :autocomplete="autocomplete ?? 'current-password'"
      class="form-input pr-10"
      @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      @blur="emit('blur')"
    />
    <input
      v-else
      ref="inputRef"
      type="text"
      :value="modelValue"
      :placeholder="placeholder ?? '••••••••'"
      autocomplete="off"
      class="form-input pr-10"
      @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      @blur="emit('blur')"
    />
    <button
      type="button"
      tabindex="-1"
      class="absolute inset-y-0 right-0 flex items-center px-3 text-gray-400 hover:text-gray-600 focus:outline-none"
      :aria-label="show ? '隐藏密码' : '显示密码'"
      @mousedown.prevent
      @click="toggleShow"
    >
      <!-- 眼睛开 -->
      <svg v-if="!show" xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
        <path stroke-linecap="round" stroke-linejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.477 0 8.268 2.943 9.542 7-1.274 4.057-5.065 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
      </svg>
      <!-- 眼睛关 -->
      <svg v-else xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.477 0-8.268-2.943-9.542-7a9.956 9.956 0 012.293-3.95M6.32 6.32A9.956 9.956 0 0112 5c4.477 0 8.268 2.943 9.542 7a9.97 9.97 0 01-4.417 5.457M3 3l18 18" />
      </svg>
    </button>
  </div>
</template>
