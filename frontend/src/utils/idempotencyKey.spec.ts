import { afterEach, describe, expect, it, vi } from 'vitest'
import { createIdempotencyKey } from './idempotencyKey'

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

describe('createIdempotencyKey', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('优先使用 randomUUID', () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('11111111-1111-4111-8111-111111111111')
    expect(createIdempotencyKey()).toBe('11111111-1111-4111-8111-111111111111')
  })

  it('没有 randomUUID 时用 getRandomValues 拼 v4', () => {
    vi.spyOn(crypto, 'randomUUID').mockImplementation(undefined as never)
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
    const key = createIdempotencyKey()
    expect(key).toMatch(uuid)
    expect(key[14]).toBe('4')
  })
})