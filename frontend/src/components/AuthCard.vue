<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="min-h-screen flex items-center justify-center px-4 py-12 bg-gradient-to-br from-indigo-50 via-white to-purple-50">
    <div class="w-full max-w-md">
      <div class="text-center mb-6">
        <div class="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-brand text-white text-xl font-bold shadow-md">
          M
        </div>
        <h1 class="mt-3 text-2xl font-bold text-gray-900">{{ title }}</h1>
        <p v-if="subtitle" class="mt-1 text-sm text-gray-500">{{ subtitle }}</p>
      </div>
      <div class="bg-white rounded-2xl shadow-xl border border-gray-100 px-6 sm:px-8 py-8">
        <slot />
      </div>
      <p class="mt-6 text-xs text-center text-gray-400">© {{ new Date().getFullYear() }} 未来ノート</p>
    </div>
  </div>
</template>
