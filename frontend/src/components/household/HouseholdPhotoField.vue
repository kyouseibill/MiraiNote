<script setup lang="ts">
import { ref } from 'vue'
import { useHouseholdStore } from '@/stores/household'
import { useToast } from '@/composables/useToast'
import { staticUrl } from '@/composables/useStaticUrl'

const props = withDefaults(defineProps<{
  modelValue: string[]
  disabled?: boolean
}>(), {
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [string[]]
  uploading: [boolean]
}>()

const store = useHouseholdStore()
const toast = useToast()
const uploading = ref(false)
const maxCount = 9
const maxBytes = 5 * 1024 * 1024

async function onPick(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  if (files.length === 0 || props.disabled) return

  const remaining = maxCount - props.modelValue.length
  if (files.length > remaining) {
    toast.error(`最多上传 ${maxCount} 张图片，还可选择 ${remaining} 张`)
    return
  }
  const invalid = files.find((file) => !file.type.startsWith('image/'))
  if (invalid) {
    toast.error(`「${invalid.name}」不是有效的图片文件`)
    return
  }
  const oversized = files.find((file) => file.size > maxBytes)
  if (oversized) {
    toast.error(`「${oversized.name}」超过 5MB`)
    return
  }

  uploading.value = true
  emit('uploading', true)
  const uploaded: string[] = []
  try {
    for (const file of files) {
      try {
        uploaded.push(await store.uploadPhoto(file))
      } catch {
        // 单张失败时继续其余图片，错误由拦截器提示。
      }
    }
    if (uploaded.length) emit('update:modelValue', [...props.modelValue, ...uploaded])
  } finally {
    uploading.value = false
    emit('uploading', false)
  }
}

function remove(index: number) {
  emit('update:modelValue', props.modelValue.filter((_, current) => current !== index))
}

</script>

<template>
  <div>
    <div class="mb-2 flex items-center justify-between gap-3">
      <span class="text-[13px] font-medium text-[var(--mn-ink)]">照片</span>
      <span class="text-[11px] text-[var(--mn-muted)]">{{ modelValue.length }} / {{ maxCount }} 张 · 单张最大 5MB</span>
    </div>
    <input
      type="file"
      accept="image/*"
      multiple
      class="block text-[13px] text-[var(--mn-muted)]"
      :disabled="disabled || uploading || modelValue.length >= maxCount"
      @change="onPick"
    />
    <p v-if="uploading" class="mt-1 text-[11px] text-[var(--mn-muted)]">上传中…</p>
    <div v-if="modelValue.length" class="mt-3 grid grid-cols-3 gap-2">
      <div v-for="(path, index) in modelValue" :key="`${path}-${index}`" class="relative aspect-square">
        <img :src="staticUrl(path)" class="h-full w-full rounded-md border border-[var(--mn-line)] object-cover" :alt="`照片 ${index + 1}`" />
        <button
          type="button"
          class="absolute right-1.5 top-1.5 flex h-6 w-6 items-center justify-center rounded-full bg-[#262521]/70 text-[12px] text-white hover:bg-[#b4493f]"
          :aria-label="`移除第 ${index + 1} 张图片`"
          :disabled="disabled"
          @click="remove(index)"
        >
          ×
        </button>
      </div>
    </div>
  </div>
</template>
