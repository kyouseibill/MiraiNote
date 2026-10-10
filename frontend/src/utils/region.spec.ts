import { describe, expect, it } from 'vitest'
import { filterCountries, findExactCountry } from '@/utils/region'

const countries = [
  { name: '中国', englishName: 'China', code: 'cn' },
  { name: '日本', englishName: 'Japan', code: 'jp' },
  { name: '美国', englishName: 'United States', code: 'us' },
  { name: '澳大利亚', englishName: 'Australia', code: 'au' },
  { name: '印度', englishName: 'India', code: 'in' },
  { name: '巴西', englishName: 'Brazil', code: 'br' },
]

describe('国家搜索匹配', () => {
  it('空查询保持置顶顺序，回车不选中任何国家', () => {
    expect(filterCountries(countries, '   ').map((item) => item.name)).toEqual(countries.map((item) => item.name))
    expect(findExactCountry(countries, '   ')).toBeUndefined()
    expect(findExactCountry(countries, '')).toBeUndefined()
  })

  it('中文名、英文名和国家代码都能命中，过滤后仍保持原顺序', () => {
    expect(filterCountries(countries, '日本').map((item) => item.code)).toEqual(['jp'])
    expect(filterCountries(countries, 'Japan').map((item) => item.code)).toEqual(['jp'])
    expect(filterCountries(countries, 'JP').map((item) => item.code)).toEqual(['jp'])
    expect(filterCountries(countries, '印度').map((item) => item.code)).toEqual(['in'])
    expect(filterCountries(countries, 'br').map((item) => item.code)).toEqual(['br'])
    expect(filterCountries(countries, 'us').map((item) => item.code)).toEqual(['us', 'au'])
    expect(findExactCountry(countries, 'us')?.name).toBe('美国')
    expect(findExactCountry(countries, 'Japan')?.name).toBe('日本')
    expect(findExactCountry(countries, '中国')?.code).toBe('cn')
  })
})
