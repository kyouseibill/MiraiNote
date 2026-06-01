export type MemoSection = 'work' | 'life'

/** 提醒方式位标志：0=不提醒, 1=弹窗, 2=邮件, 3=弹窗+邮件 */
export const ReminderMethod = {
  None: 0,
  Popup: 1,
  Email: 2,
} as const

export interface Memo {
  id: number
  section: MemoSection
  content: string
  remindAt: string | null
  remindMethods: number
  emailReminderSent: boolean
  popupAcknowledged: boolean
  remindedAt: string | null
  priority: number // 1=低 2=中 3=高
  isPinned: boolean
  isDone: boolean
  isArchived: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateMemoPayload {
  section: MemoSection
  content: string
  remindAt?: string | null
  remindMethods?: number
  priority?: number
  isPinned?: boolean
}

export interface UpdateMemoPayload {
  content: string
  remindAt?: string | null
  remindMethods?: number
  priority?: number
  isPinned?: boolean
}

export interface PatchMemoStatusPayload {
  isDone?: boolean | null
  isPinned?: boolean | null
  isArchived?: boolean | null
}

export interface MemoListQuery {
  section: MemoSection
  page?: number
  pageSize?: number
  keyword?: string
  includeArchived?: boolean
  includeDone?: boolean
}
