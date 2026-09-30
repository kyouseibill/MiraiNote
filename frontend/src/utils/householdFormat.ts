import { calendarDayKey } from '@/utils/accountTime'
import type { HouseholdCategory, HouseholdCycleUnit, HouseholdItemType, HouseholdRole } from '@/types/household'

/** 家务日期比较用的「今天」，固定 Asia/Shanghai，不取浏览器本地时区。 */
export function shanghaiToday(now: Date = new Date()): string {
  return calendarDayKey(now)
}

/**
 * 在 yyyy-MM-dd 上平移日历日。只用于输入框的 min/max 和状态分组展示，
 * 不根据周期推算下次到期日。
 */
export function shiftCalendarDay(day: string, delta: number): string {
  const [year, month, date] = day.split('-').map(Number)
  const utc = new Date(Date.UTC(year, (month ?? 1) - 1, (date ?? 1) + delta))
  const y = utc.getUTCFullYear()
  const m = String(utc.getUTCMonth() + 1).padStart(2, '0')
  const d = String(utc.getUTCDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** 接口返回的日历日原样显示，避免用 Date 解析后跨时区少一天。 */
export function formatCalendarDate(value: string | null | undefined): string {
  if (!value) return '—'
  const day = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(day) ? day : value
}

export const HOUSEHOLD_CATEGORIES: { value: HouseholdCategory; label: string }[] = [
  { value: 'HomeMaintenance', label: '家务维护' },
  { value: 'Vehicle', label: '车辆' },
  { value: 'Document', label: '证件' },
  { value: 'Warranty', label: '保修' },
]

export const CYCLE_UNITS: { value: HouseholdCycleUnit; label: string }[] = [
  { value: 'Day', label: '天' },
  { value: 'Month', label: '月' },
  { value: 'Year', label: '年' },
]

export function categoryLabel(value: HouseholdCategory): string {
  return HOUSEHOLD_CATEGORIES.find((item) => item.value === value)?.label ?? value
}

export function unitLabel(value: HouseholdCycleUnit | null | undefined): string {
  if (!value) return ''
  return CYCLE_UNITS.find((item) => item.value === value)?.label ?? value
}

export function cycleLabel(value: number | null | undefined, unit: HouseholdCycleUnit | null | undefined): string {
  if (value == null || !unit) return '一次性到期'
  return `${value} ${unitLabel(unit)}`
}

export function itemTypeLabel(value: HouseholdItemType): string {
  return value === 'OneOffExpiry' ? '一次性到期' : '周期'
}

export function roleLabel(value: HouseholdRole): string {
  return value === 'Admin' ? '管理员' : '成员'
}

export type ItemStatusTone = 'overdue' | 'soon' | 'ok' | 'paused' | 'archived' | 'none'

/**
 * 用接口给出的 nextDueDate 和北京时间的今天做状态标签。
 * 暂停不算逾期。这里不推算到期日。
 */
export function itemStatus(
  item: { isPaused: boolean; isArchived?: boolean; nextDueDate: string | null },
  today = shanghaiToday(),
): { tone: ItemStatusTone; label: string } {
  if (item.isArchived) return { tone: 'archived', label: '已归档' }
  if (item.isPaused) return { tone: 'paused', label: '已暂停' }
  if (!item.nextDueDate) return { tone: 'none', label: '未排期' }
  const due = item.nextDueDate.slice(0, 10)
  if (due < today) return { tone: 'overdue', label: '已逾期' }
  if (due <= shiftCalendarDay(today, 7)) return { tone: 'soon', label: '即将到期' }
  return { tone: 'ok', label: '正常' }
}

/** 近期到期卡片上的天数文案，数字来自接口。 */
export function dueOffsetLabel(item: { daysOverdue: number; daysRemaining: number }): string {
  if (item.daysOverdue > 0) return `逾期 ${item.daysOverdue} 天`
  if (item.daysRemaining === 0) return '今天到期'
  return `剩余 ${item.daysRemaining} 天`
}

/**
 * 数字输入在 Vue 里会变成 number，清空时可能是 null。
 * 校验和提交前统一成去掉空白的字符串。
 */
export function draftText(raw: unknown): string {
  if (typeof raw === 'number' && Number.isFinite(raw)) return String(raw)
  if (typeof raw === 'string') return raw.trim()
  return ''
}

export function isPositiveInt(raw: unknown, max = 3650): boolean {
  const text = draftText(raw)
  if (!/^\d+$/.test(text)) return false
  const value = Number(text)
  return Number.isSafeInteger(value) && value > 0 && value <= max
}

export interface ItemDraftInput {
  name: string
  itemType: HouseholdItemType
  cycleValue: unknown
  cycleUnit: HouseholdCycleUnit | ''
  lastDoneDate: string
  expiryDate: string
  leadDays: unknown
  mileageCycleKm: unknown
}

/** 与接口一致：周期必须大于 0，上次完成日期不能晚于北京时间的今天。 */
export function validateItemDraft(input: ItemDraftInput, today: string): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!input.name.trim()) errors.name = '请填写名称'
  else if (input.name.trim().length > 100) errors.name = '名称不能超过 100 个字符'

  if (input.itemType === 'Recurring') {
    if (!isPositiveInt(input.cycleValue)) errors.cycleValue = '周期必须大于 0'
    if (!input.cycleUnit) errors.cycleUnit = '请选择周期单位'
    if (!input.lastDoneDate) errors.lastDoneDate = '请填写上次完成日期'
    else if (input.lastDoneDate > today) errors.lastDoneDate = '上次完成日期不能晚于今天'
  } else if (!input.expiryDate) {
    errors.expiryDate = '请填写到期日'
  }

  const leadDays = draftText(input.leadDays)
  if (leadDays) {
    if (!/^\d+$/.test(leadDays) || Number(leadDays) > 3650) {
      errors.leadDays = '提前提醒天数需在 0 到 3650 之间'
    }
  }

  const mileageCycleKm = draftText(input.mileageCycleKm)
  if (mileageCycleKm && !isPositiveInt(mileageCycleKm, 10_000_000)) {
    errors.mileageCycleKm = '里程周期需大于 0'
  }

  return errors
}

