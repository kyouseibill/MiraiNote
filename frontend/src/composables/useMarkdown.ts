import { marked } from 'marked'
import DOMPurify from 'dompurify'
import { staticUrl } from '@/composables/useStaticUrl'

// 配置 marked：启用 GitHub Flavored Markdown，关闭废弃的 mangle/headerIds
const renderer = new marked.Renderer()
marked.setOptions({ breaks: true })

/**
 * 将 Markdown 字符串安全渲染为 HTML。
 * 使用 DOMPurify 净化，防止 XSS。
 */
export function renderMarkdown(md: string | null | undefined): string {
  if (!md) return ''
  const raw = marked.parse(md, { renderer, async: false }) as string
  const sanitized = DOMPurify.sanitize(raw)
  return rewriteStaticAssetUrls(sanitized)
}

function rewriteStaticAssetUrls(html: string): string {
  return html
    .replace(/(<img\b[^>]*\bsrc=")(\/[^"]+)(")/gi, (_, prefix, src, suffix) => {
      return `${prefix}${staticUrl(src)}${suffix}`
    })
    .replace(/(<a\b[^>]*\bhref=")(\/[^"]+)(")/gi, (_, prefix, href, suffix) => {
      return `${prefix}${staticUrl(href)}${suffix}`
    })
}
