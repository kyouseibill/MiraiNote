<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const username = ref('')
const password = ref('')
const error = ref('')

async function submit() {
  error.value = ''
  try {
    await auth.login(username.value, password.value)
    router.push({ name: 'today' })
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  }
}
</script>

<template>
  <div class="flex h-full items-center justify-center bg-paper">
    <form
      class="w-96 rounded-xl border border-ink-faint/20 bg-paper-card p-8 shadow-lg"
      @submit.prevent="submit"
    >
      <div class="mb-6 text-center">
        <div class="text-2xl font-bold text-brand">◈ Mirai</div>
        <div class="mt-1 text-xs text-ink-faint">MiraiNote 桌面版 · M1 骨架</div>
      </div>
      <label class="mb-1 block text-xs text-ink-sub">用户名</label>
      <input
        v-model="username"
        class="mb-4 w-full rounded-lg border border-ink-faint/30 px-3 py-2 outline-none focus:border-brand"
        autocomplete="username"
      />
      <label class="mb-1 block text-xs text-ink-sub">密码</label>
      <input
        v-model="password"
        type="password"
        class="mb-2 w-full rounded-lg border border-ink-faint/30 px-3 py-2 outline-none focus:border-brand"
        autocomplete="current-password"
      />
      <div v-if="error" class="mb-3 text-xs text-red-600">{{ error }}</div>
      <button
        type="submit"
        class="mt-2 w-full rounded-lg bg-brand py-2 font-semibold text-white hover:bg-brand-dark disabled:opacity-50"
        :disabled="auth.pending || !username || !password"
      >
        {{ auth.pending ? '登录中…' : '登录' }}
      </button>
      <div class="mt-4 text-center text-[11px] text-ink-faint">使用现有 MiraiNote 账号</div>
    </form>
  </div>
</template>