export const backfillRenewalMessage = '补记日期早于上次完成日期时不能同时填写新的到期日'

export interface CompletionDraftInput {
  completedOn: string
  today: string
  backfill: boolean
  renew: boolean
  newExpiryDate: string
  cost: unknown
  hasConsumable: boolean
  skipDeduction: boolean
  quantity: unknown
}

export function validateCompletionDraft(input: CompletionDraftInput): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!input.completedOn) errors.completedOn = '请填写完成日期'
  else if (input.completedOn > input.today) errors.completedOn = '完成日期不能晚于今天'

  if (input.renew) {
    if (input.backfill) errors.newExpiryDate = backfillRenewalMessage
    else if (!input.newExpiryDate) errors.newExpiryDate = '请填写新的到期日'
    else if (input.newExpiryDate <= input.today) errors.newExpiryDate = '新的到期日必须晚于今天'
    else if (input.completedOn && input.newExpiryDate <= input.completedOn) errors.newExpiryDate = '新的到期日必须晚于完成日期'
  }

  const cost = draftText(input.cost)
  if (cost) {
    if (!/^\d+(\.\d{1,2})?$/.test(cost)) errors.cost = '费用需为非负数字，最多两位小数'
  }

  if (input.hasConsumable && !input.skipDeduction) {
    const quantity = draftText(input.quantity)
    if (!/^\d+$/.test(quantity)) errors.quantity = '扣减数量需为 0 或正整数'
  }

  return errors
}

const PURCHASE_LINK_MAX = 500
const DISALLOWED_URL_CHARS = /\p{Cc}|\p{Cf}/u

/** 同样内容在 3 秒内换新 key 再完成时，接口返回 409。 */
export const duplicateCompletionMessage = '刚刚已提交过，请稍后再试'

/** Bark 推送地址只接受 https。主机白名单由服务器判断。 */
export const barkAddressError = 'Bark 地址只接受 https'

/** 密钥对不上时，设置页不再显示「已配置」。 */
export const barkReentryMessage = '保存的 Bark 地址无法读取，请重新填写。'

