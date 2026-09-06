/**
 * Lightweight checks for account-timezone day grouping (B2).
 * Mirrors frontend/src/utils/accountTime.ts without Vite/TS deps.
 */
import assert from 'node:assert/strict'

const ACCOUNT_TIMEZONE = 'Asia/Shanghai'

function zonedParts(isoOrDate, timeZone = ACCOUNT_TIMEZONE) {
  const date = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate
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
  )
  const hourRaw = parts.hour === '24' ? '0' : parts.hour
  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    hour: Number(hourRaw),
    minute: Number(parts.minute),
  }
}

function calendarDayKey(isoOrDate, timeZone = ACCOUNT_TIMEZONE) {
  const { year, month, day } = zonedParts(isoOrDate, timeZone)
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

function dayUtcMs(dayKey) {
  const [year, month, day] = dayKey.split('-').map(Number)
  return Date.UTC(year, month - 1, day)
}

function calendarDaysAgo(iso, timeZone = ACCOUNT_TIMEZONE, now = new Date()) {
  const target = calendarDayKey(iso, timeZone)
  const today = calendarDayKey(now, timeZone)
  return Math.round((dayUtcMs(today) - dayUtcMs(target)) / 86_400_000)
}

function sessionDateGroup(iso, timeZone = ACCOUNT_TIMEZONE, now = new Date()) {
  const days = calendarDaysAgo(iso, timeZone, now)
  const parts = zonedParts(iso, timeZone)
  const nowParts = zonedParts(now, timeZone)
  if (days === 0) return '今天'
  if (days === 1) return '昨天'
  if (days > 1 && days < 7) return '最近 7 天'
  if (parts.year === nowParts.year && parts.month === nowParts.month) return '本月'
  return `${parts.year}年${parts.month}月`
}

// Fixed "now": 2026-09-06 01:30 UTC = 2026-09-06 09:30 Asia/Shanghai
const now = new Date('2026-09-06T01:30:00.000Z')

// Same Shanghai day
assert.equal(sessionDateGroup('2026-09-05T16:30:00.000Z', ACCOUNT_TIMEZONE, now), '今天')
// Previous Shanghai day (still "today" in US Pacific if someone used browser local)
assert.equal(sessionDateGroup('2026-09-05T15:30:00.000Z', ACCOUNT_TIMEZONE, now), '昨天')
assert.equal(calendarDayKey('2026-09-05T15:59:59.000Z'), '2026-09-05')
assert.equal(calendarDayKey('2026-09-05T16:00:00.000Z'), '2026-09-06')

// Browser-local trap: if we naively used get* in UTC-7, 2026-09-05T16:30Z is Sep 5 local.
// Account TZ must still call it 今天 relative to Shanghai Sep 6.
assert.equal(sessionDateGroup('2026-09-05T16:30:00.000Z', ACCOUNT_TIMEZONE, now), '今天')

assert.equal(sessionDateGroup('2026-09-01T02:00:00.000Z', ACCOUNT_TIMEZONE, now), '最近 7 天')
assert.equal(sessionDateGroup('2026-08-20T02:00:00.000Z', ACCOUNT_TIMEZONE, now), '2026年8月')

console.log('test-account-time: ok')
