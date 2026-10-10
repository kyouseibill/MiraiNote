import { describe, expect, it } from 'vitest'
import { fallbackInspiration, inspirationFallbacks, resolveInspiration } from './inspirationCopy'

describe('欢迎语小句回退', () => {
  const day = new Date(2026, 9, 8, 15, 0, 0)

  it('同一天稳定，空和超长都改用本地句', () => {
    const local = fallbackInspiration(day)
    expect(inspirationFallbacks).toContain(local)
    expect(resolveInspiration(null, day)).toBe(local)
    expect(resolveInspiration('   ', day)).toBe(local)
    expect(resolveInspiration('安'.repeat(41), day)).toBe(local)
    expect(resolveInspiration('今天不必赶完所有事，把眼前这一小步走稳。', day)).toBe('今天不必赶完所有事，把眼前这一小步走稳。')
    expect(resolveInspiration('安'.repeat(40), day)).toBe('安'.repeat(40))
  })

  it('隔天可以换一句', () => {
    const lines = new Set<string>()
    for (let dayOfMonth = 1; dayOfMonth <= 12; dayOfMonth++) {
      lines.add(fallbackInspiration(new Date(2026, 9, dayOfMonth, 9, 0, 0)))
    }
    expect(lines.size).toBeGreaterThan(1)
  })
})