export const deliveryFailureCheckMessage = '请点发送测试检查。'

const DELIVERY_FAILURE_REASONS = new Set(['超时', '连接失败', '发送失败'])

/** 把失败时间格式化成用户时区。没有单独的用户时区时用 Asia/Shanghai。 */
export function formatDeliveryFailureTime(iso: string, timeZone = 'Asia/Shanghai'): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date)
  const pick = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
  return `${pick('year')}-${pick('month')}-${pick('day')} ${pick('hour')}:${pick('minute')}`
}

/** 失败之后又成功时传空，调用方就不显示警告。 */
export function deliveryFailureText(failure: { failedAt: string; reason: string } | null | undefined, timeZone = 'Asia/Shanghai'): string {
  if (!failure?.failedAt || !failure.reason || !DELIVERY_FAILURE_REASONS.has(failure.reason)) return ''
  const when = formatDeliveryFailureTime(failure.failedAt, timeZone)
  if (!when) return ''
  return `最近一次投递失败：${when}（北京时间）。原因：${failure.reason}。${deliveryFailureCheckMessage}`
}

/**
 * 只接受带主机名的 http/https，并返回解析后的绝对地址。
 * `http:evil.com`、`http:///evil`、javascript、data，以及控制字符、零宽字符都拒绝。
 */
export function canonicalHttpUrl(raw: string): string | null {
  if (DISALLOWED_URL_CHARS.test(raw)) return null
  const trimmed = raw.trim()
  if (!trimmed) return null
  if (!/^https?:\/\//i.test(trimmed)) return null
  if (/^https?:\/\/\//i.test(trimmed)) return null
  try {
    const url = new URL(trimmed)
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null
    if (!url.hostname || url.hostname.replace(/\./g, '') === '') return null
    return url.href
  } catch {
    return null
  }
}

/** Bark 地址。购买链接仍允许 http，这里只留下 https，并且只接受 443（省略端口或显式 :443）。 */
export function canonicalHttpsUrl(raw: string): string | null {
  const canonical = canonicalHttpUrl(raw)
  if (!canonical?.startsWith('https://')) return null
  try {
    if (new URL(canonical).port !== '') return null
  } catch {
    return null
  }
  return canonical
}

/** 空链接合法。非空时必须是有主机名的 http/https，含控制字符直接拒绝。 */
export function purchaseLinkError(raw: string): string {
  if (DISALLOWED_URL_CHARS.test(raw)) return '购买链接只接受 http 或 https'
  const trimmed = raw.trim()
  if (!trimmed) return ''
  if (trimmed.length > PURCHASE_LINK_MAX) return '购买链接不能超过 500 个字符'
  const canonical = canonicalHttpUrl(trimmed)
  if (!canonical || canonical.length > PURCHASE_LINK_MAX) return '购买链接只接受 http 或 https'
  return ''
}

/**
 * 绑定 href 前再解析一次。只有 http/https 才返回可导航地址，其它情况返回 null。
 * 返回的是解析后的 href，不把原始字符串直接放进链接。
 */
export function safeHttpUrl(raw: string | null | undefined): string | null {
  if (!raw || DISALLOWED_URL_CHARS.test(raw)) return null
  const trimmed = raw.trim()
  if (!trimmed || trimmed.length > PURCHASE_LINK_MAX) return null
  const canonical = canonicalHttpUrl(trimmed)
  if (!canonical || canonical.length > PURCHASE_LINK_MAX) return null
  return canonical
}

/** 邀请人没有单独昵称时用用户名。收件箱不展示邀请人邮箱。 */
export function invitationSentence(inviterName: string, householdName: string) {
  return `${inviterName} 邀请你加入『${householdName}』，接受后加入这个家庭。`
}

export function parseAliases(raw: string): string[] {
  return raw
    .split(/[,，\n]/)
    .map((part) => part.trim())
    .filter((part, index, list) => part.length > 0 && list.findIndex((item) => item.toLowerCase() === part.toLowerCase()) === index)
}

export function formatCost(cost: number | null | undefined): string {
  if (cost == null) return ''
  return `¥${cost.toFixed(2)}`
}
