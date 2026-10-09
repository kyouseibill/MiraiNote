import type { LocationQueryValue } from 'vue-router'

/** 只在非生产环境把欢迎页的 welcomeNow 传给接口。生产环境一律丢掉。 */
export function welcomeNowParam(
  query: LocationQueryValue | LocationQueryValue[] | undefined,
  production: boolean,
): string | undefined {
  if (production) return undefined
  const raw = Array.isArray(query) ? query[0] : query
  const text = raw?.trim()
  return text ? text : undefined
}
