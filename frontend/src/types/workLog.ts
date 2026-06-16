/** 工作状态：0=未标记，1=进行中，2=已完成，3=已延期 */
export type WorkLogStatus = 0 | 1 | 2 | 3

export interface WorkLog {
  id: number
  title: string
  purpose: string | null
  content: string | null
  tags: string | null
  category: string | null
  logDate: string
  status: WorkLogStatus
  statusRemark: string | null
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
  status?: WorkLogStatus
  statusRemark?: string | null
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
  status?: WorkLogStatus | null
}
