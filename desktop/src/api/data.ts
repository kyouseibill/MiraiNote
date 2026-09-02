// ============================================================
// 现有域 API（worklogs / lifelogs / memos / reports / agent-memories）
// 从 frontend/src/api/{workLog,lifeLog,memo,weeklyReport,agent}.ts 复制适配到 desktop；
// mock 分支读写 mockState 内存库。契约见 docs/contracts/api-contract.md §0（沿用现有端点）。
// ============================================================
import { http, unwrap } from './client'
import { mockDb } from './mockState'
import type { PagedResult } from './types'

const USE_MOCK = import.meta.env.MIRAI_USE_MOCK === '1'

function mock<T>(value: T, ms = 300): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms))
}

// ---------- 类型（frontend/src/types/{workLog,lifeLog,memo}.ts 的副本） ----------
export type WorkLogStatus = 0 | 1 | 2 | 3
export const WORKLOG_STATUS_TEXT: Record<WorkLogStatus, string> = {
  0: '未标记',
  1: '进行中',
  2: '已完成',
  3: '已延期',
}

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
  /** 客户端扩展：AI 摘要（真实接口 M1 恒空 → UI 隐藏该行） */
  aiSummary?: string | null
}

export interface WorkLogPayload {
  title: string
  purpose?: string | null
  content?: string | null
  tags?: string | null
  category?: string | null
  logDate: string
  status?: WorkLogStatus
  statusRemark?: string | null
}

export interface LifeLog {
  id: number
  content: string
  mood: string | null
  imagePath: string | null
  imagePaths: string[]
  logDate: string
  createdAt: string
  updatedAt: string
}

export interface LifeLogPayload {
  content: string
  mood?: string | null
  imagePath?: string | null
  imagePaths?: string[]
  logDate: string
}

export interface Memo {
  id: number
  section: 'work' | 'life'
  content: string
  remindAt: string | null
  remindMethods: number
  emailReminderSent: boolean
  popupAcknowledged: boolean
  remindedAt: string | null
  priority: number // 1=低 2=中 3=高
  isPinned: boolean
  isDone: boolean
  isArchived: boolean
  createdAt: string
  updatedAt: string
  /** 客户端扩展：任务 → 关联记录（渲染「AI 上下文」行；真实接口无此字段时隐藏） */
  contextWorklogId?: number | null
}

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

export interface AgentMemoryDto {
  id: number
  key: string
  value: string
  category: string
  tags: string | null
  importance: number
  accessedCount: number
  source: string | null
  createdAt: string
  updatedAt: string
}

export interface CreateMemoryRequest {
  key: string
  value: string
  category?: string
  tags?: string | null
  importance?: number
  source?: string | null
}

// ---------- mock 种子：历史周报（视觉稿 ⑦「23 篇」） ----------
const MOCK_REPORTS: WeeklyReport[] = Array.from({ length: 23 }, (_, i) => {
  const weekStart = new Date(Date.UTC(2026, 7, 22) - (i + 1) * 7 * 86400000)
  const weekEnd = new Date(weekStart.getTime() + 6 * 86400000)
  const fmt = (d: Date) => d.toISOString().slice(0, 10)
  return {
    id: 100 - i,
    weekStart: fmt(weekStart),
    weekEnd: fmt(weekEnd),
    content:
      `## 本周概览\n\n- 工作记录 ${8 + (i % 5)} 条，聚焦迁移方案与安全评审推进\n- 到期任务完成率 ${70 + ((i * 7) % 25)}%\n\n## 下周计划\n\n- 推动安全评审排期结论落地\n- 存量数据差异清零`,
    generatedAt: weekEnd.toISOString(),
    isEdited: i % 6 === 0,
    createdAt: weekEnd.toISOString(),
    updatedAt: weekEnd.toISOString(),
  }
})

let mockNextMemoryId = 1

