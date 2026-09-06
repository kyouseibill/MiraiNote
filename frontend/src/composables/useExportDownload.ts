import { http } from '@/api/auth'
import { staticUrl } from '@/composables/useStaticUrl'

const EXPORT_API_MARKER = '/api/v1/mirai/exports/'

export type ExportPreviewKind = 'text' | 'pdf' | 'binary'

export interface ExportPreviewResult {
  kind: ExportPreviewKind
  text?: string
  objectUrl?: string
}

function originBase(): string {
  return typeof window !== 'undefined' ? window.location.origin : 'http://localhost'
}

/** True when the URL points at a Mirai export download endpoint. */
export function isExportDownload(url: string): boolean {
  try {
    return new URL(staticUrl(url), originBase()).pathname.includes(EXPORT_API_MARKER)
  } catch {
    return typeof url === 'string' && url.includes(EXPORT_API_MARKER)
  }
}

/**
 * Normalize any absolute/relative export URL to a path starting with
 * `/api/v1/mirai/exports/...` (origin stripped; http/IP rewritten to path-only).
 */
export function exportApiPath(url: string): string {
  const raw = String(url ?? '').trim()
  if (!raw) return ''

  try {
    const resolved = staticUrl(raw)
    const parsed = new URL(resolved, originBase())
    let pathname = parsed.pathname.replace(/\\/g, '/')

    const apiIdx = pathname.indexOf(EXPORT_API_MARKER)
    if (apiIdx >= 0) {
      return `${pathname.slice(apiIdx)}${parsed.search}`
    }

    const exportsIdx = pathname.indexOf('/exports/')
    if (exportsIdx >= 0) {
      // e.g. /mirai/exports/... or bare /exports/...
      const after = pathname.slice(exportsIdx) // /exports/...
      return `/api/v1/mirai${after}${parsed.search}`
    }
  } catch {
    /* fall through */
  }

  // Already a path-like string
  const normalized = raw.replace(/\\/g, '/')
  const idx = normalized.indexOf(EXPORT_API_MARKER)
  if (idx >= 0) {
    const pathPart = normalized.slice(idx).split(/[?#]/)[0]
    const queryIdx = normalized.indexOf('?', idx)
    return queryIdx >= 0 ? normalized.slice(idx) : pathPart
  }

  if (normalized.startsWith('/exports/')) {
    return `/api/v1/mirai${normalized}`
  }

  return normalized.startsWith('/') ? normalized : `/${normalized}`
}

/**
 * Axios request URL for an export: always same-origin path under API base
 * (`mirai/exports/...`), never a bare cross-origin http://IP link.
 */
export function exportRequestUrl(url: string): string {
  const apiPath = exportApiPath(url)
  if (apiPath.startsWith('/api/v1/')) {
    return apiPath.slice('/api/v1/'.length) // mirai/exports/...
  }
  if (apiPath.startsWith('api/v1/')) {
    return apiPath.slice('api/v1/'.length)
  }
  if (apiPath.startsWith('mirai/exports/')) return apiPath
  if (apiPath.startsWith('/mirai/exports/')) return apiPath.slice(1)
  return apiPath.replace(/^\//, '')
}

export function fileNameFromUrl(url: string): string {
  const path = exportApiPath(url) || url
  const name = path.split('/').pop()?.split('?')[0] || '导出文件'
  try {
    return decodeURIComponent(name).replace(/^\d{17}_/, '') || '导出文件'
  } catch {
    return name
  }
}

async function messageFromAxiosError(error: unknown): Promise<string> {
  const err = error as {
    message?: string
    response?: { data?: unknown; status?: number }
  }
  const data = err?.response?.data
  if (typeof Blob !== 'undefined' && data instanceof Blob) {
    try {
      const text = await data.text()
      const json = JSON.parse(text) as { message?: string }
      if (json?.message) return json.message
    } catch {
      /* ignore */
    }
  }
  if (data && typeof data === 'object' && data !== null && 'message' in data) {
    const msg = (data as { message?: string }).message
    if (msg) return msg
  }
  return err?.message || '文件下载失败，请稍后重试'
}

/** Authenticated blob download via same-origin API path + `<a download>`. */
export async function downloadExportFile(url: string, fileName = fileNameFromUrl(url)): Promise<void> {
  try {
    const response = await http.get<Blob>(exportRequestUrl(url), {
      responseType: 'blob',
      timeout: 60_000,
    })
    const contentType = String(response.headers['content-type'] || '')
    if (contentType.includes('application/json')) {
      const text = await response.data.text()
      let message = '下载失败'
      try {
        const json = JSON.parse(text) as { message?: string }
        if (json?.message) message = json.message
      } catch {
        /* ignore */
      }
      throw new Error(message)
    }
    if (!response.data.size) throw new Error('文件内容为空')

    const objectUrl = URL.createObjectURL(response.data)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = fileName
    link.style.display = 'none'
    document.body.appendChild(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1_000)
  } catch (error) {
    throw new Error(await messageFromAxiosError(error))
  }
}

function extensionOf(urlOrName: string): string {
  const base = (urlOrName.split('/').pop() || urlOrName).split('?')[0]
  const idx = base.lastIndexOf('.')
  return idx >= 0 ? base.slice(idx).toLowerCase() : ''
}

/** Fetch export content for in-app preview (text / pdf blob URL / binary). */
export async function fetchExportPreview(url: string): Promise<ExportPreviewResult> {
  const ext = extensionOf(fileNameFromUrl(url) || url)
  const response = await http.get<Blob>(exportRequestUrl(url), {
    responseType: 'blob',
    timeout: 60_000,
  })
  const contentType = String(response.headers['content-type'] || '')
  if (contentType.includes('application/json')) {
    const text = await response.data.text()
    let message = '预览加载失败'
    try {
      const json = JSON.parse(text) as { message?: string }
      if (json?.message) message = json.message
    } catch {
      /* ignore */
    }
    throw new Error(message)
  }
  if (!response.data.size) throw new Error('文件内容为空')

  if (ext === '.pdf' || contentType.includes('application/pdf')) {
    return { kind: 'pdf', objectUrl: URL.createObjectURL(response.data) }
  }

  if (['.md', '.markdown', '.txt'].includes(ext) || contentType.startsWith('text/')) {
    const text = await response.data.text()
    return { kind: 'text', text }
  }

  return { kind: 'binary' }
}

export function isExportPreviewable(extension: string): boolean {
  return ['.md', '.markdown', '.txt', '.pdf'].includes(extension.toLowerCase())
}

export function isMarkdownExport(extension: string): boolean {
  return ['.md', '.markdown'].includes(extension.toLowerCase())
}
