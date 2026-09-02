// 展示格式化工具（桌面端本地时区渲染；服务端一律 UTC ISO，见契约 §0）

export function fmtTime(iso: string | null | undefined): string {
  if (!iso) return ''
  return new Date(iso).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

export function fmtDateTime(iso: string | null | undefined): string {
  if (!iso) return ''
  return new Date(iso).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}

export function fmtDate(iso: string | null | undefined): string {
  if (!iso) return ''
  return iso.slice(0, 10)
}

const WEEKDAYS = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']

export function localDateStr(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** 「2026-08-22 周六」 */
export function fmtDateWithWeek(date: string): string {
  const d = new Date(`${date}T00:00:00`)
  return Number.isNaN(d.getTime()) ? date : `${date} ${WEEKDAYS[d.getDay()]}`
}

/** 到期时间的友好展示：今天 → HH:mm；其他 → MM-dd HH:mm */
export function fmtDue(iso: string | null | undefined): string {
  if (!iso) return '无提醒'
  const d = new Date(iso)
  const today = localDateStr()
  const dateStr = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const hm = d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
  return dateStr === today ? `今天 ${hm}` : `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${hm}`
}

/** 逾期天数（向上取整，不足一天算 1） */
export function overdueDays(iso: string): number {
  const diff = Date.now() - new Date(iso).getTime()
  return diff <= 0 ? 0 : Math.max(1, Math.ceil(diff / 86400000))
}

export const PRIORITY_TEXT: Record<number, string> = { 1: '低', 2: '中', 3: '高' }

/** 提醒方式位标志 → 文案（0=不提醒 1=弹窗 2=邮件 3=弹窗+邮件） */
export function remindMethodsText(bits: number): string {
  const methods: string[] = []
  if (bits & 1) methods.push('弹窗')
  if (bits & 2) methods.push('邮件')
  return methods.length ? methods.join('+') : '不提醒'
}

/** UTC ISO → datetime-local 输入框值（本地时区，无秒） */
export function isoToLocalInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** datetime-local 输入框值 → UTC ISO（空串 → null） */
export function localInputToIso(v: string | null | undefined): string | null {
  if (!v) return null
  const d = new Date(v)
  return Number.isNaN(d.getTime()) ? null : d.toISOString()
}

export function todayStr(): string {
  return localDateStr()
}
