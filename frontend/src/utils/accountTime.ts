/**
 * Account-facing calendar helpers.
 * Day boundaries for chat history grouping follow the account timezone
 * (default Asia/Shanghai), not the browser's local timezone.
 */
export const ACCOUNT_TIMEZONE = 'Asia/Shanghai'
export const ACCOUNT_TIMEZONE_HINT = '日切 · Asia/Shanghai'

type ZonedParts = {
  year: number
  month: number
  day: number
  hour: number
  minute: number
}

function toDate(isoOrDate: string | Date): Date {
  return typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate
}

/** Calendar / clock parts in the account timezone. */
export function zonedParts(isoOrDate: string | Date, timeZone = ACCOUNT_TIMEZONE): ZonedParts {
  const date = toDate(isoOrDate)
  if (Number.isNaN(date.getTime())) {
    const fallback = new Date()
    return zonedParts(fallback, timeZone)
  }

  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })

  const parts = Object.fromEntries(
    formatter
      .formatToParts(date)
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  ) as Record<string, string>

  // Some engines emit hour "24" for midnight.
  const hourRaw = parts.hour === '24' ? '0' : parts.hour

  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    hour: Number(hourRaw),
    minute: Number(parts.minute),
  }
}

/** yyyy-MM-dd key for a moment in the account timezone. */
export function calendarDayKey(isoOrDate: string | Date, timeZone = ACCOUNT_TIMEZONE): string {
  const { year, month, day } = zonedParts(isoOrDate, timeZone)
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

function dayUtcMs(dayKey: string): number {
  const [year, month, day] = dayKey.split('-').map(Number)
  return Date.UTC(year, month - 1, day)
}

/**
 * Whole calendar days between `iso` and "today" in the account timezone.
 * Positive = past; 0 = today; 1 = yesterday.
 */
export function calendarDaysAgo(iso: string, timeZone = ACCOUNT_TIMEZONE, now: Date = new Date()): number {
  const target = calendarDayKey(iso, timeZone)
  const today = calendarDayKey(now, timeZone)
  return Math.round((dayUtcMs(today) - dayUtcMs(target)) / 86_400_000)
}

/** Sidebar history grouping label (今天 / 昨天 / …) using account timezone. */
export function sessionDateGroup(
  iso: string,
  timeZone = ACCOUNT_TIMEZONE,
  now: Date = new Date(),
): string {
  const days = calendarDaysAgo(iso, timeZone, now)
  const parts = zonedParts(iso, timeZone)
  const nowParts = zonedParts(now, timeZone)

  if (days === 0) return '今天'
  if (days === 1) return '昨天'
  if (days > 1 && days < 7) return '最近 7 天'
  if (parts.year === nowParts.year && parts.month === nowParts.month) return '本月'
  return `${parts.year}年${parts.month}月`
}

export function formatAccountDate(iso: string, timeZone = ACCOUNT_TIMEZONE): string {
  const { year, month, day } = zonedParts(iso, timeZone)
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

export function formatAccountDateTime(iso: string, timeZone = ACCOUNT_TIMEZONE, now: Date = new Date()): string {
  const parts = zonedParts(iso, timeZone)
  const nowParts = zonedParts(now, timeZone)
  const pad = (n: number) => String(n).padStart(2, '0')
  const time = `${pad(parts.hour)}:${pad(parts.minute)}`
  const isToday =
    parts.year === nowParts.year && parts.month === nowParts.month && parts.day === nowParts.day
  if (isToday) return time
  return `${parts.year}-${pad(parts.month)}-${pad(parts.day)} ${time}`
}
