// ============================================================
// Mock 内存数据库（MIRAI_USE_MOCK=1 时启用）
// 目的：让「捕获 → 分拣 → 勾选/改 overrides → 分发 → 撤销」全流程在离线状态下
// 具备真实的状态流转（契约 §2.1–2.6 的客户端镜像）。种子数据与视觉稿场景一致。
// 真实模式下本模块不会被调用。
// ============================================================
import {
  MOCK_AI_STATS,
  MOCK_BRIEFING,
  MOCK_INBOX_ITEMS,
  type AiActionStats,
  type Briefing,
  type DayOverview,
  type DispatchRequest,
  type DispatchResult,
  type DueTask,
  type FeedItem,
  type InboxItem,
  type InboxSource,
  type InboxStatus,
  type MemoSection,
  type RetriageRequest,
  type TaskPriority,
  type TriageResult,
} from './types'
import type { LifeLog, Memo, WorkLog } from './data'

const now = () => new Date()

function isoLocal(): string {
  return now().toISOString().slice(0, 19)
}

function localDate(): string {
  return isoLocal().slice(0, 10)
}

function clone<T>(v: T): T {
  return structuredClone(v)
}

let nextInboxId = 200
let nextTaskId = 600
let nextWorklogId = 240
let nextLifelogId = 80

// ---------- 种子：现有域数据（与视觉稿 ①⑤ 场景一致） ----------
const seedWorklogs: WorkLog[] = [
  {
    id: 238,
    title: '迁移方案 v3 修订完成',
    purpose: '按 8/18 评审意见完成授权改造修订',
    content:
      '按 8/18 评审意见完成 v3 修订：授权改造采用 RBAC 收敛到 3 个角色，去掉了原来的 7 个散落权限点。\n\n## 遗留风险\n\n- 存量数据差异 37 条，校验脚本已覆盖，待业务确认\n- 安全评审排期未定（阻塞项）',
    tags: '重构方案,迁移',
    category: '后端',
    logDate: '2026-08-21',
    status: 1,
    statusRemark: null,
    createdAt: '2026-08-21T01:41:00Z',
    updatedAt: '2026-08-21T01:41:00Z',
    aiSummary: 'v3 完成授权收敛，风险集中在存量数据与安全评审排期',
  },
  {
    id: 237,
    title: '数据校验脚本完成，差异 37 条',
    purpose: '存量数据迁移前校验',
    content: '存量数据校验脚本完成，扫描出 37 条差异，已输出差异清单给业务侧确认。',
    tags: '迁移',
    category: '后端',
    logDate: '2026-08-20',
    status: 2,
    statusRemark: null,
    createdAt: '2026-08-20T06:12:00Z',
    updatedAt: '2026-08-20T06:12:00Z',
    aiSummary: null,
  },
  {
    id: 231,
    title: '安全评审要求（老王）',
    purpose: null,
    content: '老王提出：重构方案必须过安全评审，周三前要给出排期结论，涉及授权改造部分。',
    tags: '安全评审',
    category: null,
    logDate: '2026-08-19',
    status: 3,
    statusRemark: '排期结论尚未给出',
    createdAt: '2026-08-19T08:05:00Z',
    updatedAt: '2026-08-19T08:05:00Z',
    aiSummary: null,
  },
]

const seedLifelogs: LifeLog[] = [
  {
    id: 79,
    content: '沿着海岸慢慢走，风很轻。电车从身旁经过，远处的海面像一张安静铺开的纸。',
    mood: '平静',
    imagePath: null,
    imagePaths: [],
    logDate: '2026-08-21',
    createdAt: '2026-08-21T10:20:00Z',
    updatedAt: '2026-08-21T10:20:00Z',
  },
  {
    id: 78,
    content: '和团队吃了个晚饭，聊到下半年计划，大家都挺有干劲。',
    mood: '开心',
    imagePath: null,
    imagePaths: [],
    logDate: '2026-08-19',
    createdAt: '2026-08-19T13:00:00Z',
    updatedAt: '2026-08-19T13:00:00Z',
  },
]

