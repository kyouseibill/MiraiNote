import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { AuthResponse, LoginRequest, RegisterRequest, User } from '@/types/auth'
import { authApi, bindAuthHooks } from '@/api/auth'

export const useAuthStore = defineStore('auth', () => {
  // accessToken 仅存内存
  const accessToken = ref<string | null>(null)
  const accessTokenExpiresAt = ref<string | null>(null)
  const user = ref<User | null>(null)

  const isAuthenticated = computed(() => !!accessToken.value && !!user.value)
  const isAdmin = computed(() => !!user.value?.isAdmin)

  function setAuth(resp: AuthResponse) {
    accessToken.value = resp.accessToken
    accessTokenExpiresAt.value = resp.accessTokenExpiresAt
    user.value = resp.user
  }

  function clearAuth() {
    accessToken.value = null
    accessTokenExpiresAt.value = null
    user.value = null
  }

  async function login(payload: LoginRequest) {
    const resp = await authApi.login(payload)
    setAuth(resp)
    return resp
  }

  async function register(payload: RegisterRequest) {
    await authApi.register(payload)
  }

  async function logout() {
    try {
      await authApi.logout()
    } catch {
      // 忽略，本地清理
    } finally {
      clearAuth()
    }
  }

  // 静默刷新（应用启动时尝试）
  async function tryRefresh(): Promise<boolean> {
    try {
      const resp = await authApi.refresh()
      setAuth(resp)
      return true
    } catch {
      clearAuth()
      return false
    }
  }

  // 绑定 axios 钩子，避免循环依赖
  bindAuthHooks({
    getAccessToken: () => accessToken.value,
    onUnauthorized: () => clearAuth(),
    onRefreshed: (resp) => setAuth(resp),
  })

  return {
    accessToken,
    accessTokenExpiresAt,
    user,
    isAuthenticated,
    isAdmin,
    login,
    register,
    logout,
    tryRefresh,
    setAuth,
    clearAuth,
  }
})
