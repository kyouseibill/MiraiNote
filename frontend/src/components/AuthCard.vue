<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="min-h-screen bg-[#f6f3ec] px-5 py-8 sm:px-8 lg:flex lg:items-center lg:justify-center lg:py-12">
    <div class="mx-auto grid w-full max-w-[980px] overflow-hidden rounded-lg border border-[#dcd7ce] bg-[#fcfbf8] md:grid-cols-[0.82fr_1.18fr]">
      <div class="hidden min-h-[610px] border-r border-[#ddd8cf] bg-[#f1eee8] p-10 md:flex md:flex-col">
        <div class="flex items-center gap-3">
          <img src="/favicon.svg" alt="未来ノート" class="h-11 w-11 rounded-[10px]" />
          <div>
            <p class="font-serif text-[16px] font-medium tracking-[0.08em] text-[#262521]">未来ノート</p>
            <p class="mt-0.5 text-[9px] uppercase tracking-[0.2em] text-[#99938b]">Mirai workspace</p>
          </div>
        </div>

        <div class="my-auto max-w-[310px]">
          <div class="flex items-center gap-3 font-serif text-[13px] text-[#6f6a63]">
            <span class="h-[8px] w-[8px] rounded-full bg-[#b4493f]" />
            <span>Focus · Capture · Create</span>
          </div>
          <h2 class="mt-7 font-serif text-[28px] font-medium leading-[1.55] tracking-[0.04em] text-[#292824]">
            让工作与生活，<br />都有清晰的脉络。
          </h2>
          <p class="mt-6 text-[12px] leading-7 text-[#777169]">
            记录、备忘、周报与智能协作汇聚于一处，把注意力留给真正重要的事。
          </p>
        </div>

        <p class="text-[10px] tracking-[0.08em] text-[#a7a198]">A calm space for focused progress.</p>
      </div>

      <div class="flex min-h-[610px] flex-col justify-center px-7 py-10 sm:px-14 lg:px-20">
        <div class="mb-8 md:hidden">
          <img src="/favicon.svg" alt="未来ノート" class="h-11 w-11 rounded-[10px]" />
        </div>
        <div class="mb-9">
          <div class="flex items-center gap-2.5 text-[10px] uppercase tracking-[0.18em] text-[#4c6178]">
            <span class="h-[6px] w-[6px] rounded-full bg-[#b4493f]" />
            <span>Welcome</span>
          </div>
          <h1 class="mt-3 font-serif text-[25px] font-medium tracking-[0.04em] text-[#292824]">{{ title }}</h1>
          <p v-if="subtitle" class="mt-2 text-[12px] leading-6 text-[#817b73]">{{ subtitle }}</p>
        </div>
        <slot />
        <p class="mt-11 text-[10px] text-[#aaa49c]">© {{ new Date().getFullYear() }} 未来ノート · 保持专注，持续积累</p>
      </div>
    </div>
  </div>
</template>
