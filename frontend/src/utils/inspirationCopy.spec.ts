import { describe, expect, it } from 'vitest'
import { joinWelcomeHeadline, visibleInspiration } from './inspirationCopy'

describe('欢迎语标题合成', () => {
  const inspiration = '上海秋日多云，梧桐叶在风里打着旋。'

  it('有小句时用中文句号接成一行', () => {
    expect(joinWelcomeHeadline('早上好，Bill', inspiration)).toBe(`早上好，Bill。${inspiration}`)
    expect(joinWelcomeHeadline('  早上好，Bill  ', `  ${inspiration}  `)).toBe(`早上好，Bill。${inspiration}`)
    expect(joinWelcomeHeadline('早上好，Bill。', inspiration)).toBe(`早上好，Bill。${inspiration}`)
  })

  it('小句缺失、失败或空白时只留问候，不加句号，也不留空', () => {
    expect(joinWelcomeHeadline('早上好，Bill', null)).toBe('早上好，Bill')
    expect(joinWelcomeHeadline('早上好，Bill', undefined)).toBe('早上好，Bill')
    expect(joinWelcomeHeadline('早上好，Bill', '   ')).toBe('早上好，Bill')
    expect(joinWelcomeHeadline('早上好，Bill', '')).toBe('早上好，Bill')
    expect(joinWelcomeHeadline('早上好，Bill', '安'.repeat(41))).toBe('早上好，Bill')
    expect(visibleInspiration(null)).toBe('')
    expect(visibleInspiration('   ')).toBe('')
  })

  it('合格的小句原样保留', () => {
    const line = '今天不必赶完所有事，把眼前这一小步走稳。'
    expect(visibleInspiration(line)).toBe(line)
    expect(visibleInspiration('安'.repeat(40))).toBe('安'.repeat(40))
    expect(joinWelcomeHeadline('下午好，雅美', line)).toBe(`下午好，雅美。${line}`)
  })
})
