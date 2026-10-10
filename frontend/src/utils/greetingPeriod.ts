import { greetingLines, type GreetingPeriod } from './greetingCopy'

export type { GreetingPeriod }

/** 左闭右开的本地钟点。23:00–05:00 归深夜。 */
const PERIOD_BOUNDS: { start: number; end: number; period: GreetingPeriod }[] = [
  { start: 5 * 60, end: 11 * 60, period: 'morning' },
  { start: 11 * 60, end: 13 * 60, period: 'noon' },
  { start: 13 * 60, end: 18 * 60, period: 'afternoon' },
  { start: 18 * 60, end: 23 * 60, period: 'evening' },
]

export function greetingPeriod(date: Date): GreetingPeriod {
  const minutes = date.getHours() * 60 + date.getMinutes()
  for (const band of PERIOD_BOUNDS) {
    if (minutes >= band.start && minutes < band.end) return band.period
  }
  return 'lateNight'
}

function fillName(template: string, name: string): string {
  return template.split('{name}').join(name)
}

/**
 * 大标题只看钟点。下雨和周五不再换句。
 * weatherBrief 仍保留在参数里，调用方不用改，但不再影响句子。
 */
export function composeGreeting(options: {
  name: string
  now: Date
  weatherBrief?: string | null
}): string {
  return fillName(greetingLines[greetingPeriod(options.now)], options.name)
}

const WELCOME_NOW = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/

/** 解析 `2026-10-09T10:59`，按浏览器本地时间构造。 */
export function parseWelcomeNow(raw: string): Date | null {
  const match = WELCOME_NOW.exec(raw.trim())
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const hour = Number(match[4])
  const minute = Number(match[5])
  const second = match[6] ? Number(match[6]) : 0
  const date = new Date(year, month - 1, day, hour, minute, second, 0)
  if (
    date.getFullYear() !== year
    || date.getMonth() !== month - 1
    || date.getDate() !== day
    || date.getHours() !== hour
    || date.getMinutes() !== minute
    || date.getSeconds() !== second
  ) {
    return null
  }
  return date
}

/**
 * 仅开发或非 production 构建生效。
 * 生产构建里 `import.meta.env.DEV` 为 false，这个函数直接返回时钟，解析代码不会留下。
 */
export function resolveGreetingNow(query: string | null | undefined, fallback: Date): Date {
  if (!import.meta.env.DEV || import.meta.env.MODE === 'production') return fallback
  if (!query?.trim()) return fallback
  return parseWelcomeNow(query) ?? fallback
}
