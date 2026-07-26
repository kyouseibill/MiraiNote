<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="auth-canvas min-h-screen px-4 py-8 sm:px-6 lg:flex lg:items-center lg:justify-center lg:py-12">
    <div class="mx-auto grid w-full max-w-5xl overflow-hidden rounded-[28px] border border-slate-200/80 bg-white shadow-float md:grid-cols-[0.9fr_1.1fr]">
      <div class="relative hidden min-h-[620px] overflow-hidden bg-slate-950 p-10 text-white md:flex md:flex-col">
        <div class="absolute -right-24 -top-24 h-72 w-72 rounded-full bg-teal-400/15 blur-3xl" />
        <div class="absolute -bottom-24 -left-24 h-72 w-72 rounded-full bg-sky-400/10 blur-3xl" />

        <div class="relative flex items-center gap-3">
          <div class="flex h-10 w-10 items-center justify-center rounded-xl bg-teal-500 font-bold shadow-lg shadow-teal-950/40">M</div>
          <div>
            <p class="text-sm font-semibold tracking-wide">未来ノート</p>
            <p class="text-[10px] uppercase tracking-[0.2em] text-slate-500">Mirai workspace</p>
          </div>
        </div>

        <div class="relative my-auto max-w-sm">
          <p class="text-xs font-semibold uppercase tracking-[0.22em] text-teal-300">Focus · Capture · Create</p>
          <h2 class="mt-5 text-4xl font-semibold leading-tight tracking-tight">让工作与生活，<br>都有清晰的脉络。</h2>
          <p class="mt-5 text-sm leading-7 text-slate-400">记录、备忘、周报与智能协作汇聚于一处，减少切换，把注意力留给真正重要的事。</p>
        </div>

        <p class="relative text-xs text-slate-600">A calm space for focused progress.</p>
      </div>

      <div class="flex min-h-[620px] flex-col justify-center px-6 py-10 sm:px-12 lg:px-16">
        <div class="mb-8 md:hidden">
          <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-brand text-lg font-bold text-white shadow-md">
            M
          </div>
        </div>
        <div class="mb-8">
          <p class="section-kicker">Welcome</p>
          <h1 class="mt-2 text-3xl font-semibold tracking-tight text-slate-900">{{ title }}</h1>
          <p v-if="subtitle" class="mt-2 text-sm leading-6 text-slate-500">{{ subtitle }}</p>
        </div>
        <slot />
        <p class="mt-10 text-xs text-slate-400">© {{ new Date().getFullYear() }} 未来ノート · 保持专注，持续积累</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auth-canvas {
  background:
    radial-gradient(circle at 12% 8%, rgba(13, 148, 136, 0.1), transparent 28rem),
    radial-gradient(circle at 92% 88%, rgba(14, 116, 144, 0.08), transparent 26rem),
    #f1f5f4;
}
</style>
