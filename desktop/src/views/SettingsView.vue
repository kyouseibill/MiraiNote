<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { miraiApi } from '@/api/mirai'
import type { AiActionStats } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { useSettingsStore } from '@/stores/settings'

// 设置页：桌面偏好（plugin-store / localStorage 持久化，键名与 SHELL 流协调）
// + AI 调用统计（AIActionLog，含 byActionType 明细）
const auth = useAuthStore()
const settings = useSettingsStore()

const stats = ref<AiActionStats | null>(null)
const loading = ref(true)
const apiBase = import.meta.env.MIRAI_API_BASE || 'http://localhost:5273/api/v1'
const useMock = import.meta.env.MIRAI_USE_MOCK === '1'

const ACTION_LABEL: Record<string, string> = {
  inbox_dispatch: '收件箱分发',
  inbox_discard: '收件箱丢弃',
  inbox_undo: '分发撤销',
  inbox_retriage: '纠错重分拣',
  briefing_generate: '晨报生成',
  briefing_regenerate: '晨报重生成',
  command_agent: '指令面板',
  context_chat: '侧边对话',
}

const maxCount = computed(() => Math.max(1, ...(stats.value?.last7Days.map((d) => d.count) ?? [1])))
const totalOfByType = computed(() => stats.value?.byActionType.reduce((s, t) => s + t.count, 0) ?? 0)

onMounted(async () => {
  void settings.load()
  try {
    stats.value = await miraiApi.aiStats()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="mx-auto max-w-2xl space-y-4">
    <h1 class="text-base font-bold">设置</h1>

    <!-- 账户 -->
    <section class="rounded-xl border border-ink-faint/20 bg-paper-card p-4">
      <b class="text-sm">账户</b>
      <div class="mt-2 flex items-center gap-3 text-xs text-ink-sub">
        <span>{{ auth.username || '未登录' }}</span>
        <span v-if="useMock" class="rounded border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-[10px] text-amber-700">Mock 模式</span>
        <button class="ml-auto rounded-lg border border-ink-faint/30 px-3 py-1 hover:border-red-300 hover:text-red-600" @click="auth.logout()">
          退出登录
        </button>
      </div>
    </section>

    <!-- 桌面偏好（plugin-store；SHELL 流按同键名读取） -->
    <section class="rounded-xl border border-ink-faint/20 bg-paper-card p-4">
      <b class="text-sm">桌面偏好</b>
      <div class="mt-3 space-y-3 text-xs text-ink-sub">
        <label class="flex cursor-pointer items-center gap-2">
          <input
            type="checkbox"
            class="accent-brand"
            :checked="settings.prefs.autostart"
            @change="settings.set('autostart', ($event.target as HTMLInputElement).checked)"
          />
          开机自启
          <span class="text-[11px] text-ink-faint">（登录 Windows 后自动驻留托盘）</span>
        </label>
        <label class="flex cursor-pointer items-center gap-2">
          <input
            type="checkbox"
            class="accent-brand"
            :checked="settings.prefs.dueNotification"
            @change="settings.set('dueNotification', ($event.target as HTMLInputElement).checked)"
          />
          到期原生通知
          <span class="text-[11px] text-ink-faint">（每日 ≤5 条，点击通知聚焦到 Mirai）</span>
        </label>
        <div class="flex items-center gap-2">
          <span class="shrink-0">全局捕获热键</span>
          <input
            class="w-44 rounded-lg border border-ink-faint/30 px-2 py-1 font-mono text-[11px] outline-none focus:border-brand"
            :value="settings.prefs.captureHotkey"
            placeholder="Ctrl+Shift+Space"
            @change="settings.set('captureHotkey', ($event.target as HTMLInputElement).value.trim() || 'Ctrl+Shift+Space')"
          />
          <span class="text-[11px] text-ink-faint">（任何应用内唤起捕获小窗）</span>
        </div>
        <div class="flex items-center gap-2">
          <span class="shrink-0">指令面板热键</span>
          <kbd class="rounded border border-ink-faint/30 bg-paper px-1.5 py-0.5 font-mono text-[11px]">Ctrl K</kbd>
          <span class="text-[11px] text-ink-faint">（应用内固定，暂不支持改键）</span>
        </div>
        <div class="text-ink-faint">API 基址：{{ apiBase }}</div>
      </div>
    </section>

    <!-- AI 调用统计 -->
    <section class="rounded-xl border border-ink-faint/20 bg-paper-card p-4">
      <b class="text-sm">AI 调用统计（AIActionLog）</b>
      <div v-if="loading" class="mt-2 text-xs text-ink-faint">加载中…</div>
      <template v-else-if="stats">
        <div class="mt-2 flex items-baseline gap-2">
          <span class="text-2xl font-bold text-brand">{{ stats.total }}</span>
          <span class="text-xs text-ink-faint">次累计 AI 处理</span>
        </div>

        <!-- 近 7 日柱状图 -->
        <div class="mt-3 flex items-end gap-1.5" style="height: 56px">
          <div
            v-for="d in stats.last7Days"
            :key="d.date"
            class="flex-1 rounded-t bg-brand/80 transition-all hover:bg-brand"
            :style="{ height: `${Math.max(6, (d.count / maxCount) * 56)}px` }"
            :title="`${d.date}: ${d.count} 次`"
          />
        </div>
        <div class="mt-1 flex gap-1.5">
          <span v-for="d in stats.last7Days" :key="d.date" class="flex-1 text-center text-[9px] text-ink-faint">
            {{ d.date.slice(5) }}
          </span>
        </div>

        <!-- byActionType 明细 -->
        <div class="mt-4 border-t border-dashed border-ink-faint/20 pt-3">
          <div class="mb-2 text-[11px] font-semibold text-ink-sub">按动作类型</div>
          <div class="space-y-1.5">
            <div v-for="t in stats.byActionType" :key="t.actionType" class="flex items-center gap-2 text-[11.5px]">
              <span class="w-24 shrink-0 text-ink-sub">{{ ACTION_LABEL[t.actionType] ?? t.actionType }}</span>
              <div class="h-1.5 min-w-0 flex-1 overflow-hidden rounded-full bg-paper">
                <div class="h-full rounded-full bg-brand/70" :style="{ width: `${Math.round((t.count / (totalOfByType || 1)) * 100)}%` }" />
              </div>
              <span class="w-20 shrink-0 text-right tabular-nums text-ink-faint">
                {{ t.count }} 次 · {{ Math.round((t.count / (totalOfByType || 1)) * 100) }}%
              </span>
            </div>
          </div>
          <div class="mt-2 text-[10px] text-ink-faint">统计口径：分发 / 丢弃 / 撤销 / 晨报 / 会话等 AI 动作，不含纯数据读写。</div>
        </div>
      </template>
      <div v-else class="mt-2 text-xs text-warn">统计加载失败（AI 服务不可用不影响其他功能）</div>
    </section>
  </div>
</template>
