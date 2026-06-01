<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'
import PasswordInput from '@/components/PasswordInput.vue'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const token = computed(() => (route.query.token as string) || '')

const form = reactive({
  newPassword: '',
  confirmPassword: '',
})
const errors = reactive<Record<string, string>>({})
const loading = ref(false)
const done = ref(false)
const passwordRe = /^(?=.*[A-Za-z])(?=.*\d).{8,32}$/

function validate(): boolean {
  for (const k of Object.keys(errors)) delete errors[k]
  if (!token.value) errors.token = '缺少重置令牌，请重新通过邮箱链接进入'
  if (!passwordRe.test(form.newPassword)) errors.newPassword = '密码 8-32 位，须包含字母与数字'
  if (form.confirmPassword !== form.newPassword) errors.confirmPassword = '两次密码不一致'
  return Object.keys(errors).length === 0
}

async function onSubmit() {
  if (!validate()) return
  loading.value = true
  try {
    await authApi.resetPassword({ token: token.value, newPassword: form.newPassword })
    done.value = true
    toast.success('密码已重置，请重新登录')
    setTimeout(() => router.replace({ name: 'login' }), 1200)
  } catch {
    // toast
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthCard title="重置密码" subtitle="设置新的登录密码">
    <div v-if="done" class="text-center py-4">
      <div class="inline-flex items-center justify-center w-12 h-12 rounded-full bg-green-100 text-green-600 text-2xl">✓</div>
      <p class="mt-3 text-sm text-gray-700">密码已重置，正在跳转登录…</p>
    </div>

    <form v-else class="space-y-4" @submit.prevent="onSubmit">
      <div v-if="errors.token" class="rounded-md bg-red-50 border border-red-200 text-red-700 text-xs px-3 py-2">
        {{ errors.token }}
      </div>

      <FormField label="新密码" :error="errors.newPassword" hint="8-32 位，须包含字母与数字">
        <PasswordInput v-model="form.newPassword" autocomplete="new-password" />
      </FormField>

      <FormField label="确认新密码" :error="errors.confirmPassword">
        <PasswordInput v-model="form.confirmPassword" autocomplete="new-password" />
      </FormField>

      <button type="submit" class="btn-primary" :disabled="loading || !token">
        <span v-if="loading">提交中…</span>
        <span v-else>重置密码</span>
      </button>

      <p class="text-sm text-center text-gray-600">
        <router-link to="/login" class="text-brand hover:text-brand-dark">返回登录</router-link>
      </p>
    </form>
  </AuthCard>
</template>
