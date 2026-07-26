<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
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
  items: NavItem[]
}

const groups: NavGroup[] = [
  {
    title: '工作',
    items: [
      { to: '/dashboard', label: '工作台', icon: '⌂' },
      { to: '/work/logs', label: '工作记录', icon: '▤' },
      { to: '/work/memos', label: '工作备忘', icon: '✓' },
      { to: '/work/reports', label: '智能周报', icon: '↗' },
    ],
  },
  {
    title: '生活',
    items: [
      { to: '/life/memos', label: '生活备忘', icon: '○' },
      { to: '/life/logs', label: '生活记录', icon: '◌' },
    ],
  },
  {
    title: '工具',
    items: [
      { to: '/chat', label: 'Mirai Chat', icon: '✦' },
    ],
  },
]

const currentTitle = computed(() => (route.meta.title as string) || '未来ノート')
const mobileMenuOpen = ref(false)
const userInitial = computed(() => auth.user?.username?.charAt(0).toUpperCase() || 'M')

watch(() => route.fullPath, () => {
  mobileMenuOpen.value = false
})

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
  <div class="min-h-screen flex bg-slate-100">
    <div
      v-if="mobileMenuOpen"
      class="fixed inset-0 z-30 bg-slate-950/35 backdrop-blur-sm lg:hidden"
      @click="mobileMenuOpen = false"
    />

    <aside
      class="fixed inset-y-0 left-0 z-40 flex w-[264px] shrink-0 flex-col bg-slate-950 text-slate-200 shadow-float transition-transform duration-300 lg:relative lg:translate-x-0 lg:shadow-none"
      :class="mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'"
    >
      <div class="flex h-[72px] items-center gap-3 border-b border-white/10 px-5">
        <div class="flex h-10 w-10 items-center justify-center rounded-xl bg-teal-500 text-base font-bold text-white shadow-lg shadow-teal-950/30">M</div>
        <div class="min-w-0">
          <p class="truncate text-[15px] font-semibold tracking-wide text-white">未来ノート</p>
          <p class="mt-0.5 text-[10px] uppercase tracking-[0.18em] text-slate-500">Personal workspace</p>
        </div>
        <button
          class="ml-auto flex h-9 w-9 items-center justify-center rounded-lg text-slate-400 hover:bg-white/10 hover:text-white lg:hidden"
          aria-label="关闭导航"
          @click="mobileMenuOpen = false"
        >
          ×
        </button>
      </div>

      <nav class="flex-1 space-y-7 overflow-y-auto px-3 py-6">
        <div v-for="group in groups" :key="group.title">
          <div class="mb-2 px-3 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-500">
            {{ group.title }}
          </div>
          <ul class="space-y-1">
            <li v-for="item in group.items" :key="item.to">
              <RouterLink
                :to="item.to"
                class="group flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm text-slate-400 transition duration-200 hover:bg-white/[0.06] hover:text-slate-100 aria-[current=page]:bg-teal-500/15 aria-[current=page]:font-medium aria-[current=page]:text-teal-300"
              >
                <span class="flex h-7 w-7 items-center justify-center rounded-lg bg-white/[0.05] text-sm text-slate-400 transition group-aria-[current=page]:bg-teal-400/15 group-aria-[current=page]:text-teal-300">{{ item.icon }}</span>
                <span>{{ item.label }}</span>
                <span class="ml-auto h-1.5 w-1.5 rounded-full bg-teal-400 opacity-0 group-aria-[current=page]:opacity-100" />
              </RouterLink>
            </li>
          </ul>
        </div>
      </nav>

      <div class="border-t border-white/10 p-3">
        <RouterLink
          to="/profile"
          class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm text-slate-400 transition hover:bg-white/[0.06] hover:text-white aria-[current=page]:bg-white/10 aria-[current=page]:text-white"
        >
          <span class="flex h-8 w-8 items-center justify-center rounded-lg bg-white/[0.06]">⚙</span>
          <span class="min-w-0 flex-1 truncate">个人设置</span>
        </RouterLink>
      </div>
    </aside>

    <div class="flex-1 flex flex-col min-w-0">
      <header class="sticky top-0 z-20 flex h-[72px] items-center justify-between border-b border-slate-200/80 bg-white/90 px-4 backdrop-blur-xl sm:px-6 lg:px-8">
        <div class="flex min-w-0 items-center gap-3">
          <button
            class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-600 shadow-sm hover:bg-slate-50 lg:hidden"
            aria-label="打开导航"
            @click="mobileMenuOpen = true"
          >
            ☰
          </button>
          <div class="min-w-0">
            <p class="text-[10px] font-semibold uppercase tracking-[0.16em] text-teal-700">Workspace</p>
            <h1 class="truncate text-lg font-semibold tracking-tight text-slate-900">{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="flex items-center gap-2 text-sm sm:gap-3">
          <RouterLink
            to="/profile"
            class="hidden items-center gap-2 rounded-xl border border-slate-200 bg-white py-1.5 pl-1.5 pr-3 text-slate-600 shadow-sm transition hover:border-teal-200 hover:text-teal-800 sm:flex"
          >
            <span class="flex h-7 w-7 items-center justify-center rounded-lg bg-teal-50 text-xs font-bold text-teal-700">{{ userInitial }}</span>
            <span class="font-medium">{{ auth.user?.username }}</span>
            <span v-if="auth.isAdmin" class="rounded-md bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">管理员</span>
          </RouterLink>
          <button
            class="rounded-xl px-3 py-2 text-sm font-medium text-slate-500 transition hover:bg-slate-100 hover:text-slate-900"
            @click="handleLogout"
          >
            退出
          </button>
        </div>
      </header>

      <main class="flex-1 overflow-y-auto bg-slate-100">
        <RouterView />
      </main>
    </div>

    <ReminderPopup />
  </div>
</template>
