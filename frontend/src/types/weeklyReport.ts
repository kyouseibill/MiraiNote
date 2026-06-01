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
