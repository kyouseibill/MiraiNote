import { defineStore } from 'pinia'
import { http, unwrap, setAccessToken, bindUnauthorizedHandler } from '@/api/client'

// 精简登录状态；完整 AuthResponse 字段由 UI 流对齐 frontend/src/types/auth.ts 后补全。
interface AuthResponse {
  accessToken: string
  refreshToken?: string
  username?: string
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    username: localStorage.getItem('mirai.username') || '',
    pending: false,
  }),
  getters: {
    // 注意：此处不要提供 isAuthenticated getter——只读 localStorage 的 getter 没有响应式依赖，
    // Pinia 会按 computed 缓存首次求值，登录后仍返回 false（联调实测登录后不跳转的根因）。
    // 需要判断登录态时直接读 localStorage.getItem('mirai.accessToken')。
  },
  actions: {
    async login(username: string, password: string) {
      this.pending = true
      try {
        if (import.meta.env.MIRAI_USE_MOCK === '1') {
          // mock 模式：任意账号可登录（仅 UI 开发）
          setAccessToken('mock-token')
          localStorage.setItem('mirai.username', username)
          this.username = username
          return
        }
        const authResp = await unwrap<AuthResponse>(
          http.post('/auth/login', { usernameOrEmail: username, password }),
        )
        setAccessToken(authResp.accessToken)
        localStorage.setItem('mirai.username', authResp.username ?? username)
        this.username = authResp.username ?? username
      } finally {
        this.pending = false
      }
    },
    logout() {
      setAccessToken(null)
      localStorage.removeItem('mirai.username')
      this.username = ''
      void http.post('/auth/logout').catch(() => {})
      location.href = '/login'
    },
  },
})

// 401 兜底：跳登录页（router 守卫同样兜底）
bindUnauthorizedHandler(() => {
  location.href = '/login'
})
