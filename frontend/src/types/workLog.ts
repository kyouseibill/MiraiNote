export interface WorkLog {
  id: number
  title: string
  purpose: string | null
  content: string | null
  tags: string | null
  category: string | null
  logDate: string
  createdAt: string
  updatedAt: string
}

export interface CreateWorkLogPayload {
  title: string
  purpose?: string | null
  content?: string | null
  tags?: string | null
  category?: string | null
  logDate: string
}

export type UpdateWorkLogPayload = CreateWorkLogPayload

export interface WorkLogListQuery {
  page?: number
  pageSize?: number
  keyword?: string
  category?: string
  tag?: string
  dateFrom?: string
  dateTo?: string
}
