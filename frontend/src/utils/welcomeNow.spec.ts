import { describe, expect, it } from 'vitest'
import { welcomeNowParam } from './welcomeNow'

describe('welcomeNow', () => {
  it('生产环境不把参数传给接口', () => {
    expect(welcomeNowParam('2026-10-09T04:59:00', true)).toBeUndefined()
  })

  it('非生产环境原样传出，空白则不传', () => {
    expect(welcomeNowParam(' 2026-10-09T04:59:00 ', false)).toBe('2026-10-09T04:59:00')
    expect(welcomeNowParam(['2026-10-08T23:00:00+08:00'], false)).toBe('2026-10-08T23:00:00+08:00')
    expect(welcomeNowParam('   ', false)).toBeUndefined()
    expect(welcomeNowParam(null, false)).toBeUndefined()
  })
})