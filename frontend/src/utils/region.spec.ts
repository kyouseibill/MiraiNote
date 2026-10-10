import { describe, expect, it } from 'vitest'
import { cityDedupeKey, filterCountries, findExactCountry, mergeCityHits } from '@/utils/region'

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

  it('城市按行政区后缀去重，常用城市只补和风没有的', () => {
    expect(cityDedupeKey('大阪市')).toBe('大阪')
    expect(cityDedupeKey('札幌市')).toBe('札幌')
    expect(cityDedupeKey('东京')).toBe('东京')

    const commons = [
      { name: '东京' },
      { name: '大阪' },
      { name: '札幌市' },
    ]
    expect(mergeCityHits([{ name: '大阪市' }], commons, '大阪').map((item) => item.name)).toEqual(['大阪市'])
    expect(mergeCityHits([], commons, '札幌').map((item) => item.name)).toEqual(['札幌市'])
    expect(mergeCityHits([], commons, '').map((item) => item.name)).toEqual(['东京', '大阪', '札幌市'])
    expect(mergeCityHits(Array.from({ length: 20 }, (_, i) => ({ name: `城${i}` })), commons, '东')).toHaveLength(20)
  })
})
