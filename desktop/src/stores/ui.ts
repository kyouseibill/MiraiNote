import { defineStore } from 'pinia'
import type { AttachToType } from '@/api/types'

// 全局 UI 状态：指令面板开合、上下文面板挂载对象
export interface ContextTarget {
  type: AttachToType
  id: number
  title: string
}

export const useUiStore = defineStore('ui', {
  state: () => ({
    paletteOpen: false,
    contextTarget: null as ContextTarget | null,
    /** 每次打开/切换挂载对象时递增，ContextPanel 据此重置会话 */
    contextSeq: 0,
  }),
  actions: {
    openPalette() {
      this.paletteOpen = true
    },
    closePalette() {
      this.paletteOpen = false
    },
    togglePalette() {
      this.paletteOpen = !this.paletteOpen
    },
    openContext(target: ContextTarget) {
      this.contextTarget = target
      this.contextSeq++
    },
    closeContext() {
      this.contextTarget = null
    },
  },
})
