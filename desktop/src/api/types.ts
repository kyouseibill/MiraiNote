// ============================================================
// Mirai M1 API 契约类型（副本）
// 权威版本：docs/contracts/types.ts — 契约变更由主 Agent 同步两处，子 Agent 勿改
// ============================================================

// ---------- 通用（沿用现有 API） ----------
export interface ApiResponse<T> {
  success: boolean
  data: T | null
  message: string | null
}

export interface PagedResult<T> {
  page: number
  pageSize: number
  total: number
  items: T[]
}

// ---------- 枚举 ----------
export const InboxSource = {
  HotkeyCapture: 1,
  TodayBar: 2,
  Manual: 3,
  Retriage: 4,
} as const
export type InboxSource = (typeof InboxSource)[keyof typeof InboxSource]

export const InboxStatus = {
  Pending: 0,
  Triaging: 1,
  Triaged: 2,
  Dispatched: 3,
  Discarded: 4,
  Error: 5,
} as const
export type InboxStatus = (typeof InboxStatus)[keyof typeof InboxStatus]

export const INBOX_STATUS_TEXT: Record<InboxStatus, string> = {
  0: '待分拣',
  1: '分拣中',
  2: '已分拣 · 待确认',
  3: '已分发',
  4: '已丢弃',
  5: '分拣失败',
}

export type TriageSuggestionType = 'task' | 'worklog' | 'lifelog' | 'knowledge' | 'ignore'
export type SessionType = 'legacy' | 'command' | 'context'
export type AttachToType = 'worklog' | 'lifelog' | 'memo' | 'inbox' | 'briefing'
export type SourceRefType = AttachToType | 'chat'
export type MemoSection = 'work' | 'life'
export type TaskPriority = 1 | 2 | 3
export type FeedKind = 'capture' | 'worklog' | 'lifelog' | 'memo' | 'task' | 'briefing'

// ---------- 分拣结果（AiParse JSON 结构） ----------
export interface TaskSuggestionFields {
  content: string
  /** 客户端本地时间，无时区后缀；换算 UTC 由服务端在 dispatch 时完成 */
  remindAtLocal: string | null
  priority: TaskPriority
  section: MemoSection
}

export interface WorklogSuggestionFields {
  title: string
  content: string
  tags: string[]
  category: string | null
}

export interface LifelogSuggestionFields {
  content: string
  mood: string | null
}

export type SuggestionFields = TaskSuggestionFields | WorklogSuggestionFields | LifelogSuggestionFields | null

export interface TriageSuggestion {
  suggestionId: string
  type: TriageSuggestionType
  confidence: number
  rationale: string
  fields: SuggestionFields
}

export interface TriageResult {
  items: TriageSuggestion[]
  uncertain: string[]
}

// ---------- Inbox ----------
export interface InboxItem {
  id: number
  raw: string
  source: InboxSource
  status: InboxStatus
  aiParse: TriageResult | null
  aiModel: string | null
  correctionNote: string | null
  error: string | null
  triagedAt: string | null
  createdAt: string
}

export interface CreateInboxItemRequest {
  raw: string
  source: InboxSource
  localTime: string
  tzOffsetMinutes: number
}

export interface RetriageRequest {
  correction?: string
}

export interface DispatchItem {
  suggestionId: string
  overrides?: Record<string, unknown>
}

export interface DispatchRequest {
  items: DispatchItem[]
}

export interface CreatedRef {
  suggestionId: string
  type: TriageSuggestionType
  id: number
  title: string
}

export interface DispatchResult {
  inboxItemId: number
  created: CreatedRef[]
}

export interface InboxListQuery {
  status?: InboxStatus
  page?: number
  pageSize?: number
}

// ---------- 晨报与今日流 ----------
export interface SourceRef {
  type: SourceRefType
  id: number
  title: string
}

export interface Briefing {
  id: number
  date: string
  content: string
  sources: SourceRef[]
  model: string
  generatedAt: string
}

export interface DueTask {
  id: number
  content: string
  remindAt: string | null
  priority: TaskPriority
  section: MemoSection
  isDone: boolean
  isPinned: boolean
}

export interface FeedItem {
  time: string
  kind: FeedKind
  title: string
  refId: number | null
  aiSummary: string | null
}

export interface DayOverview {
  date: string
  briefing: Briefing | null
  briefingError: string | null
  dueTasks: DueTask[]
  overdueTasks: DueTask[]
  todayFeed: FeedItem[]
  inboxPendingCount: number
  weekEntryCount: number
}

// ---------- AI 统计（设置页） ----------
export interface AiActionStats {
  total: number
  byActionType: Array<{ actionType: string; count: number }>
  last7Days: Array<{ date: string; count: number }>
}

// ---------- Chat 会话扩展（响应沿用现有会话 DTO，此处为新增字段子集） ----------
export interface CreateMiraiSessionRequest {
  title?: string | null
  sessionType?: SessionType
  attachToType?: AttachToType | null
  attachToObjectId?: number | null
}

// ============================================================
// Mock 数据（UI 流离线开发用；与视觉稿 docs/m1-ui-mockups.html 场景一致）
// ============================================================

