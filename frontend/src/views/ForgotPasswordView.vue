<script setup lang="ts">
import { reactive, ref } from 'vue'
import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'

const toast = useToast()

const form = reactive({ email: '' })
const errors = reactive<Record<string, string>>({})
const loading = ref(false)
const sent = ref(false)
const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

async function onSubmit() {
  for (const k of Object.keys(errors)) delete errors[k]
  if (!emailRe.test(form.email)) {
    errors.email = '邮箱格式不正确'
    return
  }
  loading.value = true
  try {
    await authApi.forgotPassword({ email: form.email })
    sent.value = true
    toast.success('若邮箱存在，重置链接已发送')
  } catch {
    // toast
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthCard title="忘记密码" subtitle="我们将向你的邮箱发送重置链接">
    <div v-if="sent" class="text-center py-4">
      <div class="inline-flex items-center justify-center w-12 h-12 rounded-full bg-green-100 text-green-600 text-2xl">✓</div>
      <p class="mt-3 text-sm text-gray-700">
        若 <span class="font-medium">{{ form.email }}</span> 已注册，重置链接已发送，请查收邮箱。
      </p>
      <router-link to="/login" class="btn-secondary mt-5 inline-flex">返回登录</router-link>
    </div>

    <form v-else class="space-y-4" @submit.prevent="onSubmit">
      <FormField label="注册邮箱" :error="errors.email">
        <input v-model="form.email" type="email" class="form-input" placeholder="you@example.com" />
      </FormField>

      <button type="submit" class="btn-primary" :disabled="loading">
        <span v-if="loading">发送中…</span>
        <span v-else>发送重置链接</span>
      </button>

      <p class="text-sm text-center text-gray-600">
        想起密码了？
        <router-link to="/login" class="text-brand hover:text-brand-dark font-medium">返回登录</router-link>
      </p>
    </form>
  </AuthCard>
</template>
