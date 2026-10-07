<script setup lang="ts">
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="mx-auto flex min-h-[60vh] w-full max-w-[720px] flex-col justify-center px-6 py-16">
    <p class="text-[11px] tracking-[0.16em] text-[#9a958d]">生活</p>
    <h1 class="mt-3 font-serif text-[28px] font-medium tracking-[0.04em] text-[#262521]">家务周期已关闭</h1>
    <p class="mt-4 max-w-[36em] text-[14px] leading-7 text-[#625f59]">
      这个入口暂时不用。已经记下的家务数据仍保留，现在不能从这里查看或修改。
    </p>
    <button
      type="button"
      class="mt-8 inline-flex h-10 w-fit items-center rounded-[5px] bg-[#4c6178] px-4 text-[13px] text-white hover:bg-[#384b60]"
      @click="router.push('/dashboard')"
    >
      返回工作台
    </button>
  </div>
</template>
