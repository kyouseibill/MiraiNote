import { describe, expect, it } from 'vitest'
import { greetingLines, type GreetingPeriod } from './greetingCopy'
import { composeGreeting, greetingPeriod, parseWelcomeNow, resolveGreetingNow } from './greetingPeriod'

function at(year: number, month: number, day: number, hour: number, minute: number): Date {
  return new Date(year, month - 1, day, hour, minute, 0, 0)
}

function expected(period: GreetingPeriod, name = 'Bill'): string {
  return greetingLines[period].split('{name}').join(name)
}

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
    expect(composeGreeting({ name: 'Bill', now: date, weatherBrief: '多云' })).toBe(expected(period))
  })
})

describe('下雨和周五都不换大标题', () => {
  const friday = { year: 2026, month: 10, day: 9 }

  it('每个时段只有固定的一句', () => {
    expect(expected('morning')).toBe('早上好，Bill')
    expect(expected('noon')).toBe('中午好，Bill')
    expect(expected('afternoon')).toBe('下午好，Bill')
    expect(expected('evening')).toBe('晚上好，Bill')
    expect(expected('lateNight')).toBe('夜深了，Bill，早点休息')
  })

  it('周五有雨仍用时段句', () => {
    const evening = at(friday.year, friday.month, friday.day, 21, 0)
    expect(composeGreeting({ name: 'Bill', now: evening, weatherBrief: '小雨' })).toBe('晚上好，Bill')
    expect(composeGreeting({ name: 'Bill', now: evening, weatherBrief: '雷阵雨' })).toBe('晚上好，Bill')

    const afternoon = at(friday.year, friday.month, friday.day, 15, 0)
    expect(composeGreeting({ name: 'Bill', now: afternoon, weatherBrief: null })).toBe('下午好，Bill')
    expect(composeGreeting({ name: 'Bill', now: afternoon, weatherBrief: '多云 24°C' })).toBe('下午好，Bill')
    expect(composeGreeting({ name: 'Bill', now: afternoon })).toBe('下午好，Bill')
    expect(composeGreeting({ name: 'Bill', now: afternoon, weatherBrief: '   ' })).toBe('下午好，Bill')
  })

  it('深夜有雨也只说早点休息', () => {
    const late = at(friday.year, friday.month, friday.day, 23, 30)
    expect(composeGreeting({ name: 'Bill', now: late, weatherBrief: '暴雨' })).toBe('夜深了，Bill，早点休息')
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 22, 59),
      weatherBrief: '雷阵雨',
    })).toBe('晚上好，Bill')
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 23, 0),
      weatherBrief: '雷阵雨',
    })).toBe('夜深了，Bill，早点休息')
    expect(composeGreeting({
      name: 'Bill',
      now: at(friday.year, friday.month, friday.day, 5, 0),
      weatherBrief: null,
    })).toBe('早上好，Bill')
  })

  it('同一时段隔天也不换句', () => {
    const afternoon = at(2026, 10, 7, 15, 0)
    const first = composeGreeting({ name: 'Bill', now: afternoon, weatherBrief: null })
    expect(first).toBe('下午好，Bill')
    expect(composeGreeting({ name: 'Bill', now: at(2026, 10, 7, 17, 59), weatherBrief: null })).toBe(first)
    expect(composeGreeting({ name: 'Bill', now: at(2026, 10, 8, 15, 0), weatherBrief: '小雨' })).toBe(first)
    expect(composeGreeting({ name: 'Bill', now: at(2026, 10, 9, 15, 0), weatherBrief: null })).toBe(first)
  })
})

describe('welcomeNow', () => {
  it('决定时段，不决定轮换', () => {
    const morning = parseWelcomeNow('2026-10-08T10:59')
    const again = parseWelcomeNow('2026-10-08T10:59')
    expect(morning).not.toBeNull()
    expect(greetingPeriod(morning!)).toBe('morning')
    expect(composeGreeting({ name: 'Bill', now: morning!, weatherBrief: null })).toBe('早上好，Bill')
    expect(composeGreeting({ name: 'Bill', now: again!, weatherBrief: null })).toBe('早上好，Bill')

    const fridayAfternoon = parseWelcomeNow('2026-10-09T15:00')!
    expect(composeGreeting({ name: 'Bill', now: fridayAfternoon, weatherBrief: null })).toBe('下午好，Bill')

    const late = parseWelcomeNow('2026-10-09T23:30')!
    expect(greetingPeriod(late)).toBe('lateNight')
    expect(composeGreeting({ name: 'Bill', now: late, weatherBrief: '雷阵雨' })).toBe('夜深了，Bill，早点休息')

    const fallback = at(2026, 10, 8, 15, 0)
    expect(resolveGreetingNow('2026-10-09T23:30', fallback).getTime()).toBe(late.getTime())
    expect(resolveGreetingNow(null, fallback)).toBe(fallback)
    expect(resolveGreetingNow('不是时间', fallback)).toBe(fallback)
    expect(parseWelcomeNow('2026-10-09T25:00')).toBeNull()
  })
})