export const MOCK_TRIAGE_RESULT: TriageResult = {
  items: [
    {
      suggestionId: 's1',
      type: 'task',
      confidence: 0.92,
      rationale: '原文含行动+期限「周三前要排期」',
      fields: {
        content: '推动安全评审排期（老王）',
        remindAtLocal: '2026-08-26T09:00',
        priority: 2,
        section: 'work',
      },
    },
    {
      suggestionId: 's2',
      type: 'task',
      confidence: 0.88,
      rationale: '「记得」= 提醒意图；生日日期原文未提及，未猜测',
      fields: {
        content: '给妈妈买生日礼物',
        remindAtLocal: '2026-08-26T09:00',
        priority: 1,
        section: 'life',
      },
    },
    {
      suggestionId: 's3',
      type: 'worklog',
      confidence: 0.81,
      rationale: '工作事实记录',
      fields: {
        title: '安全评审排期待推进',
        content: '重构方案需通过安全评审，老王负责排期，截止周三前。',
        tags: ['重构方案', '安全评审'],
        category: null,
      },
    },
  ],
  uncertain: ['「妈的生日」具体日期原文未给出，提醒时间默认设为周三 09:00，请在确认时核对'],
}

export const MOCK_INBOX_ITEMS: InboxItem[] = [
  {
    id: 101,
    raw: '重构方案要过安全评审，老王周三前要排期，顺便记得给妈买生日礼物',
    source: 1,
    status: 2,
    aiParse: MOCK_TRIAGE_RESULT,
    aiModel: 'deepseek-v4-flash',
    correctionNote: null,
    error: null,
    triagedAt: '2026-08-22T01:20:08Z',
    createdAt: '2026-08-22T01:20:00Z',
  },
  {
    id: 100,
    raw: 'Go 1.24 的迭代器模式挺有意思',
    source: 2,
    status: 1,
    aiParse: null,
    aiModel: null,
    correctionNote: null,
    error: null,
    triagedAt: null,
    createdAt: '2026-08-22T00:58:00Z',
  },
  {
    id: 99,
    raw: '（一段会议录音转写）',
    source: 3,
    status: 5,
    aiParse: null,
    aiModel: null,
    correctionNote: null,
    error: '模型响应超时（25s），已重试 1 次仍失败',
    triagedAt: null,
    createdAt: '2026-08-21T14:02:00Z',
  },
]

export const MOCK_BRIEFING: Briefing = {
  id: 12,
  date: '2026-08-22',
  content:
    '今天有 **3 件到期事项**，最要紧的是「推动安全评审排期」——你 8/19 记录过老王要求周三前给排期结论。\n昨天你完成了迁移方案 v3 的修订，本周该主题已积累 6 条记录。\n收件箱积压 3 条，最早的一条已放 2 天，建议抽空处理。',
  sources: [
    { type: 'worklog', id: 231, title: '安全评审要求' },
    { type: 'worklog', id: 238, title: '迁移方案 v3 修订完成' },
    { type: 'inbox', id: 97, title: '收件箱积压' },
  ],
  model: 'deepseek-v4-flash',
  generatedAt: '2026-08-21T23:30:00Z',
}

export const MOCK_DAY_OVERVIEW: DayOverview = {
  date: '2026-08-22',
  briefing: MOCK_BRIEFING,
  briefingError: null,
  dueTasks: [
    {
      id: 501,
      content: '推动安全评审排期',
      remindAt: '2026-08-22T06:00:00Z',
      priority: 3,
      section: 'work',
      isDone: false,
      isPinned: true,
    },
  ],
  overdueTasks: [],
  todayFeed: [
    { time: '2026-08-21T23:30:00Z', kind: 'briefing', title: '晨报已生成', refId: 12, aiSummary: null },
    { time: '2026-08-22T01:20:00Z', kind: 'capture', title: '捕获 1 条，AI 分拣出 2 条任务 + 1 条记录草稿', refId: 101, aiSummary: null },
    { time: '2026-08-22T01:41:00Z', kind: 'worklog', title: '迁移方案 v3 修订完成', refId: 238, aiSummary: null },
  ],
  inboxPendingCount: 3,
  weekEntryCount: 23,
}

export const MOCK_DISPATCH_RESULT: DispatchResult = {
  inboxItemId: 101,
  created: [
    { suggestionId: 's1', type: 'task', id: 501, title: '推动安全评审排期（老王）' },
    { suggestionId: 's2', type: 'task', id: 502, title: '给妈妈买生日礼物' },
  ],
}

export const MOCK_AI_STATS: AiActionStats = {
  total: 128,
  byActionType: [
    { actionType: 'inbox_dispatch', count: 96 },
    { actionType: 'inbox_discard', count: 18 },
    { actionType: 'briefing_regenerate', count: 14 },
  ],
  last7Days: [
    { date: '2026-08-16', count: 12 },
    { date: '2026-08-17', count: 19 },
    { date: '2026-08-18', count: 24 },
    { date: '2026-08-19', count: 16 },
    { date: '2026-08-20', count: 21 },
    { date: '2026-08-21', count: 28 },
    { date: '2026-08-22', count: 8 },
  ],
}
