import axios, { type AxiosError } from 'axios'

export interface ApiFailure {
  status: number | null
  message: string
}

const axiosStatusText = /^Request failed with status code \d+$/i

function readable(candidate: string | undefined, fallback: string): string {
  const text = candidate?.trim() ?? ''
  if (!text || text === 'Network Error' || axiosStatusText.test(text)) return fallback
  return text
}

export const SERVER_ERROR_MESSAGE = '服务器出错了，请稍后再试'
export const NETWORK_ERROR_MESSAGE = '网络错误'

export function apiFailure(error: unknown, fallback = '请求失败'): ApiFailure {
  if (axios.isAxiosError(error)) {
    if (!error.response) {
      return { status: null, message: NETWORK_ERROR_MESSAGE }
    }
    const status = error.response.status ?? null
    const data = error.response.data as { message?: string } | undefined
    const fromBody = typeof data?.message === 'string' ? data.message : ''
    if (status != null && status >= 500 && !fromBody.trim()) {
      return { status, message: SERVER_ERROR_MESSAGE }
    }
    return {
      status,
      message: readable(fromBody, fallback),
    }
  }
  if (error instanceof Error) {
    const status = 'status' in error && typeof error.status === 'number' ? error.status : null
    return { status, message: readable(error.message, fallback) }
  }
  return { status: null, message: fallback }
}

export function isAxiosError(error: unknown): error is AxiosError {
  return axios.isAxiosError(error)
}
