<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useReminderStore } from '@/stores/reminder'
import { useToast } from '@/composables/useToast'
import ReminderPopup from '@/components/ReminderPopup.vue'

const auth = useAuthStore()
const reminder = useReminderStore()
const route = useRoute()
const router = useRouter()
const toast = useToast()

interface NavItem {
  to: string
  label: string
  icon: string
}

interface NavGroup {
  title: string
  accent: string // tailwind text/bg accent
  items: NavItem[]
}

const groups: NavGroup[] = [
  {
    title: '工作',
    accent: 'indigo',
    items: [
      { to: '/dashboard', label: '工作台', icon: '◐' },
      { to: '/work/logs', label: '工作记录', icon: '✎' },
      { to: '/work/memos', label: '工作备忘', icon: '☑' },
      { to: '/work/reports', label: 'AI 周报', icon: '✨' },
    ],
  },
  {
    title: '生活',
    accent: 'rose',
    items: [
      { to: '/life/memos', label: '生活备忘', icon: '✿' },
      { to: '/life/logs', label: '生活记录', icon: '📖' },
    ],
  },
  {
    title: '工具',
    accent: 'violet',
    items: [
      { to: '/chat', label: 'AI 对话', icon: '💬' },
    ],
  },
]

const currentTitle = computed(() => (route.meta.title as string) || '未来ノート')

async function handleLogout() {
  if (!confirm('确定要退出登录吗？')) return
  reminder.stop()
  await auth.logout()
  toast.success('已退出登录')
  router.replace({ name: 'login' })
}

onMounted(() => reminder.start())
onBeforeUnmount(() => reminder.stop())
</script>

<template>
  <div class="min-h-screen flex bg-gray-50">
    <!-- 侧边栏 -->
    <aside class="w-56 shrink-0 bg-white border-r border-gray-200 flex flex-col">
      <div class="h-14 px-4 flex items-center gap-2 border-b border-gray-100">
        <div class="w-8 h-8 rounded-lg bg-brand text-white flex items-center justify-center font-bold">M</div>
        <span class="font-semibold text-gray-900">未来ノート</span>
      </div>

      <nav class="flex-1 px-3 py-4 space-y-6 overflow-y-auto">
        <div v-for="group in groups" :key="group.title">
          <div
            class="px-2 mb-2 text-xs font-semibold tracking-wider uppercase"
            :class="{
              'text-rose-500': group.accent === 'rose',
              'text-violet-500': group.accent === 'violet',
              'text-indigo-500': group.accent === 'indigo',
            }"
          >
            {{ group.title }}
          </div>
          <ul class="space-y-1">
            <li v-for="item in group.items" :key="item.to">
              <RouterLink
                :to="item.to"
                class="flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-gray-700 hover:bg-gray-100 transition"
                :class="{
                  'aria-[current=page]:bg-rose-50 aria-[current=page]:text-rose-700': group.accent === 'rose',
                  'aria-[current=page]:bg-violet-50 aria-[current=page]:text-violet-700': group.accent === 'violet',
                  'aria-[current=page]:bg-indigo-50 aria-[current=page]:text-indigo-700': group.accent === 'indigo',
                }"
              >
                <span class="w-5 text-center opacity-70">{{ item.icon }}</span>
                <span>{{ item.label }}</span>
              </RouterLink>
            </li>
          </ul>
        </div>
      </nav>

      <div class="px-3 py-3 border-t border-gray-100 space-y-1">
        <RouterLink
          to="/profile"
          class="flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-gray-600 hover:bg-gray-100 transition"
        >
          <span class="w-5 text-center opacity-70">⚙</span>
          <span>个人设置</span>
        </RouterLink>
        <p class="px-3 text-xs text-gray-400">v0.3 · Phase 5</p>
      </div>
    </aside>

    <!-- 右侧主区 -->
    <div class="flex-1 flex flex-col min-w-0">
      <header class="h-14 bg-white border-b border-gray-200 px-6 flex items-center justify-between">
        <h1 class="text-base font-semibold text-gray-900">{{ currentTitle }}</h1>
        <div class="flex items-center gap-3 text-sm">
          <RouterLink
            to="/profile"
            class="text-gray-600 hidden sm:inline hover:text-indigo-600 transition"
          >
            <span class="font-medium">{{ auth.user?.username }}</span>
            <span v-if="auth.isAdmin" class="ml-1 inline-block px-1.5 py-0.5 rounded bg-indigo-100 text-indigo-700 text-xs">管理员</span>
          </RouterLink>
          <button
            class="px-3 py-1.5 rounded-md text-gray-700 hover:bg-gray-100 transition"
            @click="handleLogout"
          >
            退出
          </button>
        </div>
      </header>

      <main class="flex-1 overflow-y-auto">
        <RouterView />
      </main>
    </div>

    <ReminderPopup />
  </div>
</template>
