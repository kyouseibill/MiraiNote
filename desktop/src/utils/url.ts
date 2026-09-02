// 静态资源地址拼接（对齐 frontend/src/composables/useStaticUrl.ts 的行为）：
// 后端返回的图片等资源为相对路径（/uploads/…），需拼接 API 主机。

const apiBase = (import.meta.env.MIRAI_API_BASE as string | undefined) ?? 'http://localhost:5273/api/v1'
const staticBase = apiBase.replace(/\/api\/v\d+\/?$/, '').replace(/\/$/, '')

export function staticUrl(path: string | null | undefined): string {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  if (path.startsWith('data:') || path.startsWith('blob:')) return path
  return `${staticBase}${path.startsWith('/') ? path : `/${path}`}`
}
