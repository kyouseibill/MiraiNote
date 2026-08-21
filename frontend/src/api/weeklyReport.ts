import { http, unwrap, getAccessToken, API_BASE_URL } from './auth'
import { consumeSseResponseUntilTerminal } from './sse'
import type {
  WeeklyReport,
  WeeklyReportReference,
  GenerateReportPayload,
  UpdateReportPayload,
} from '@/types/weeklyReport'

export type WeeklyReportSseEventType =
  | 'token'
  | 'heartbeat'
  | 'done'
  | 'error'

export interface WeeklyReportSseEvent {
  type: WeeklyReportSseEventType
  data: any
}

export type WeeklyReportSseCallback = (event: WeeklyReportSseEvent) => void

export const weeklyReportApi = {
  generate: (payload: GenerateReportPayload) =>
    unwrap<WeeklyReport>(http.post('/reports/generate', payload)),

  /**
   * 流式生成周报，通过 onEvent 回调接收 SSE 事件。
   * 原生 fetch（不走 axios），绕开全局 15s 超时。
   */
  generateStream: async (
    payload: GenerateReportPayload,
    onEvent: WeeklyReportSseCallback,
    signal?: AbortSignal,
  ): Promise<void> => {
    const token = getAccessToken()
    const response = await fetch(`${API_BASE_URL}/reports/generate/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(payload),
      credentials: 'include',  // 携带 HttpOnly Cookie（RefreshToken）
      signal,
    })

    if (!response.ok) {
      onEvent({ type: 'error', data: { message: `HTTP ${response.status}: ${response.statusText}` } })
      return
    }

    await consumeSseResponseUntilTerminal(response, (event) => {
      onEvent({ type: event.type as WeeklyReportSseEventType, data: event.data })
    }, signal)
  },

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
