import { describe, expect, it } from 'vitest'
import {
  fridayGreeting,
  greetingPools,
  rainGreeting,
  type GreetingPeriod,
} from './greetingCopy'
import { composeGreeting, greetingPeriod, parseWelcomeNow, pickPoolLine, resolveGreetingNow } from './greetingPeriod'

function at(year: number, month: number, day: number, hour: number, minute: number): Date {
  return new Date(year, month - 1, day, hour, minute, 0, 0)
}

function filled(period: GreetingPeriod, name = 'Bill'): string[] {
  return greetingPools[period].map((line) => line.split('{name}').join(name))
}

const rainLine = rainGreeting.split('{name}').join('Bill')
const fridayLine = fridayGreeting.split('{name}').join('Bill')

describe('时段边界', () => {
  const cases: [number, number, GreetingPeriod][] = [
    [4, 59, 'lateNight'],
    [5, 0, 'morning'],
    [10, 59, 'morning'],
    [11, 0, 'noon'],
    [12, 59, 'noon'],
    [13, 0, 'afternoon'],
    [17, 59, 'afternoon'],
    [18, 0, 'evening'],
    [22, 59, 'evening'],
    [23, 0, 'lateNight'],
    [0, 0, 'lateNight'],
  ]

  it.each(cases)('%s:%s → %s', (hour, minute, period) => {
    const date = at(2026, 10, 7, hour, minute)
    expect(greetingPeriod(date)).toBe(period)
    const line = composeGreeting({ name: 'Bill', now: date, weatherBrief: '多云' })
    expect(filled(period)).toContain(line)
  })
})

describe('优先级', () => {
  const friday = { year: 2026, month: 10, day: 9 }

  it('周五晚上有雨用下雨句', () => {
    const now = at(friday.year, friday.month, friday.day, 21, 0)
    expect(greetingPeriod(now)).toBe('evening')
    expect(composeGreeting({ name: 'Bill', now, weatherBrief: '小雨' })).toBe(rainLine)
  })

  it('周五 23:30 有雨仍走深夜池', () => {
    const now = at(friday.year, friday.month, friday.day, 23, 30)
    const line = composeGreeting({ name: 'Bill', now, weatherBrief: '暴雨' })
    expect(greetingPeriod(now)).toBe('lateNight')
    expect(filled('lateNight')).toContain(line)
    expect(line).not.toBe(rainLine)
    expect(line).not.toBe(fridayLine)
  })

  it('周五下午无雨用周五句', () => {
    const now = at(friday.year, friday.month, friday.day, 15, 0)
    expect(composeGreeting({ name: 'Bill', now, weatherBrief: null })).toBe(fridayLine)
    expect(composeGreeting({ name: 'Bill', now, weatherBrief: '多云 24°C' })).toBe(fridayLine)
  })

  it('没有实况不算下雨', () => {
    const fridayAfternoon = at(friday.year, friday.month, friday.day, 15, 0)
    expect(composeGreeting({ name: 'Bill', now: fridayAfternoon })).toBe(fridayLine)
    expect(composeGreeting({ name: 'Bill', now: fridayAfternoon, weatherBrief: '' })).toBe(fridayLine)
    expect(composeGreeting({ name: 'Bill', now: fridayAfternoon, weatherBrief: '   ' })).toBe(fridayLine)

    const thursday = at(2026, 10, 8, 15, 0)
    const line = composeGreeting({ name: 'Bill', now: thursday, weatherBrief: null })
    expect(line).not.toBe(rainLine)
    expect(filled('afternoon')).toContain(line)
  })

  it('雷阵雨和雨夹雪算下雨', () => {
    const thursday = at(2026, 10, 8, 15, 0)
    expect(composeGreeting({ name: 'Bill', now: thursday, weatherBrief: '雷阵雨' })).toBe(rainLine)
    expect(composeGreeting({ name: 'Bill', now: thursday, weatherBrief: '雨夹雪' })).toBe(rainLine)
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 15, 0),
      weatherBrief: '雷阵雨',
    })).toBe(rainLine)
  })

  it('周五 22:59 有雨仍是下雨，23:00 起改走深夜池', () => {
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 22, 59),
      weatherBrief: '雷阵雨',
    })).toBe(rainLine)
    const late = composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 23, 0),
      weatherBrief: '雷阵雨',
    })
    expect(filled('lateNight')).toContain(late)
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 0, 0),
      weatherBrief: null,
    })).not.toBe(fridayLine)
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 5, 0),
      weatherBrief: null,
    })).toBe(fridayLine)
  })
})

describe('按日本地日期轮换', () => {
  it('同一天同一时段稳定，隔天至少换一次', () => {
    const afternoon = at(2026, 10, 7, 15, 0)
    const first = composeGreeting({ name: 'Bill', now: afternoon, weatherBrief: null })
    expect(composeGreeting({ name: 'Bill', now: at(2026, 10, 7, 17, 59), weatherBrief: null })).toBe(first)
    expect(pickPoolLine(afternoon, 'afternoon')).toBe(pickPoolLine(at(2026, 10, 7, 13, 0), 'afternoon'))

    let differed = false
    for (let offset = 1; offset <= 12; offset++) {
      const next = at(2026, 10, 7 + offset, 15, 0)
      if (next.getDay() === 5) continue
      if (composeGreeting({ name: 'Bill', now: next, weatherBrief: null }) !== first) {
        differed = true
        break
      }
    }
    expect(differed).toBe(true)
  })

  it('welcomeNow 同时决定时段和轮换日期', () => {
    const morning = parseWelcomeNow('2026-10-08T10:59')
    const again = parseWelcomeNow('2026-10-08T10:59')
    expect(morning).not.toBeNull()
    expect(greetingPeriod(morning!)).toBe('morning')
    const morningLine = composeGreeting({ name: 'Bill', now: morning!, weatherBrief: null })
    expect(composeGreeting({ name: 'Bill', now: again!, weatherBrief: null })).toBe(morningLine)
    expect(filled('morning')).toContain(morningLine)

    const fridayAfternoon = parseWelcomeNow('2026-10-09T15:00')!
    expect(composeGreeting({ name: 'Bill', now: fridayAfternoon, weatherBrief: null })).toBe(fridayLine)

    const late = parseWelcomeNow('2026-10-09T23:30')!
    expect(greetingPeriod(late)).toBe('lateNight')
    expect(filled('lateNight')).toContain(composeGreeting({
      name: 'Bill',
      now: late,
      weatherBrief: '雷阵雨',
    }))

    const fallback = at(2026, 10, 8, 15, 0)
    expect(resolveGreetingNow('2026-10-09T23:30', fallback).getTime()).toBe(late.getTime())
    expect(resolveGreetingNow(null, fallback)).toBe(fallback)
    expect(resolveGreetingNow('不是时间', fallback)).toBe(fallback)
    expect(parseWelcomeNow('2026-10-09T25:00')).toBeNull()
  })
})