const seedMemos: Memo[] = [
  {
    id: 501,
    section: 'work',
    content: '推动安全评审排期',
    remindAt: `${localDate()}T14:00:00Z`,
    remindMethods: 3,
    emailReminderSent: false,
    popupAcknowledged: false,
    remindedAt: null,
    priority: 3,
    isPinned: true,
    isDone: false,
    isArchived: false,
    createdAt: '2026-08-22T01:20:10Z',
    updatedAt: '2026-08-22T01:20:10Z',
    contextWorklogId: 231,
  },
  {
    id: 502,
    section: 'life',
    content: '给妈妈买生日礼物',
    remindAt: '2026-08-26T01:00:00Z',
    remindMethods: 1,
    emailReminderSent: false,
    popupAcknowledged: false,
    remindedAt: null,
    priority: 1,
    isPinned: false,
    isDone: false,
    isArchived: false,
    createdAt: '2026-08-22T01:20:10Z',
    updatedAt: '2026-08-22T01:20:10Z',
    contextWorklogId: null,
  },
  {
    id: 498,
    section: 'work',
    content: '回复迁移差异清单邮件',
    remindAt: '2026-08-20T02:00:00Z',
    remindMethods: 1,
    emailReminderSent: false,
    popupAcknowledged: false,
    remindedAt: null,
    priority: 2,
    isPinned: false,
    isDone: false,
    isArchived: false,
    createdAt: '2026-08-20T02:10:00Z',
    updatedAt: '2026-08-20T02:10:00Z',
    contextWorklogId: 237,
  },
]

// 任务 → 关联工作记录（客户端渲染「AI 上下文」行的 mock 数据；真实接口无此字段时隐藏）
const taskContext = new Map<number, { text: string; worklogId: number }>([
  [501, { text: '老王上周三提出的安全评审要求，涉及迁移方案 v3 的授权改造部分', worklogId: 231 }],
  [498, { text: '迁移差异清单 37 条待业务确认，邮件回复前先看 #237 校验记录', worklogId: 237 }],
])

const inboxItems: InboxItem[] = clone(MOCK_INBOX_ITEMS)
const worklogs = clone(seedWorklogs)
const lifelogs = clone(seedLifelogs)
const memos = clone(seedMemos)

// 撤销栈：dispatch 创建的对象，undo 时软删
const lastDispatch = new Map<number, DispatchResult>()

// ---------- 分拣合成 ----------
function synthesizeTriage(raw: string): TriageResult {
  // 简单启发式：让新捕获的内容也能得到一组可交互的建议（演示字段级 diff 用）
  const isLife = /买|妈|爸|家|生日|医院|缴|水电|物业/.test(raw)
  return {
    items: [
      {
        suggestionId: 's1',
        type: isLife ? 'lifelog' : 'task',
        confidence: 0.9,
        rationale: isLife ? '生活相关事实记录' : '原文含行动项',
        fields: isLife
          ? { content: raw, mood: null }
          : {
              content: raw.length > 30 ? `${raw.slice(0, 30)}…` : raw,
              remindAtLocal: null,
              priority: 2,
              section: 'work',
            },
      },
      {
        suggestionId: 's2',
        type: 'worklog',
        confidence: 0.78,
        rationale: '可作为工作记录草稿留存',
        fields: {
          title: raw.length > 16 ? `${raw.slice(0, 16)}…` : raw,
          content: raw,
          tags: [],
          category: null,
        },
      },
    ],
    uncertain: [],
  }
}

function retriaged(raw: string, correction: string | undefined): TriageResult {
  const base = synthesizeTriage(raw)
  // 纠错演示：去掉最后一条建议并回落置信度，让用户看到「AI 重新出了方案」
  const items = base.items.slice(0, Math.max(1, base.items.length - 1))
  return {
    items: items.map((it) => ({ ...it, confidence: Number((it.confidence - 0.05).toFixed(2)) })),
    uncertain: correction ? [`已按纠错备注「${correction}」重新分拣，请再次核对`] : [],
  }
}

