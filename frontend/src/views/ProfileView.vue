<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import { useRouter } from 'vue-router'

const auth = useAuthStore()
const toast = useToast()
const router = useRouter()

// ===== 修改密码 =====
const pwForm = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const pwSubmitting = ref(false)
const pwErrors = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })

function validatePw(): boolean {
  pwErrors.currentPassword = ''
  pwErrors.newPassword = ''
  pwErrors.confirmPassword = ''
  let ok = true
  if (!pwForm.currentPassword) { pwErrors.currentPassword = '请输入当前密码'; ok = false }
  if (!pwForm.newPassword || pwForm.newPassword.length < 8) { pwErrors.newPassword = '新密码至少 8 位'; ok = false }
  if (pwForm.newPassword !== pwForm.confirmPassword) { pwErrors.confirmPassword = '两次输入的密码不一致'; ok = false }
  return ok
}

async function submitPw() {
  if (!validatePw()) return
  pwSubmitting.value = true
  try {
    await authApi.changePassword({
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
      confirmPassword: pwForm.confirmPassword,
    })
    toast.success('密码修改成功，请重新登录')
    // 后端已吊销 refresh token，前端清理并跳转登录
    auth.clearAuth()
    router.replace({ name: 'login' })
  } catch {
    // 拦截器已 toast
  } finally {
    pwSubmitting.value = false
  }
}

function fmtDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
  })
}
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-6 sm:px-6 lg:py-10 space-y-8">

    <!-- 账户信息卡 -->
    <section class="surface-card overflow-hidden">
      <div class="px-6 py-4 border-b border-gray-100 flex items-center gap-3">
        <!-- 头像占位 -->
        <div class="w-12 h-12 rounded-full bg-teal-100 text-teal-600 flex items-center justify-center text-xl font-bold select-none">
          {{ auth.user?.username?.charAt(0).toUpperCase() }}
        </div>
        <div>
          <p class="font-semibold text-gray-900 text-lg">{{ auth.user?.username }}</p>
          <p class="text-sm text-gray-500">{{ auth.user?.email }}</p>
        </div>
        <div class="ml-auto flex gap-2">
          <span
            v-if="auth.isAdmin"
            class="text-xs px-2 py-0.5 rounded-full bg-teal-100 text-teal-700 font-medium"
          >管理员</span>
          <span
            class="text-xs px-2 py-0.5 rounded-full"
            :class="auth.user?.isEmailVerified
              ? 'bg-green-100 text-green-700'
              : 'bg-amber-100 text-amber-700'"
          >
            {{ auth.user?.isEmailVerified ? '邮箱已验证' : '邮箱未验证' }}
          </span>
        </div>
      </div>

      <dl class="divide-y divide-gray-50 px-6">
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">用户 ID</dt>
          <dd class="text-sm text-gray-800 font-mono">{{ auth.user?.id }}</dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">账户状态</dt>
          <dd class="text-sm">
            <span
              class="px-2 py-0.5 rounded-full text-xs"
              :class="auth.user?.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-600'"
            >
              {{ auth.user?.isActive ? '正常' : '已禁用' }}
            </span>
          </dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">注册时间</dt>
          <dd class="text-sm text-gray-800">{{ fmtDate(auth.user?.createdAt ?? null) }}</dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">上次登录</dt>
          <dd class="text-sm text-gray-800">{{ fmtDate(auth.user?.lastLoginAt ?? null) }}</dd>
        </div>
      </dl>
    </section>

    <!-- 修改密码卡 -->
    <section class="surface-card">
      <div class="px-6 py-4 border-b border-gray-100">
        <h2 class="font-semibold text-gray-900">修改密码</h2>
        <p class="text-sm text-gray-500 mt-0.5">修改后将自动退出，需重新登录</p>
      </div>

      <form class="px-6 py-5 space-y-4" @submit.prevent="submitPw">
        <!-- 当前密码 -->
        <div>
          <label class="block text-sm text-gray-700 mb-1">当前密码 <span class="text-red-500">*</span></label>
          <input
            v-model="pwForm.currentPassword"
            type="password"
            autocomplete="current-password"
            class="w-full h-9 px-3 rounded-md border text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
            :class="pwErrors.currentPassword ? 'border-red-400' : 'border-gray-200'"
          />
          <p v-if="pwErrors.currentPassword" class="mt-1 text-xs text-red-500">{{ pwErrors.currentPassword }}</p>
        </div>

        <!-- 新密码 -->
        <div>
          <label class="block text-sm text-gray-700 mb-1">新密码 <span class="text-red-500">*</span></label>
          <input
            v-model="pwForm.newPassword"
            type="password"
            autocomplete="new-password"
            class="w-full h-9 px-3 rounded-md border text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
            :class="pwErrors.newPassword ? 'border-red-400' : 'border-gray-200'"
          />
          <p v-if="pwErrors.newPassword" class="mt-1 text-xs text-red-500">{{ pwErrors.newPassword }}</p>
        </div>

        <!-- 确认新密码 -->
        <div>
          <label class="block text-sm text-gray-700 mb-1">确认新密码 <span class="text-red-500">*</span></label>
          <input
            v-model="pwForm.confirmPassword"
            type="password"
            autocomplete="new-password"
            class="w-full h-9 px-3 rounded-md border text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
            :class="pwErrors.confirmPassword ? 'border-red-400' : 'border-gray-200'"
          />
          <p v-if="pwErrors.confirmPassword" class="mt-1 text-xs text-red-500">{{ pwErrors.confirmPassword }}</p>
        </div>

        <div class="pt-1">
          <button
            type="submit"
            class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60 transition"
            :disabled="pwSubmitting"
          >
            {{ pwSubmitting ? '保存中…' : '保存修改' }}
          </button>
        </div>
      </form>
    </section>

  </div>
</template>
