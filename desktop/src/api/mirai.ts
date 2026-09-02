import { http, unwrap } from './client'
import { mockDb } from './mockState'
import type {
  AiActionStats,
  Briefing,
  CreateInboxItemRequest,
  DayOverview,
  DispatchRequest,
  DispatchResult,
  InboxItem,
  InboxListQuery,
  PagedResult,
  RetriageRequest,
} from './types'

// MIRAI_USE_MOCK=1 时走本地 mock（UI 流离线开发）；契约见 docs/contracts/api-contract.md
// mock 数据由 mockState 内存库提供（支持捕获→分拣→分发→撤销的完整状态流转）。
const USE_MOCK = import.meta.env.MIRAI_USE_MOCK === '1'

function mock<T>(value: T, ms = 400): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms))
}

function localTimeNow(): { localTime: string; tzOffsetMinutes: number } {
  const now = new Date()
  return {
    localTime: now.toISOString().slice(0, 19),
    tzOffsetMinutes: -now.getTimezoneOffset(),
  }
}

export const miraiApi = {
  createInbox: (raw: string, source: CreateInboxItemRequest['source']): Promise<InboxItem> => {
    if (USE_MOCK) return mockDb.createInbox(raw, source)
    return unwrap(http.post('/mirai/inbox', { raw, source, ...localTimeNow() }))
  },

  inboxList: (query: InboxListQuery = {}): Promise<PagedResult<InboxItem>> => {
    if (USE_MOCK) {
      let items = mockDb.inboxList()
      if (query.status != null) items = items.filter((it) => it.status === query.status)
      const page = query.page ?? 1
      const pageSize = query.pageSize ?? 50
      return mock({
        page,
        pageSize,
        total: items.length,
        items: items.slice((page - 1) * pageSize, page * pageSize),
      })
    }
    return unwrap(http.get('/mirai/inbox', { params: query }))
  },

  /** 导航角标用：待处理（Pending/Triaging/Triaged）数量 */
  inboxPendingCount: (): Promise<number> => {
    if (USE_MOCK) return Promise.resolve(mockDb.inboxPendingCount())
    return unwrap<PagedResult<InboxItem>>(http.get('/mirai/inbox', { params: { page: 1, pageSize: 200 } })).then(
      (page) => page.items.filter((it) => it.status !== 3 && it.status !== 4).length,
    )
  },

  retriage: (id: number, req: RetriageRequest = {}): Promise<InboxItem> => {
    if (USE_MOCK) return mockDb.retriage(id, req)
    return unwrap(http.post(`/mirai/inbox/${id}/retriage`, req))
  },

  dispatch: (id: number, req: DispatchRequest): Promise<DispatchResult> => {
    if (USE_MOCK) return mockDb.dispatch(id, req)
    return unwrap(http.post(`/mirai/inbox/${id}/dispatch`, req))
  },

  discard: (id: number): Promise<null> => {
    if (USE_MOCK) return mockDb.discard(id)
    return unwrap(http.post(`/mirai/inbox/${id}/discard`))
  },

  undo: (id: number): Promise<null> => {
    if (USE_MOCK) return mockDb.undo(id)
    return unwrap(http.post(`/mirai/inbox/${id}/undo`))
  },

  dayOverview: (date: string): Promise<DayOverview> => {
    if (USE_MOCK) return mock(mockDb.dayOverview(date), 500)
    return unwrap(http.get('/mirai/day/overview', { params: { date } }))
  },

  regenerateBriefing: (date: string): Promise<Briefing> => {
    if (USE_MOCK) return mockDb.regenerateBriefing(date)
    return unwrap(http.post('/mirai/briefing/regenerate', { date }))
  },

  aiStats: (): Promise<AiActionStats> => {
    if (USE_MOCK) return mockDb.aiStats()
    return unwrap(http.get('/mirai/stats/ai-actions'))
  },

  /** 到期任务 → AI 关联上下文（客户端渲染；真实模式下无数据则隐藏该行） */
  taskContextOf: (taskId: number): { text: string; worklogId: number } | null => {
    return USE_MOCK ? mockDb.taskContextOf(taskId) : null
  },
}
