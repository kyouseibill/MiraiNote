import { describe, expect, it } from 'vitest'
import { barkAddressError, canonicalHttpsUrl, canonicalHttpUrl, purchaseLinkError, safeHttpUrl } from '@/utils/householdFormat'

/**
 * 与后端 HouseholdCycleRulesTests 的购买链接用例同一组（PRD 6-8b）。
 * 允许的期望值是解析后的绝对地址。
 */
const rejected = [
  'javascript:alert(1)',
  'JAVASCRIPT:alert(1)',
  ' javascript:alert(1)',
  '\tjavascript:alert(1)',
  'java\nscript:alert(1)',
  'java\tscript:alert(1)',
  'data:text/html,<script>alert(1)</script>',
  'vbscript:msgbox(1)',
  '/foo',
  'example.com/filter',
  'http://',
  'https://',
  ' HTTPS:// ',
  'http:evil.com',
  'http:///evil',
  'http:/evil.com',
  'https://example.com/\n',
  'https://example.com/\tpath',
  'https://ex\u200bample.com',
  'https://example.com/\u200c',
  'https://example.com/\u200d',
  '\uFEFFhttps://example.com',
  '\t',
]

const accepted: Array<[string, string]> = [
  ['http://example.com', 'http://example.com/'],
  ['https://example.com', 'https://example.com/'],
  ['https://example.com/item', 'https://example.com/item'],
  [' HTTP://shop.example/a ', 'http://shop.example/a'],
  ['https://example.com/filter', 'https://example.com/filter'],
  ['http://example.com/order', 'http://example.com/order'],
  ['http://example.com/template', 'http://example.com/template'],
]

describe('购买链接校验', () => {
  it.each(rejected)('拒绝 %j', (value) => {
    expect(purchaseLinkError(value)).toBe('购买链接只接受 http 或 https')
    expect(safeHttpUrl(value)).toBeNull()
    expect(canonicalHttpUrl(value)).toBeNull()
  })

  it.each(accepted)('接受 %j 并规范化为 %j', (value, expected) => {
    expect(purchaseLinkError(value)).toBe('')
    expect(canonicalHttpUrl(value)).toBe(expected)
    expect(safeHttpUrl(value)).toBe(expected)
  })

  it.each([null, '', '   '])('空值 %j 不报错也不生成链接', (value) => {
    expect(purchaseLinkError(value ?? '')).toBe('')
    expect(safeHttpUrl(value)).toBeNull()
  })
})

describe('Bark 地址', () => {
  it('只接受 https', () => {
    expect(canonicalHttpsUrl('https://api.day.app/device')).toBe('https://api.day.app/device')
    expect(canonicalHttpsUrl('http://api.day.app/device')).toBeNull()
    expect(canonicalHttpsUrl('http:evil.com')).toBeNull()
    expect(canonicalHttpsUrl('http:///evil')).toBeNull()
    expect(canonicalHttpsUrl('javascript:alert(1)')).toBeNull()
    expect(canonicalHttpsUrl('https://example.com/\n')).toBeNull()
  })

  it('提示文案只提 https', () => {
    expect(barkAddressError).toBe('Bark 地址只接受 https')
  })
})