// ---------- API ----------
export const workLogApi = {
  list: (): Promise<WorkLog[]> => {
    if (USE_MOCK) return mock(mockDb.listWorklogs())
    return unwrap<PagedResult<WorkLog>>(http.get('/worklogs', { params: { page: 1, pageSize: 100 } })).then((p) => p.items)
  },
  get: (id: number): Promise<WorkLog> => {
    if (USE_MOCK) {
      const wl = mockDb.getWorklog(id)
      return wl ? mock(wl) : Promise.reject(new Error('工作记录不存在'))
    }
    return unwrap(http.get(`/worklogs/${id}`))
  },
  save: (payload: WorkLogPayload & { id?: number }): Promise<WorkLog> => {
    if (USE_MOCK) return mock(mockDb.saveWorklog(payload))
    if (payload.id != null) return unwrap(http.put(`/worklogs/${payload.id}`, payload))
    return unwrap(http.post('/worklogs', payload))
  },
}

export const lifeLogApi = {
  list: (): Promise<LifeLog[]> => {
    if (USE_MOCK) return mock(mockDb.listLifelogs())
    return unwrap<PagedResult<LifeLog>>(http.get('/lifelogs', { params: { page: 1, pageSize: 100 } })).then((p) => p.items)
  },
  get: (id: number): Promise<LifeLog> => {
    if (USE_MOCK) {
      const ll = mockDb.getLifelog(id)
      return ll ? mock(ll) : Promise.reject(new Error('生活记录不存在'))
    }
    return unwrap(http.get(`/lifelogs/${id}`))
  },
  save: (payload: LifeLogPayload & { id?: number }): Promise<LifeLog> => {
    if (USE_MOCK) return mock(mockDb.saveLifelog(payload))
    if (payload.id != null) return unwrap(http.put(`/lifelogs/${payload.id}`, payload))
    return unwrap(http.post('/lifelogs', payload))
  },
}

export const memoApi = {
  list: (): Promise<Memo[]> => {
    if (USE_MOCK) return mock(mockDb.listMemos())
    // 现有接口按 section 查询；两个分区合并为全量任务列表
    return Promise.all([
      unwrap<PagedResult<Memo>>(http.get('/memos', { params: { section: 'work', page: 1, pageSize: 200, includeDone: true } })),
      unwrap<PagedResult<Memo>>(http.get('/memos', { params: { section: 'life', page: 1, pageSize: 200, includeDone: true } })),
    ]).then((ps) => [...ps[0].items, ...ps[1].items])
  },
  patchStatus: (id: number, patch: { isDone?: boolean | null; isPinned?: boolean | null }): Promise<Memo> => {
    if (USE_MOCK) return mock(mockDb.patchMemoStatus(id, patch))
    // PUT 优先，兼容可能拦截 PATCH 的网关（对齐 frontend/src/api/memo.ts）
    const send = (method: 'put' | 'patch'): Promise<Memo> => unwrap<Memo>(http[method](`/memos/${id}/status`, patch))
    return send('put').catch(() => send('patch'))
  },
  update: (id: number, patch: Partial<Memo>): Promise<Memo> => {
    if (USE_MOCK) return mock(mockDb.updateMemo(id, patch))
    return unwrap(http.put(`/memos/${id}`, {
      content: patch.content,
      remindAt: patch.remindAt,
      priority: patch.priority,
      isPinned: patch.isPinned,
    }))
  },
}

export const reportApi = {
  list: (): Promise<WeeklyReport[]> => {
    if (USE_MOCK) return mock(MOCK_REPORTS, 400)
    return unwrap(http.get('/reports'))
  },
}

export const memoryApi = {
  create: (req: CreateMemoryRequest): Promise<AgentMemoryDto> => {
    if (USE_MOCK) {
      const dto: AgentMemoryDto = {
        id: mockNextMemoryId++,
        key: req.key,
        value: req.value,
        category: req.category ?? 'context',
        tags: req.tags ?? null,
        importance: req.importance ?? 3,
        accessedCount: 0,
        source: req.source ?? null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      return mock(dto, 500)
    }
    return unwrap(http.post('/agent/memories', req))
  },
}
