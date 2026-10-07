<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authApi } from '@/api/auth'
import { useResendCooldown } from '@/composables/useResendCooldown'
import { RESEND_VERIFY_MESSAGE, type VerifyEmailStatus } from '@/types/auth'
import { apiFailure } from '@/utils/apiError'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'

const route = useRoute()
const router = useRouter()

const phase = ref<'verifying' | 'idle' | VerifyEmailStatus>('idle')
const message = ref('')
const token = ref('')
const resendNotice = ref('')
const resendLoading = ref(false)
const { remaining, start: startCooldown } = useResendCooldown()

const resend = reactive({ email: '' })
const resendErrors = reactive<Record<string, string>>({})
const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

const statusCopy: Record<VerifyEmailStatus, string> = {
  verified: '验证成功',
  already_verified: '邮箱已验证，请直接登录',
  expired: '链接已过期',
  invalid: '链接无效',
}

onMounted(async () => {
  const queryToken = route.query.token as string | undefined
  if (!queryToken) {
    phase.value = 'idle'
    return
  }
  token.value = queryToken
  phase.value = 'verifying'
  try {
    const result = await authApi.verifyEmail({ token: queryToken })
    phase.value = result.status
    message.value = statusCopy[result.status] || statusCopy.invalid
  } catch (error: unknown) {
    phase.value = 'invalid'
    message.value = apiFailure(error, '链接无效').message
  }
})

async function onResendEmail() {
  for (const k of Object.keys(resendErrors)) delete resendErrors[k]
  if (!emailRe.test(resend.email)) {
    resendErrors.email = '邮箱格式不正确'
    return
  }
  resendLoading.value = true
  try {
    resendNotice.value = (await authApi.resendVerify({ email: resend.email.trim() })) || RESEND_VERIFY_MESSAGE
    startCooldown()
  } catch (error: unknown) {
    resendNotice.value = apiFailure(error).message
  } finally {
    resendLoading.value = false
  }
}

async function onResendToken() {
  resendLoading.value = true
  try {
    resendNotice.value = (await authApi.resendVerifyToken({ token: token.value })) || RESEND_VERIFY_MESSAGE
    startCooldown()
  } catch (error: unknown) {
    resendNotice.value = apiFailure(error).message
  } finally {
    resendLoading.value = false
  }
}

function goLogin() {
  router.replace({ name: 'login' })
}
</script>

<template>
  <AuthCard title="邮箱验证" subtitle="完成验证以激活账号">
    <div v-if="phase === 'verifying'" class="text-center py-6 text-gray-600">
      <div class="inline-block w-6 h-6 border-2 border-brand border-t-transparent rounded-full animate-spin"></div>
      <p class="mt-3 text-sm">验证中…</p>
    </div>

    <div v-else-if="phase === 'verified' || phase === 'already_verified'" class="text-center py-4">
      <div class="inline-flex items-center justify-center w-12 h-12 rounded-full bg-green-100 text-green-600 text-2xl">✓</div>
      <p class="mt-3 text-sm text-gray-700">{{ message }}</p>
      <button class="btn-primary mt-5" @click="goLogin">去登录</button>
    </div>

    <div v-else-if="phase === 'expired'" class="space-y-4 text-center">
      <p class="text-sm text-gray-700">{{ message }}</p>
      <button type="button" class="btn-primary" :disabled="resendLoading || remaining > 0" @click="onResendToken">
        <span v-if="resendLoading">发送中…</span>
        <span v-else-if="remaining > 0">{{ remaining }} 秒后可再次发送</span>
        <span v-else>重发验证邮件</span>
      </button>
      <p v-if="resendNotice" class="text-sm text-gray-600 text-left">{{ resendNotice }}</p>
    </div>

    <div v-else-if="phase === 'invalid'" class="text-center py-4 space-y-4">
      <p class="text-sm text-gray-700">{{ message }}</p>
      <button class="btn-primary" @click="goLogin">去登录</button>
    </div>

    <div v-else class="space-y-4">
      <p class="text-sm text-gray-600">
        请在邮件中点击验证链接以完成验证。如未收到，可在下方重新发送。
      </p>
      <form class="space-y-3" @submit.prevent="onResendEmail">
        <FormField label="邮箱" :error="resendErrors.email">
          <input v-model="resend.email" type="email" class="form-input" placeholder="you@example.com" />
        </FormField>
        <button type="submit" class="btn-primary" :disabled="resendLoading || remaining > 0">
          <span v-if="resendLoading">发送中…</span>
          <span v-else-if="remaining > 0">{{ remaining }} 秒后可再次发送</span>
          <span v-else>重新发送验证邮件</span>
        </button>
      </form>
      <p v-if="resendNotice" class="text-sm text-gray-600">{{ resendNotice }}</p>
      <p class="text-sm text-center text-gray-600">
        <router-link to="/login" class="text-brand hover:text-brand-dark">去登录</router-link>
      </p>
    </div>
  </AuthCard>
</template>
