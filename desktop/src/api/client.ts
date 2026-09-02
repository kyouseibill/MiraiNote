import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import type { ApiResponse } from './types'

// 简化版认证客户端（对齐 frontend/src/api/auth.ts 的模式）。
// M1：token 存内存 + localStorage；M2 升级 tauri-plugin-store / OS 凭据管理器。

let accessToken: string | null = localStorage.getItem('mirai.accessToken')
let onUnauthorized: () => void = () => {}

export function setAccessToken(token: string | null) {
  accessToken = token
  if (token) localStorage.setItem('mirai.accessToken', token)
  else localStorage.removeItem('mirai.accessToken')
}

export function getAccessToken(): string | null {
  return accessToken
}

export function bindUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn
}

export const API_BASE_URL = import.meta.env.MIRAI_API_BASE || 'http://localhost:5273/api/v1'

export const http = axios.create({
  baseURL: API_BASE_URL,
  withCredentials: true, // HttpOnly RefreshToken Cookie
  timeout: 30000,
})

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken && config.headers) {
    config.headers['Authorization'] = `Bearer ${accessToken}`
  }
  return config
})

// 401 → 尝试静默刷新一次（复用现有 /auth/refresh + Cookie 机制）
// 未授权重定向去重：401 风暴时只允许一次整页跳转，防止 401→refresh→401→reload 死循环（联调实测闪烁根因）
let unauthorizedRedirecting = false

http.interceptors.response.use(
  (resp) => resp,
  async (error: AxiosError<ApiResponse<unknown>>) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined
    const status = error.response?.status
    if (status === 401 && original && !original._retry && !original.url?.includes('/auth/')) {
      original._retry = true
      // 仅在持有 accessToken 时尝试静默刷新；登出态的鉴权请求（如角标轮询）直接走未授权处理
      if (accessToken) {
        try {
          const { data } = await http.post<ApiResponse<{ accessToken: string }>>('/auth/refresh')
          if (data.success && data.data) {
            setAccessToken(data.data.accessToken)
            if (original.headers) original.headers['Authorization'] = `Bearer ${data.data.accessToken}`
            return http(original)
          }
        } catch {
          /* fallthrough */
        }
      }
      setAccessToken(null)
      if (!unauthorizedRedirecting) {
        unauthorizedRedirecting = true
        onUnauthorized()
        setTimeout(() => (unauthorizedRedirecting = false), 2000)
      }
    }
    return Promise.reject(error)
  },
)

/** 解包信封：成功返回 data，失败抛 Error(message) */
export async function unwrap<T>(promise: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  const resp = await promise
  if (!resp.data.success) {
    throw new Error(resp.data.message || '请求失败')
  }
  return resp.data.data as T
}
