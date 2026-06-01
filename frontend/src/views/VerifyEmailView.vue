<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const status = ref<'verifying' | 'success' | 'failed' | 'idle'>('idle')
const message = ref('')

// 重发表单
const resend = reactive({ email: '' })
const resendErrors = reactive<Record<string, string>>({})
const resendLoading = ref(false)
const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

onMounted(async () => {
  const token = route.query.token as string | undefined
  if (!token) {
    status.value = 'idle'
    return
  }
  status.value = 'verifying'
  try {
    await authApi.verifyEmail({ token })
    status.value = 'success'
    message.value = '邮箱验证成功，请前往登录'
  } catch (e: any) {
    status.value = 'failed'
    message.value = e?.message || '链接无效或已过期'
  }
})

async function onResend() {
  for (const k of Object.keys(resendErrors)) delete resendErrors[k]
  if (!emailRe.test(resend.email)) {
    resendErrors.email = '邮箱格式不正确'
    return
  }
  resendLoading.value = true
  try {
    await authApi.resendVerify({ email: resend.email })
    toast.success('验证邮件已发送，请查收')
  } catch {
    // toast
  } finally {
    resendLoading.value = false
  }
}
</script>

<template>
  <AuthCard title="邮箱验证" subtitle="完成验证以激活账号">
    <div v-if="status === 'verifying'" class="text-center py-6 text-gray-600">
      <div class="inline-block w-6 h-6 border-2 border-brand border-t-transparent rounded-full animate-spin"></div>
      <p class="mt-3 text-sm">验证中…</p>
    </div>

    <div v-else-if="status === 'success'" class="text-center py-4">
      <div class="inline-flex items-center justify-center w-12 h-12 rounded-full bg-green-100 text-green-600 text-2xl">✓</div>
      <p class="mt-3 text-sm text-gray-700">{{ message }}</p>
      <button class="btn-primary mt-5" @click="router.replace({ name: 'login' })">前往登录</button>
    </div>

    <div v-else-if="status === 'failed'" class="space-y-4">
      <div class="text-center py-2">
        <div class="inline-flex items-center justify-center w-12 h-12 rounded-full bg-red-100 text-red-600 text-2xl">✕</div>
        <p class="mt-3 text-sm text-gray-700">{{ message }}</p>
      </div>
      <form class="space-y-3" @submit.prevent="onResend">
        <FormField label="重新发送验证邮件" :error="resendErrors.email">
          <input v-model="resend.email" type="email" class="form-input" placeholder="you@example.com" />
        </FormField>
        <button type="submit" class="btn-primary" :disabled="resendLoading">
          <span v-if="resendLoading">发送中…</span>
          <span v-else>发送</span>
        </button>
      </form>
    </div>

    <div v-else class="space-y-4">
      <p class="text-sm text-gray-600">
        请在邮件中点击验证链接以完成验证。如未收到，可在下方重新发送。
      </p>
      <form class="space-y-3" @submit.prevent="onResend">
        <FormField label="邮箱" :error="resendErrors.email">
          <input v-model="resend.email" type="email" class="form-input" placeholder="you@example.com" />
        </FormField>
        <button type="submit" class="btn-primary" :disabled="resendLoading">
          <span v-if="resendLoading">发送中…</span>
          <span v-else>重新发送验证邮件</span>
        </button>
      </form>
      <p class="text-sm text-center text-gray-600">
        <router-link to="/login" class="text-brand hover:text-brand-dark">返回登录</router-link>
      </p>
    </div>
  </AuthCard>
</template>
