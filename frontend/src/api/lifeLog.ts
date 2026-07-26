import { http, unwrap } from './auth'
import type { PagedResult } from '@/types/common'
import type {
  LifeLog,
  CreateLifeLogPayload,
  UpdateLifeLogPayload,
  LifeLogListQuery,
} from '@/types/lifeLog'

export const lifeLogApi = {
  list: (query: LifeLogListQuery) =>
    unwrap<PagedResult<LifeLog>>(http.get('/lifelogs', { params: query })),

  get: (id: number) => unwrap<LifeLog>(http.get(`/lifelogs/${id}`)),

  create: (payload: CreateLifeLogPayload) =>
    unwrap<LifeLog>(http.post('/lifelogs', payload)),

  update: (id: number, payload: UpdateLifeLogPayload) =>
    unwrap<LifeLog>(http.put(`/lifelogs/${id}`, payload)),

  remove: (id: number) => unwrap<null>(http.delete(`/lifelogs/${id}`)),

  uploadImage: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return unwrap<string>(http.post('/upload/image', form, {
      timeout: 30000,
    }))
  },
}
