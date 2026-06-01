import { http, unwrap } from './auth'
import type {
  WeeklyReport,
  WeeklyReportReference,
  GenerateReportPayload,
  UpdateReportPayload,
} from '@/types/weeklyReport'

export const weeklyReportApi = {
  generate: (payload: GenerateReportPayload) =>
    unwrap<WeeklyReport>(http.post('/reports/generate', payload)),

  list: () => unwrap<WeeklyReport[]>(http.get('/reports')),

  get: (id: number) => unwrap<WeeklyReport>(http.get(`/reports/${id}`)),

  update: (id: number, payload: UpdateReportPayload) =>
    unwrap<WeeklyReport>(http.put(`/reports/${id}`, payload)),

  remove: (id: number) => unwrap<null>(http.delete(`/reports/${id}`)),

  // 参考文件
  listReferences: () =>
    unwrap<WeeklyReportReference[]>(http.get('/report-references')),

  uploadReference: (
    file: File,
    weekStart?: string,
    weekEnd?: string,
    remark?: string,
  ) => {
    const form = new FormData()
    form.append('file', file)
    if (weekStart) form.append('weekStart', weekStart)
    if (weekEnd) form.append('weekEnd', weekEnd)
    if (remark) form.append('remark', remark)
    return unwrap<WeeklyReportReference>(
      http.post('/report-references', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }),
    )
  },

  deleteReference: (id: number) =>
    unwrap<null>(http.delete(`/report-references/${id}`)),
}
