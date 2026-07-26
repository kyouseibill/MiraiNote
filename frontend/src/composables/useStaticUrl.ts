const configuredStaticBase = (import.meta.env.VITE_STATIC_BASE_URL as string | undefined) ?? ''
const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''
const derivedStaticBase = apiBase.replace(/\/api\/v\d+\/?$/, '')
const staticBase = configuredStaticBase || derivedStaticBase

export function staticUrl(path: string | null | undefined): string {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  if (path.startsWith('data:') || path.startsWith('blob:')) return path
  return `${staticBase}${path}`
}
