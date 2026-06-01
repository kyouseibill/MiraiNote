<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <div class="max-w-5xl mx-auto px-6 py-8">
    <div class="bg-white rounded-2xl border border-gray-100 shadow-sm p-8">
      <h2 class="text-2xl font-bold text-gray-900">欢迎回来，{{ auth.user?.username }} 👋</h2>
      <p class="mt-2 text-gray-600 text-sm">
        在左侧选择「工作记录 / 备忘」开始今天的内容，或点击「生活备忘」记录生活点滴。
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
  </div>
</template>
