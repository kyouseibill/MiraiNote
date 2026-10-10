<script setup lang="ts">
defineProps<{
  /** 设置页上表单就在下面，不再放「去填写」。 */
  settings?: boolean
}>()

const emit = defineEmits<{
  skip: []
}>()
</script>

<template>
  <div
    data-testid="region-prompt"
    class="flex flex-wrap items-center justify-between gap-3 rounded-md border border-[#e3d3c4] bg-[#f7f1ea] px-4 py-3"
    role="status"
  >
    <p class="text-[13px] leading-6 text-[#4a4945]">
      请填写所在地区。填写后，工作台会显示当地天气。跳过也可以继续使用，下次打开工作台还会再提醒。
    </p>
    <div class="flex items-center gap-2">
      <router-link
        v-if="!settings"
        to="/profile"
        class="text-[13px] text-[#4c6178] hover:underline"
        data-testid="region-prompt-fill"
      >
        去填写
      </router-link>
      <button
        type="button"
        class="h-8 rounded-md border border-[#ddd8cf] px-3 text-[12px] text-[#716c65] hover:border-[#c7a59f]"
        data-testid="region-prompt-skip"
        @click="emit('skip')"
      >
        稍后填写
      </button>
    </div>
  </div>
</template>
