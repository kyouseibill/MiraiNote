export interface RegionParts {
  country: string
  city: string
}

const separator = /[·・\-－–—―]/

/** 从「中国 · 上海」或旧的「中国-上海」拆出国家与城市。多出来的一级不算。 */
export function parseRegion(place: string | null | undefined): RegionParts | null {
  const text = place?.trim() ?? ''
  if (!text) return null
  const index = text.search(separator)
  if (index <= 0 || index >= text.length - 1) return null
  const country = text.slice(0, index).trim()
  const city = text.slice(index + 1).trim()
  if (!country || !city || separator.test(city)) return null
  return { country, city }
}

export function formatRegion(country: string, city: string): string {
  return `${country} · ${city}`
}

export interface SearchableCountry {
  name: string
  code: string
  englishName?: string
}

/** 空查询保持调用方给的顺序。中文名、英文名、国家代码任一包含即命中。 */
export function filterCountries<T extends SearchableCountry>(countries: readonly T[], query: string): T[] {
  const q = query.trim().toLowerCase()
  if (!q) return countries.slice()
  return countries.filter((item) => countryMatches(item, q))
}

/** 回车只接受完整的中文名、英文名或国家代码，避免把搜索词当成已选国家。 */
export function findExactCountry<T extends SearchableCountry>(countries: readonly T[], query: string): T | undefined {
  const q = query.trim()
  if (!q) return undefined
  const lower = q.toLowerCase()
  return countries.find((item) => item.name === q)
    ?? countries.find((item) => item.code.toLowerCase() === lower)
    ?? countries.find((item) => (item.englishName ?? '').toLowerCase() === lower)
}

function countryMatches(country: SearchableCountry, q: string): boolean {
  return country.name.toLowerCase().includes(q)
    || (country.englishName ?? '').toLowerCase().includes(q)
    || country.code.toLowerCase().includes(q)
}
