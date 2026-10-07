import axios from 'axios'

export interface ApiFailure {
  status: number | null
  message: string
}

export function apiFailure(error: unknown): ApiFailure {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { message?: string } | undefined
    const message = data?.message?.trim() || error.message || '请求失败'
    return { status: error.response?.status ?? null, message }
  }
  if (error instanceof Error) {
    const status = 'status' in error && typeof error.status === 'number' ? error.status : null
    return { status, message: error.message || '请求失败' }
  }
  return { status: null, message: '请求失败' }
}

export function isAxiosError(error: unknown): boolean {
  return axios.isAxiosError(error)
}
