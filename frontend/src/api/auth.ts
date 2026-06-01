import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import type {
  ApiResponse,
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  VerifyEmailRequest,
  ResendVerifyEmailRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
} from '@/types/auth'
import { useToast } from '@/composables/useToast'

// 仅用于存取内存中的 accessToken，避免循环依赖 store
let accessTokenGetter: () => string | null = () => null
let onUnauthorized: () => void = () => {}
let onRefreshed: (resp: AuthResponse) => void = () => {}

export function bindAuthHooks(opts: {
  getAccessToken: () => string | null
  onUnauthorized: () => void
  onRefreshed: (resp: AuthResponse) => void
}) {
  accessTokenGetter = opts.getAccessToken
  onUnauthorized = opts.onUnauthorized
  onRefreshed = opts.onRefreshed
}

const baseURL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5273/api/v1'

export const http = axios.create({
  baseURL,
  withCredentials: true, // 携带 HttpOnly RefreshToken Cookie
  timeout: 15000,
})

// 请求拦截：附加 Authorization
http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = accessTokenGetter()
  if (token && config.headers) {
    config.headers['Authorization'] = `Bearer ${token}`
  }
  return config
})

// 刷新队列，防止并发刷新
let isRefreshing = false
let waiters: Array<(token: string | null) => void> = []

function flushWaiters(token: string | null) {
  waiters.forEach((cb) => cb(token))
  waiters = []
}

// 响应拦截：401 自动刷新一次
http.interceptors.response.use(
  (resp) => resp,
  async (error: AxiosError<ApiResponse>) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined
    const status = error.response?.status

    // 401 且非刷新接口本身且未重试过
    if (
      status === 401 &&
      original &&
      !original._retry &&
      !original.url?.includes('/auth/refresh') &&
      !original.url?.includes('/auth/login')
    ) {
      original._retry = true

      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          waiters.push((token) => {
            if (token) {
              if (original.headers) original.headers['Authorization'] = `Bearer ${token}`
              resolve(http(original))
            } else {
              reject(error)
            }
          })
        })
      }

      isRefreshing = true
      try {
        const { data } = await http.post<ApiResponse<AuthResponse>>('/auth/refresh')
        if (data.success && data.data) {
          onRefreshed(data.data)
          flushWaiters(data.data.accessToken)
          if (original.headers) original.headers['Authorization'] = `Bearer ${data.data.accessToken}`
          return http(original)
        }
        throw error
      } catch (e) {
        flushWaiters(null)
        onUnauthorized()
        throw e
      } finally {
        isRefreshing = false
      }
    }

    // 统一错误 toast
    const toast = useToast()
    const msg = error.response?.data?.message || error.message || '网络错误'
    if (status !== 401) toast.error(msg)
    return Promise.reject(error)
  },
)

// 解包工具：返回 data，失败抛 Error(message)
export async function unwrap<T>(promise: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  const resp = await promise
  if (!resp.data.success) {
    throw new Error(resp.data.message || '请求失败')
  }
  return resp.data.data as T
}

// ============ Auth APIs ============

export const authApi = {
  register: (payload: RegisterRequest) =>
    unwrap<null>(http.post('/auth/register', payload)),

  login: (payload: LoginRequest) =>
    unwrap<AuthResponse>(http.post('/auth/login', payload)),

  logout: () => unwrap<null>(http.post('/auth/logout')),

  refresh: () => unwrap<AuthResponse>(http.post('/auth/refresh')),

  verifyEmail: (payload: VerifyEmailRequest) =>
    unwrap<null>(http.post('/auth/verify-email', payload)),

  resendVerify: (payload: ResendVerifyEmailRequest) =>
    unwrap<null>(http.post('/auth/resend-verify', payload)),

  forgotPassword: (payload: ForgotPasswordRequest) =>
    unwrap<null>(http.post('/auth/forgot-password', payload)),

  resetPassword: (payload: ResetPasswordRequest) =>
    unwrap<null>(http.post('/auth/reset-password', payload)),

  changePassword: (payload: ChangePasswordRequest) =>
    unwrap<null>(http.put('/auth/change-password', payload)),
}
