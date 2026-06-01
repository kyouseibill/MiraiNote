import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Memo } from '@/types/memo'
import { memoApi } from '@/api/memo'

/**
 * 备忘提醒弹窗轮询 Store。
 * - start() 在用户登录后启动定时器，每 30 秒拉取一次到期弹窗
 * - stop() 在退出登录时清理
 * - acknowledge() 用户点「我知道了」后通知后端，并从队列移除
 */
const POLL_INTERVAL_MS = 30_000

export const useReminderStore = defineStore('reminder', () => {
  const queue = ref<Memo[]>([])
  let timer: ReturnType<typeof setInterval> | null = null
  let polling = false

  async function poll() {
    if (polling) return
    polling = true
    try {
      const list = await memoApi.duePopups()
      // 合并：已存在 id 保留位置，新增追加
      const existingIds = new Set(queue.value.map((m) => m.id))
      const merged = [...queue.value]
      for (const m of list) {
        if (!existingIds.has(m.id)) merged.push(m)
      }
      // 同时移除后端已不再返回的（用户在其他设备确认 / 标记完成）
      const serverIds = new Set(list.map((m) => m.id))
      queue.value = merged.filter((m) => serverIds.has(m.id))
    } catch {
      // 静默：401 等会被全局拦截器处理
    } finally {
      polling = false
    }
  }

  function start() {
    if (timer) return
    void poll()
    timer = setInterval(() => void poll(), POLL_INTERVAL_MS)
  }

  function stop() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
    queue.value = []
  }

  async function acknowledge(id: number) {
    try {
      await memoApi.acknowledgePopup(id)
    } catch {
      // 即使失败也从前端队列移除，下个周期若仍到期会再次出现
    } finally {
      queue.value = queue.value.filter((m) => m.id !== id)
    }
  }

  return { queue, start, stop, poll, acknowledge }
})
