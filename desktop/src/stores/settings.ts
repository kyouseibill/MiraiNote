import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'

// ============================================================
// 桌面偏好设置
// 持久化优先走 tauri-plugin-store（SHELL 流已在 src-tauri/Cargo.toml 声明依赖并负责注册），
// 通过 @tauri-apps/api/core 的 IPC invoke 直连 plugin:store 命令——不额外引入
// @tauri-apps/plugin-store npm 包，避免与 SHELL 流的 package.json 变更冲突。
// 浏览器 / mock 开发（无 Tauri 运行时）自动回落 localStorage，键名与 plugin-store 一致。
//
// 【与 SHELL 流协调的键名】store 文件：mirai-settings.json
//   pref.autostart            boolean  开机自启
//   pref.dueNotification      boolean  到期原生通知总开关（每日 ≤5 条）
//   pref.captureHotkey        string   全局捕获热键（默认 Ctrl+Shift+Space）
//   pref.paletteHotkey        string   应用内指令面板热键（默认 Ctrl+K，只读展示）
//   pref.apiBase              string   API 基址（只读，来自构建期 env）
// ============================================================

const STORE_FILE = 'mirai-settings.json'

export interface Preferences {
  autostart: boolean
  dueNotification: boolean
  captureHotkey: string
}

const DEFAULT_PREFS: Preferences = {
  autostart: true,
  dueNotification: true,
  captureHotkey: 'Ctrl+Shift+Space',
}

function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window
}

// tauri-plugin-store v2 以资源句柄（rid）寻址 store：load(path) → rid，后续命令带 rid。
// 这里直接走 @tauri-apps/api/core 的 IPC invoke，不额外依赖 @tauri-apps/plugin-store npm 包。
let storeRid: number | null = null

async function ensureStore(): Promise<number | null> {
  if (storeRid != null) return storeRid
  try {
    const { invoke } = await import('@tauri-apps/api/core')
    storeRid = await invoke<number>('plugin:store|load', { path: STORE_FILE })
    return storeRid
  } catch {
    return null // 插件未注册（SHELL 流完成前）→ 调用方回落 localStorage
  }
}

async function storeGet<T>(key: string): Promise<T | undefined> {
  const rid = await ensureStore()
  if (rid == null) throw new Error('store unavailable')
  const { invoke } = await import('@tauri-apps/api/core')
  return invoke<T | undefined>('plugin:store|get', { rid, key })
}

async function storeSet(key: string, value: unknown): Promise<void> {
  const rid = await ensureStore()
  if (rid == null) throw new Error('store unavailable')
  const { invoke } = await import('@tauri-apps/api/core')
  await invoke('plugin:store|set', { rid, key, value })
  await invoke('plugin:store|save', { rid })
}

/** 读取单个偏好：plugin-store → localStorage 回落 → 默认值 */
async function readPref<K extends keyof Preferences>(key: K): Promise<Preferences[K]> {
  const fullKey = `pref.${key}`
  if (isTauriRuntime()) {
    try {
      const v = await storeGet<Preferences[K]>(fullKey)
      if (v !== undefined && v !== null) return v
      // 未迁移过：尝试把 localStorage 里的旧值搬进 plugin-store
      const legacy = localStorage.getItem(fullKey)
      if (legacy != null) {
        const parsed = JSON.parse(legacy) as Preferences[K]
        await storeSet(fullKey, parsed).catch(() => undefined)
        return parsed
      }
      return DEFAULT_PREFS[key]
    } catch {
      // 插件未注册 / 权限未开（SHELL 流完成前）→ 回落 localStorage
    }
  }
  const raw = localStorage.getItem(fullKey)
  if (raw == null) return DEFAULT_PREFS[key]
  try {
    return JSON.parse(raw) as Preferences[K]
  } catch {
    return DEFAULT_PREFS[key]
  }
}

/** 写入单个偏好：双写 plugin-store 与 localStorage（保证任一后端可用） */
async function writePref<K extends keyof Preferences>(key: K, value: Preferences[K]): Promise<void> {
  const fullKey = `pref.${key}`
  localStorage.setItem(fullKey, JSON.stringify(value))
  if (isTauriRuntime()) {
    try {
      await storeSet(fullKey, value)
    } catch {
      // 插件未就绪时静默回落（localStorage 已写入）
    }
  }
}

export const useSettingsStore = defineStore('settings', () => {
  const prefs = reactive<Preferences>({ ...DEFAULT_PREFS })
  const loaded = ref(false)

  async function load() {
    const keys = Object.keys(DEFAULT_PREFS) as (keyof Preferences)[]
    await Promise.all(keys.map(async (k) => ((prefs[k] as Preferences[typeof k]) = await readPref(k))))
    loaded.value = true
  }

  async function set<K extends keyof Preferences>(key: K, value: Preferences[K]) {
    prefs[key] = value
    await writePref(key, value)
  }

  return { prefs, loaded, load, set }
})
