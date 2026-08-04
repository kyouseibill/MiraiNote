<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch, type Component } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import {
  IconBooks,
  IconChecklist,
  IconFileText,
  IconHome,
  IconMenu2,
  IconMessageCircle,
  IconNotebook,
  IconReportAnalytics,
  IconSettings,
  IconX,
} from '@tabler/icons-vue'
import { useReminderStore } from '@/stores/reminder'
import ReminderPopup from '@/components/ReminderPopup.vue'

const reminder = useReminderStore()
const route = useRoute()

interface NavItem {
  to: string
  label: string
  icon: Component
}

interface NavGroup {
  title: string
  items: NavItem[]
}

const groups: NavGroup[] = [
  {
    title: '工作',
    items: [
      { to: '/dashboard', label: '工作台', icon: IconHome },
      { to: '/work/logs', label: '工作记录', icon: IconFileText },
      { to: '/work/memos', label: '工作备忘', icon: IconChecklist },
      { to: '/work/reports', label: '智能周报', icon: IconReportAnalytics },
    ],
  },
  {
    title: '生活',
    items: [
      { to: '/life/memos', label: '生活备忘', icon: IconNotebook },
      { to: '/life/logs', label: '生活记录', icon: IconBooks },
    ],
  },
  {
    title: '智能',
    items: [{ to: '/chat', label: 'Mirai Chat', icon: IconMessageCircle }],
  },
]

const currentTitle = computed(() => (route.meta.title as string) || '未来ノート')
const isDesignPreview = computed(() => import.meta.env.DEV && route.query.designPreview === '1')
const mobileMenuOpen = ref(false)

watch(() => route.fullPath, () => {
  mobileMenuOpen.value = false
})

onMounted(() => {
  if (!isDesignPreview.value) reminder.start()
})
onBeforeUnmount(() => {
  if (!isDesignPreview.value) reminder.stop()
})
</script>

<template>
  <div class="flex min-h-screen bg-[#f6f3ec] text-[#262521]">
    <div
      v-if="mobileMenuOpen"
      class="fixed inset-0 z-30 bg-[#262521]/20 backdrop-blur-[2px] lg:hidden"
      @click="mobileMenuOpen = false"
    />

    <aside
      class="fixed inset-y-0 left-0 z-40 flex w-[264px] shrink-0 flex-col border-r border-[#ddd8cf] bg-[#fcfbf8] transition-transform duration-300 lg:sticky lg:top-0 lg:h-screen lg:translate-x-0"
      :class="mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'"
    >
      <div class="flex h-[106px] items-center gap-3 px-7">
        <img src="/favicon.svg" alt="未来ノート" class="h-11 w-11 rounded-[10px]" />
        <div class="min-w-0">
          <p class="truncate font-serif text-[16px] font-medium tracking-[0.08em] text-[#262521]">未来ノート</p>
          <p class="mt-0.5 text-[9px] uppercase tracking-[0.2em] text-[#9a958d]">Mirai workspace</p>
        </div>
        <button
          class="ml-auto flex h-9 w-9 items-center justify-center rounded-md text-[#7f7a72] hover:bg-[#f1eee8] lg:hidden"
          aria-label="关闭导航"
          @click="mobileMenuOpen = false"
        >
          <IconX :size="18" :stroke-width="1.5" />
        </button>
      </div>

      <nav class="flex-1 overflow-y-auto px-3 pb-5">
        <div v-for="group in groups" :key="group.title" class="border-b border-[#e5e0d8] py-4 last:border-b-0">
          <div class="mb-2 px-5 text-[10px] font-medium tracking-[0.18em] text-[#aaa49b]">
            {{ group.title }}
          </div>
          <ul class="space-y-1">
            <li v-for="item in group.items" :key="item.to">
              <RouterLink
                :to="item.to"
                class="group relative flex h-12 items-center gap-3 rounded-[5px] px-5 text-[13px] text-[#625f59] transition hover:bg-[#f3f0eb] hover:text-[#384b60] aria-[current=page]:bg-[#f1eee9] aria-[current=page]:text-[#384b60]"
              >
                <span class="absolute right-3 h-[9px] w-[9px] rounded-full bg-[#b4493f] opacity-0 transition group-aria-[current=page]:opacity-100" />
                <component :is="item.icon" :size="20" :stroke-width="1.45" class="shrink-0 text-[#737982] group-aria-[current=page]:text-[#4c6178]" />
                <span>{{ item.label }}</span>
              </RouterLink>
            </li>
          </ul>
        </div>
      </nav>

      <div class="border-t border-[#e5e0d8] p-3">
        <RouterLink
          to="/profile"
          class="group flex h-11 items-center gap-3 rounded-[5px] px-5 text-[13px] text-[#625f59] transition hover:bg-[#f3f0eb] aria-[current=page]:bg-[#f1eee9] aria-[current=page]:text-[#384b60]"
        >
          <IconSettings :size="20" :stroke-width="1.45" />
          <span>设置</span>
        </RouterLink>
      </div>
    </aside>

    <div class="flex min-w-0 flex-1 flex-col">
      <header class="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-[#e2ddd5] bg-[#fcfbf8]/95 px-4 backdrop-blur-lg lg:hidden">
        <div class="flex min-w-0 items-center gap-3">
          <button
            class="flex h-9 w-9 items-center justify-center rounded-md border border-[#ddd8cf] bg-white/70 text-[#4c6178]"
            aria-label="打开导航"
            @click="mobileMenuOpen = true"
          >
            <IconMenu2 :size="19" :stroke-width="1.5" />
          </button>
          <span class="truncate font-serif text-[15px] text-[#262521]">{{ currentTitle }}</span>
        </div>
      </header>

      <main class="min-h-0 flex-1 overflow-y-auto bg-[#fcfbf8]">
        <RouterView />
      </main>
    </div>

    <ReminderPopup />
  </div>
</template>
