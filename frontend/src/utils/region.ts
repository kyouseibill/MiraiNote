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
