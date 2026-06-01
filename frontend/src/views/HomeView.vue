<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'
import { useRouter } from 'vue-router'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const router = useRouter()
const toast = useToast()

async function handleLogout() {
  if (!confirm('确定要退出登录吗？')) return
  await auth.logout()
  toast.success('已退出登录')
  router.replace({ name: 'login' })
}
</script>

<template>
  <div class="min-h-screen flex flex-col">
    <header class="bg-white border-b border-gray-200">
      <div class="max-w-6xl mx-auto px-4 sm:px-6 h-14 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-lg bg-brand text-white flex items-center justify-center font-bold">M</div>
          <span class="font-semibold text-gray-900">未来ノート</span>
        </div>
        <div class="flex items-center gap-3 text-sm">
          <span class="text-gray-600 hidden sm:inline">
            欢迎，<span class="font-medium text-gray-900">{{ auth.user?.username }}</span>
            <span v-if="auth.isAdmin" class="ml-1 inline-block px-1.5 py-0.5 rounded bg-indigo-100 text-indigo-700 text-xs">管理员</span>
          </span>
          <button
            class="px-3 py-1.5 rounded-md text-gray-700 hover:bg-gray-100 transition"
            @click="handleLogout"
          >
            退出
          </button>
        </div>
      </div>
    </header>

    <main class="flex-1 max-w-6xl mx-auto w-full px-4 sm:px-6 py-10">
      <div class="bg-white rounded-2xl border border-gray-100 shadow-sm p-8">
        <h1 class="text-2xl font-bold text-gray-900">欢迎回来，{{ auth.user?.username }} 👋</h1>
        <p class="mt-2 text-gray-600 text-sm">
          这里是未来ノート首页占位。后续将接入工作记录、备忘、AI 周报等模块。
        </p>

        <dl class="mt-6 grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
          <div class="border border-gray-100 rounded-lg p-4">
            <dt class="text-gray-500">邮箱</dt>
            <dd class="mt-1 font-medium text-gray-900 break-all">{{ auth.user?.email }}</dd>
          </div>
          <div class="border border-gray-100 rounded-lg p-4">
            <dt class="text-gray-500">邮箱验证状态</dt>
            <dd class="mt-1 font-medium" :class="auth.user?.isEmailVerified ? 'text-green-600' : 'text-amber-600'">
              {{ auth.user?.isEmailVerified ? '已验证' : '未验证' }}
            </dd>
          </div>
          <div class="border border-gray-100 rounded-lg p-4">
            <dt class="text-gray-500">最后登录</dt>
            <dd class="mt-1 font-medium text-gray-900">{{ auth.user?.lastLoginAt || '—' }}</dd>
          </div>
          <div class="border border-gray-100 rounded-lg p-4">
            <dt class="text-gray-500">账号状态</dt>
            <dd class="mt-1 font-medium" :class="auth.user?.isActive ? 'text-green-600' : 'text-red-600'">
              {{ auth.user?.isActive ? '正常' : '已禁用' }}
            </dd>
          </div>
        </dl>
      </div>
    </main>
  </div>
</template>
