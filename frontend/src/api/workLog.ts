import { http, unwrap } from './auth'
import type { PagedResult } from '@/types/common'
import type {
  WorkLog,
  CreateWorkLogPayload,
  UpdateWorkLogPayload,
  WorkLogListQuery,
} from '@/types/workLog'

export const workLogApi = {
  list: (query: WorkLogListQuery) =>
    unwrap<PagedResult<WorkLog>>(http.get('/worklogs', { params: query })),

  get: (id: number) => unwrap<WorkLog>(http.get(`/worklogs/${id}`)),

  create: (payload: CreateWorkLogPayload) =>
    unwrap<WorkLog>(http.post('/worklogs', payload)),

  update: (id: number, payload: UpdateWorkLogPayload) =>
    unwrap<WorkLog>(http.put(`/worklogs/${id}`, payload)),

  remove: (id: number) => unwrap<null>(http.delete(`/worklogs/${id}`)),
}
