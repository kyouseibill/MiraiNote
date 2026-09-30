import axios from 'axios'

/** 与 HouseholdAccessService.PendingInvitationMessage 一致。页面不直接展示这句。 */
export const pendingInvitationConflictMessage = '请先处理家庭邀请'

export interface ApiFailure {
  status: number | null
  message: string
}

export function isPendingInvitationConflict(error: unknown): boolean {
  const failure = apiFailure(error)
  return failure.status === 409 && failure.message === pendingInvitationConflictMessage
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
