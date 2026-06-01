/**
 * 将后端返回的相对路径（如 /uploads/images/xxx.jpg）
 * 拼接为完整的静态资源 URL。
 * 生产环境通过 VITE_STATIC_BASE_URL 配置域名；若未配置则保持相对路径。
 */
const staticBase = (import.meta.env.VITE_STATIC_BASE_URL as string | undefined) ?? ''

export function staticUrl(path: string | null | undefined): string {
  if (!path) return ''
  // 已经是绝对 URL，直接返回
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  return `${staticBase}${path}`
}
