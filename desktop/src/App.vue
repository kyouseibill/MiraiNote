<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { miraiApi } from '@/api/mirai'
import CommandPalette from '@/components/CommandPalette.vue'
import ContextPanel from '@/components/ContextPanel.vue'
import { useUiStore } from '@/stores/ui'

const route = useRoute()
const ui = useUiStore()
const isLogin = computed(() => route.path === '/login')

const PAGE_TITLE: Record<string, string> = {
  today: '今天',
  inbox: '收件箱',
  work: '工作流',
  life: '生活流',
  tasks: '任务',
  reports: '周报',
  okr: 'OKR',
  memory: '记忆',
  settings: '设置',
}
const pageTitle = computed(() => PAGE_TITLE[String(route.name ?? '')] ?? 'Mirai')

const navItems = [
  { name: 'today', path: '/', label: '今天', icon: '◈' },
  { name: 'inbox', path: '/inbox', label: '收件箱', icon: '✦' },
  { name: 'work', path: '/work', label: '工作流', icon: '▤' },
  { name: 'life', path: '/life', label: '生活流', icon: '☘' },
  { name: 'tasks', path: '/tasks', label: '任务', icon: '✓' },
  { name: 'reports', path: '/reports', label: '周报', icon: '¶', soon: 'M2' },
  { name: 'okr', path: '/okr', label: 'OKR', icon: '◎', soon: 'M2' },
  { name: 'memory', path: '/memory', label: '记忆', icon: '◇', soon: 'M2' },
]

// 收件箱待处理角标（视觉稿 ① 导航「●3」）
// 事件来源：① 应用内 DOM 事件 mirai:inbox-changed（捕获条/收件箱页）
//          ② SHELL 流悬浮捕获窗的 Tauri 事件 mirai:inbox-updated（跨窗口）
const inboxPending = ref(0)
async function refreshBadge() {
  // 未登录时不发起鉴权请求（登录页的角标刷新会触发 401→redirect→reload 循环，联调实测闪烁根因）
  if (!localStorage.getItem('mirai.accessToken')) {
    inboxPending.value = 0
    return
  }
  try {
    inboxPending.value = await miraiApi.inboxPendingCount()
  } catch {
    inboxPending.value = 0
  }
}

function onInboxChanged() {
  void refreshBadge()
}
function onRouteChange() {
  void refreshBadge()
}

// 视觉稿 ⑤：侧边对话抽屉「→」按需开合（全局负责收起；展开由各详情页处理选中对象）
function onGlobalKeydown(e: KeyboardEvent) {
  if (e.key !== 'ArrowRight' || !ui.contextTarget || ui.paletteOpen) return
  const t = e.target as HTMLElement | null
  if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return
  ui.closeContext()
}

let unlistenTauri: (() => void) | null = null

onMounted(async () => {
  void refreshBadge()
  window.addEventListener('mirai:inbox-changed', onInboxChanged)
  window.addEventListener('mirai:inbox-updated', onInboxChanged)
  window.addEventListener('focus', onInboxChanged)
  window.addEventListener('keydown', onGlobalKeydown)
  // Tauri 运行时（打包/tauri dev）下监听捕获小窗的跨窗口事件
  if (typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window) {
    try {
      const { listen } = await import('@tauri-apps/api/event')
      const un = await listen('mirai:inbox-updated', () => void refreshBadge())
      unlistenTauri = un
    } catch {
      // 插件/IPC 未就绪时忽略
    }
  }
})
onUnmounted(() => {
  window.removeEventListener('mirai:inbox-changed', onInboxChanged)
  window.removeEventListener('mirai:inbox-updated', onInboxChanged)
  window.removeEventListener('focus', onInboxChanged)
  window.removeEventListener('keydown', onGlobalKeydown)
  unlistenTauri?.()
})

watch(() => route.path, onRouteChange)
</script>

<template>
  <div v-if="isLogin" class="h-full">
    <RouterView />
  </div>

  <div v-else class="flex h-full flex-col">
    <!-- 标题栏（Tauri 壳接管拖拽区后为应用内页头） -->
    <header class="flex items-center gap-3 border-b border-ink-faint/20 bg-paper-card px-4 py-2">
      <span class="font-bold text-brand">◈ Mirai</span>
      <span class="text-xs text-ink-faint">— {{ pageTitle }}</span>
      <button
        class="ml-auto flex items-center gap-2 rounded-full border border-ink-faint/30 bg-paper px-3 py-1 text-xs text-ink-faint hover:border-brand hover:text-brand"
        title="Ctrl+K 打开指令面板"
        @click="ui.openPalette()"
      >
        🔍 指令面板 · 随时说点什么
        <kbd class="rounded border border-ink-faint/30 bg-paper px-1 text-[10px]">Ctrl K</kbd>
      </button>
    </header>

    <div class="flex min-h-0 flex-1">
      <!-- 左导航（PRD §4.1 信息架构；视觉稿 ① 侧栏） -->
      <nav class="flex w-44 shrink-0 flex-col gap-0.5 border-r border-ink-faint/20 bg-paper-card/60 p-2">
        <RouterLink
          v-for="item in navItems"
          :key="item.path"
          :to="item.path"
          class="flex items-center gap-2 rounded-lg px-3 py-2"
          :class="item.soon ? 'text-ink-faint/50' : 'text-ink-sub hover:bg-brand-soft hover:text-brand'"
          exact-active-class="!bg-brand-soft !font-semibold !text-brand"
        >
          <span>{{ item.icon }}</span>
          <span>{{ item.label }}</span>
          <span v-if="item.name === 'inbox' && inboxPending > 0" class="text-xs text-warn" title="待处理">●{{ inboxPending }}</span>
          <span v-if="item.soon" class="ml-auto rounded border border-ink-faint/30 px-1 text-[10px] text-ink-faint">
            {{ item.soon }}
          </span>
        </RouterLink>
        <div class="my-2 border-t border-dashed border-ink-faint/20" />
        <RouterLink
          to="/settings"
          class="flex items-center gap-2 rounded-lg px-3 py-2 text-ink-sub hover:bg-brand-soft hover:text-brand"
          exact-active-class="!bg-brand-soft !font-semibold !text-brand"
        >
          <span>⚙</span><span>设置</span>
        </RouterLink>
        <div class="mt-auto px-3 pb-1 text-[11px] leading-5 text-ink-faint">
          ◉ 托盘常驻中<br />Ctrl+Shift+Space 随时捕获
        </div>
      </nav>

      <!-- 主内容区 -->
      <main class="min-w-0 flex-1 overflow-y-auto bg-paper p-5">
        <RouterView />
      </main>

      <!-- 右侧上下文面板（按需展开，ui store 控制） -->
      <ContextPanel />
    </div>

    <!-- 全局指令面板浮层 -->
    <CommandPalette />
  </div>
</template>
