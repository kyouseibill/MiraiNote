import axios from 'axios'

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

export function apiFailure(error: unknown, fallback = '请求失败'): ApiFailure {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { message?: string } | undefined
    const fromBody = typeof data?.message === 'string' ? data.message : ''
    return {
      status: error.response?.status ?? null,
      message: readable(fromBody, fallback),
    }
  }
  if (error instanceof Error) {
    const status = 'status' in error && typeof error.status === 'number' ? error.status : null
    return { status, message: readable(error.message, fallback) }
  }
  return { status: null, message: fallback }
}

export function isAxiosError(error: unknown): boolean {
  return axios.isAxiosError(error)
}
