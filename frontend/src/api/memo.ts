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

  patchStatus: async (id: number, payload: PatchMemoStatusPayload) => {
    // 优先使用 PUT，兼容 Cloudflare/IIS 等可能拦截 PATCH 的环境
    try {
      return await unwrap<Memo>(http.put(`/memos/${id}/status`, payload))
    } catch {
      return await unwrap<Memo>(http.patch(`/memos/${id}/status`, payload))
    }
  },

  remove: (id: number) => unwrap<null>(http.delete(`/memos/${id}`)),

  /** 拉取到期且需弹窗、未确认的备忘 */
  duePopups: () => unwrap<Memo[]>(http.get('/memos/due-popups')),

  /** 用户关闭弹窗 */
  acknowledgePopup: async (id: number) => {
    try {
      return await unwrap<null>(http.put(`/memos/${id}/acknowledge-popup`))
    } catch {
      return await unwrap<null>(http.patch(`/memos/${id}/acknowledge-popup`))
    }
  },
}
