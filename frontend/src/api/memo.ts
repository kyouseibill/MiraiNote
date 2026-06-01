import { http, unwrap } from './auth'
import type { PagedResult } from '@/types/common'
import type {
  Memo,
  CreateMemoPayload,
  UpdateMemoPayload,
  PatchMemoStatusPayload,
  MemoListQuery,
} from '@/types/memo'

export const memoApi = {
  list: (query: MemoListQuery) =>
    unwrap<PagedResult<Memo>>(http.get('/memos', { params: query })),

  create: (payload: CreateMemoPayload) =>
    unwrap<Memo>(http.post('/memos', payload)),

  update: (id: number, payload: UpdateMemoPayload) =>
    unwrap<Memo>(http.put(`/memos/${id}`, payload)),

  patchStatus: (id: number, payload: PatchMemoStatusPayload) =>
    unwrap<Memo>(http.patch(`/memos/${id}/status`, payload)),

  remove: (id: number) => unwrap<null>(http.delete(`/memos/${id}`)),

  /** 拉取到期且需弹窗、未确认的备忘 */
  duePopups: () => unwrap<Memo[]>(http.get('/memos/due-popups')),

  /** 用户关闭弹窗 */
  acknowledgePopup: (id: number) =>
    unwrap<null>(http.patch(`/memos/${id}/acknowledge-popup`)),
}