// ---------- 对外 API（供 mirai.ts / data.ts 在 mock 分支调用） ----------
export const mockDb = {
  inboxList(): InboxItem[] {
    return inboxItems.filter((it) => it.status !== 4).sort((a, b) => b.id - a.id)
  },

  inboxPendingCount(): number {
    // 待处理含分拣失败（需用户重试），与视觉稿「● 3 待处理」口径一致
    return inboxItems.filter((it) => it.status !== 3 && it.status !== 4).length
  },

  createInbox(raw: string, source: InboxSource): Promise<InboxItem> {
    const item: InboxItem = {
      id: nextInboxId++,
      raw,
      source,
      status: 1,
      aiParse: null,
      aiModel: null,
      correctionNote: null,
      error: null,
      triagedAt: null,
      createdAt: now().toISOString(),
    }
    inboxItems.push(item)
    // 模拟 DeepSeek 同步分拣（~1.4s）
    return new Promise((resolve) => {
      setTimeout(() => {
        item.status = 2
        item.aiParse = synthesizeTriage(raw)
        item.aiModel = 'deepseek-v4-flash'
        item.triagedAt = now().toISOString()
        resolve(clone(item))
      }, 1400)
    })
  },

  retriage(id: number, req: RetriageRequest): Promise<InboxItem> {
    const item = inboxItems.find((it) => it.id === id)
    if (!item) return Promise.reject(new Error('收件箱条目不存在'))
    item.status = 1
    item.correctionNote = req.correction ?? item.correctionNote
    return new Promise((resolve) => {
      setTimeout(() => {
        item.status = 2
        item.aiParse = retriaged(item.raw, req.correction)
        item.triagedAt = now().toISOString()
        item.aiModel = 'deepseek-v4-flash'
        resolve(clone(item))
      }, 1600)
    })
  },

  dispatch(id: number, req: DispatchRequest): Promise<DispatchResult> {
    const item = inboxItems.find((it) => it.id === id)
    if (!item) return Promise.reject(new Error('收件箱条目不存在'))
    if (item.status !== 2) return Promise.reject(new Error('当前状态不可分发（409）'))
    const created: DispatchResult['created'] = []
    for (const d of req.items) {
      const sug = item.aiParse?.items.find((s) => s.suggestionId === d.suggestionId)
      if (!sug || !sug.fields) continue
      const merged = { ...(sug.fields as unknown as Record<string, unknown>), ...(d.overrides ?? {}) }
      if (sug.type === 'task') {
        const memoId = nextTaskId++
        memos.push({
          id: memoId,
          section: (merged.section as MemoSection) ?? 'work',
          content: String(merged.content ?? ''),
          remindAt: merged.remindAtLocal ? new Date(String(merged.remindAtLocal)).toISOString() : null,
          remindMethods: 1,
          emailReminderSent: false,
          popupAcknowledged: false,
          remindedAt: null,
          priority: (merged.priority as TaskPriority) ?? 2,
          isPinned: false,
          isDone: false,
          isArchived: false,
          createdAt: now().toISOString(),
          updatedAt: now().toISOString(),
          contextWorklogId: null,
        })
        created.push({ suggestionId: d.suggestionId, type: 'task', id: memoId, title: String(merged.content ?? '') })
      } else if (sug.type === 'worklog') {
        const wlId = nextWorklogId++
        worklogs.push({
          id: wlId,
          title: String(merged.title ?? ''),
          purpose: null,
          content: String(merged.content ?? ''),
          tags: Array.isArray(merged.tags) ? (merged.tags as string[]).join(',') : String(merged.tags ?? ''),
          category: (merged.category as string | null) ?? null,
          logDate: localDate(),
          status: 0,
          statusRemark: null,
          createdAt: now().toISOString(),
          updatedAt: now().toISOString(),
          aiSummary: null,
        })
        created.push({ suggestionId: d.suggestionId, type: 'worklog', id: wlId, title: String(merged.title ?? '') })
      } else if (sug.type === 'lifelog') {
        const llId = nextLifelogId++
        lifelogs.push({
          id: llId,
          content: String(merged.content ?? ''),
          mood: (merged.mood as string | null) ?? null,
          imagePath: null,
          imagePaths: [],
          logDate: localDate(),
          createdAt: now().toISOString(),
          updatedAt: now().toISOString(),
        })
        created.push({ suggestionId: d.suggestionId, type: 'lifelog', id: llId, title: String(merged.content ?? '') })
      }
    }
    item.status = 3
    const result: DispatchResult = { inboxItemId: id, created }
    lastDispatch.set(id, result)
    return Promise.resolve(clone(result))
  },

  discard(id: number): Promise<null> {
    const item = inboxItems.find((it) => it.id === id)
    if (!item) return Promise.reject(new Error('收件箱条目不存在'))
    if (item.status === 3) return Promise.reject(new Error('已分发的条目不可丢弃（409）'))
    item.status = 4
    return Promise.resolve(null)
  },

  undo(id: number): Promise<null> {
    const item = inboxItems.find((it) => it.id === id)
    if (!item) return Promise.reject(new Error('收件箱条目不存在'))
    if (item.status !== 3) return Promise.reject(new Error('仅已分发条目可撤销（409）'))
    const result = lastDispatch.get(id)
    if (result) {
      for (const c of result.created) {
        if (c.type === 'task') removeById(memos, c.id)
        else if (c.type === 'worklog') removeById(worklogs, c.id)
        else if (c.type === 'lifelog') removeById(lifelogs, c.id)
      }
    }
    item.status = 2
    return Promise.resolve(null)
  },

  dayOverview(date: string): DayOverview {
    const toDue = (m: Memo): DueTask => ({
      id: m.id,
      content: m.content,
      remindAt: m.remindAt,
      priority: (m.priority === 1 || m.priority === 2 || m.priority === 3 ? m.priority : 2) as TaskPriority,
      section: m.section,
      isDone: m.isDone,
      isPinned: m.isPinned,
    })
    const dueTasks = memos.filter((m) => !m.isDone && m.remindAt?.startsWith(date)).map(toDue)
    const today = date === localDate()
    const overdueTasks = memos
      .filter((m) => !m.isDone && m.remindAt && m.remindAt.slice(0, 10) < localDate() && !m.remindAt.startsWith(date))
      .map(toDue)
    const feed: FeedItem[] = []
    for (const wl of worklogs.filter((w) => w.logDate === date)) {
      feed.push({
        time: wl.createdAt,
        kind: 'worklog',
        title: `记录了「${wl.title}」`,
        refId: wl.id,
        aiSummary: wl.aiSummary ?? null,
      })
    }
    for (const it of inboxItems.filter((i) => i.createdAt.slice(0, 10) === date && i.status !== 4)) {
      feed.push({
        time: it.createdAt,
        kind: 'capture',
        title:
          it.status === 2
            ? `捕获 1 条，AI 分拣出 ${it.aiParse?.items.length ?? 0} 条建议，待确认`
            : it.status === 3
              ? `捕获 1 条，AI 分拣建议已确认入库`
              : '捕获 1 条',
        refId: it.id,
        aiSummary: null,
      })
    }
    if (today && MOCK_BRIEFING.date === date) {
      feed.push({ time: MOCK_BRIEFING.generatedAt, kind: 'briefing', title: '晨报已生成', refId: MOCK_BRIEFING.id, aiSummary: null })
    }
    feed.sort((a, b) => a.time.localeCompare(b.time))
    return {
      date,
      briefing: clone(MOCK_BRIEFING),
      briefingError: null,
      dueTasks: clone(dueTasks),
      overdueTasks: clone(overdueTasks),
      todayFeed: feed,
      inboxPendingCount: this.inboxPendingCount(),
      weekEntryCount: 23,
    }
  },

  regenerateBriefing(date: string): Promise<Briefing> {
    const b = clone(MOCK_BRIEFING)
    b.generatedAt = now().toISOString()
    b.date = date
    return Promise.resolve(b)
  },

  aiStats(): Promise<AiActionStats> {
    return Promise.resolve(clone(MOCK_AI_STATS))
  },

  taskContextOf(taskId: number): { text: string; worklogId: number } | null {
    return taskContext.get(taskId) ?? null
  },

  // ---------- 现有域数据 ----------
  listWorklogs(): WorkLog[] {
    return clone(worklogs).sort((a, b) => b.logDate.localeCompare(a.logDate) || b.id - a.id)
  },

  getWorklog(id: number): WorkLog | null {
    return clone(worklogs.find((w) => w.id === id) ?? null)
  },

  saveWorklog(payload: Partial<WorkLog> & { id?: number }): WorkLog {
    if (payload.id != null) {
      const idx = worklogs.findIndex((w) => w.id === payload.id)
      if (idx < 0) throw new Error('工作记录不存在')
      worklogs[idx] = { ...worklogs[idx]!, ...payload, updatedAt: now().toISOString() }
      return clone(worklogs[idx]!)
    }
    const wl: WorkLog = {
      id: nextWorklogId++,
      title: payload.title ?? '',
      purpose: payload.purpose ?? null,
      content: payload.content ?? '',
      tags: payload.tags ?? '',
      category: payload.category ?? null,
      logDate: payload.logDate ?? localDate(),
      status: payload.status ?? 0,
      statusRemark: payload.statusRemark ?? null,
      createdAt: now().toISOString(),
      updatedAt: now().toISOString(),
      aiSummary: null,
    }
    worklogs.push(wl)
    return clone(wl)
  },

  listLifelogs(): LifeLog[] {
    return clone(lifelogs).sort((a, b) => b.logDate.localeCompare(a.logDate) || b.id - a.id)
  },

  getLifelog(id: number): LifeLog | null {
    return clone(lifelogs.find((l) => l.id === id) ?? null)
  },

  saveLifelog(payload: Partial<LifeLog> & { id?: number }): LifeLog {
    if (payload.id != null) {
      const idx = lifelogs.findIndex((l) => l.id === payload.id)
      if (idx < 0) throw new Error('生活记录不存在')
      lifelogs[idx] = { ...lifelogs[idx]!, ...payload, updatedAt: now().toISOString() }
      return clone(lifelogs[idx]!)
    }
    const ll: LifeLog = {
      id: nextLifelogId++,
      content: payload.content ?? '',
      mood: payload.mood ?? null,
      imagePath: payload.imagePath ?? null,
      imagePaths: payload.imagePaths ?? [],
      logDate: payload.logDate ?? localDate(),
      createdAt: now().toISOString(),
      updatedAt: now().toISOString(),
    }
    lifelogs.push(ll)
    return clone(ll)
  },

  listMemos(): Memo[] {
    return clone(memos).sort((a, b) => (b.remindAt ?? '').localeCompare(a.remindAt ?? ''))
  },

  patchMemoStatus(id: number, patch: { isDone?: boolean | null; isPinned?: boolean | null }): Memo {
    const memo = memos.find((m) => m.id === id)
    if (!memo) throw new Error('任务不存在')
    if (patch.isDone != null) memo.isDone = patch.isDone
    if (patch.isPinned != null) memo.isPinned = patch.isPinned
    memo.updatedAt = now().toISOString()
    return clone(memo)
  },

  updateMemo(id: number, patch: Partial<Memo>): Memo {
    const memo = memos.find((m) => m.id === id)
    if (!memo) throw new Error('任务不存在')
    Object.assign(memo, patch)
    memo.updatedAt = now().toISOString()
    return clone(memo)
  },
}

function removeById<T extends { id: number }>(arr: T[], id: number) {
  const idx = arr.findIndex((x) => x.id === id)
  if (idx >= 0) arr.splice(idx, 1)
}
