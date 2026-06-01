<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'
import PasswordInput from '@/components/PasswordInput.vue'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const toast = useToast()

const form = reactive({
  usernameOrEmail: (route.query.username as string) || '',
  password: '',
  rememberMe: false,
})

const errors = reactive<Record<string, string>>({})
const loading = ref(false)

function validate(): boolean {
  for (const k of Object.keys(errors)) delete errors[k]
  if (!form.usernameOrEmail.trim()) errors.usernameOrEmail = '请输入用户名或邮箱'
  if (!form.password) errors.password = '请输入密码'
  return Object.keys(errors).length === 0
}

async function onSubmit() {
  if (!validate()) return
  loading.value = true
  try {
    await auth.login({
      usernameOrEmail: form.usernameOrEmail.trim(),
      password: form.password,
      rememberMe: form.rememberMe,
    })
    toast.success('登录成功')
    const redirect = (route.query.redirect as string) || '/'
    router.replace(redirect)
  } catch {
    // 错误已由 axios 拦截器 toast
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthCard title="登录未来ノート" subtitle="使用账号登录以继续">
    <form class="space-y-4" @submit.prevent="onSubmit">
      <FormField label="用户名或邮箱" :error="errors.usernameOrEmail">
        <input
          v-model="form.usernameOrEmail"
          type="text"
          autocomplete="username"
          class="form-input"
          placeholder="admin 或 you@example.com"
        />
      </FormField>

      <FormField label="密码" :error="errors.password">
        <PasswordInput v-model="form.password" autocomplete="current-password" />
      </FormField>

      <div class="flex items-center justify-between text-sm">
        <label class="flex items-center gap-2 cursor-pointer select-none">
          <input v-model="form.rememberMe" type="checkbox" class="rounded border-gray-300 text-brand focus:ring-brand" />
          <span class="text-gray-600">记住我（30 天）</span>
        </label>
        <router-link to="/forgot-password" class="text-brand hover:text-brand-dark">
          忘记密码？
        </router-link>
      </div>

      <button type="submit" class="btn-primary" :disabled="loading">
        <span v-if="loading">登录中…</span>
        <span v-else>登录</span>
      </button>

      <p class="text-sm text-center text-gray-600">
        还没有账号？
        <router-link to="/register" class="text-brand hover:text-brand-dark font-medium">立即注册</router-link>
      </p>
    </form>
  </AuthCard>
</template>
