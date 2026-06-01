// 简易 Toast 通知（基于 Pinia 之外的全局响应式数组）
import { reactive } from 'vue'

export type ToastType = 'success' | 'error' | 'info' | 'warning'

export interface ToastItem {
  id: number
  type: ToastType
  message: string
}

const state = reactive<{ list: ToastItem[] }>({ list: [] })
let seed = 0

function push(type: ToastType, message: string, duration = 3000) {
  const id = ++seed
  state.list.push({ id, type, message })
  window.setTimeout(() => {
    const idx = state.list.findIndex((t) => t.id === id)
    if (idx >= 0) state.list.splice(idx, 1)
  }, duration)
}

export function useToast() {
  return {
    list: state.list,
    success: (msg: string) => push('success', msg),
    error: (msg: string) => push('error', msg, 4000),
    info: (msg: string) => push('info', msg),
    warning: (msg: string) => push('warning', msg),
    dismiss: (id: number) => {
      const idx = state.list.findIndex((t) => t.id === id)
      if (idx >= 0) state.list.splice(idx, 1)
    },
  }
}
