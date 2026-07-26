export interface WeeklyReport {
  id: number
  weekStart: string
  weekEnd: string
  content: string
  generatedAt: string
  isEdited: boolean
  createdAt: string
  updatedAt: string
}

export interface GenerateReportPayload {
  weekStart: string
  weekEnd: string
  /** 输出内容复杂度：1=简洁，2=标准，3=详细 */
  detailLevel: number
}

export interface UpdateReportPayload {
  content: string
}

export interface WeeklyReportReference {
  id: number
  fileName: string
  weekStart: string | null
  weekEnd: string | null
  remark: string | null
  createdAt: string
}
