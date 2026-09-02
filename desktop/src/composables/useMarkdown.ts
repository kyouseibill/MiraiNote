// 从 frontend/src/composables/useMarkdown.ts 复制适配（去掉静态资源改写依赖，
// 改为桌面端独立 staticUrl；marked + DOMPurify 防 XSS）。
import { marked } from 'marked'
import DOMPurify from 'dompurify'
import { staticUrl } from '@/utils/url'

marked.setOptions({ breaks: true, gfm: true })

/** 将 Markdown 字符串安全渲染为 HTML（DOMPurify 净化 + 相对资源地址改写） */
export function renderMarkdown(md: string | null | undefined): string {
  if (!md) return ''
  const raw = marked.parse(md, { async: false }) as string
  const sanitized = DOMPurify.sanitize(raw, {
    // 桌面端内联渲染，禁外链跳转行为（点击仍可复制）
    ADD_ATTR: ['target'],
  })
  return rewriteRelativeUrls(sanitized)
}

function rewriteRelativeUrls(html: string): string {
  return html
    .replace(/(<img\b[^>]*\bsrc=")(\/[^"]+)(")/gi, (_, prefix: string, src: string, suffix: string) => {
      return `${prefix}${staticUrl(src)}${suffix}`
    })
    .replace(/(<a\b[^>]*\bhref=")(\/[^"]+)(")/gi, (_, prefix: string, href: string, suffix: string) => {
      return `${prefix}${staticUrl(href)}${suffix}`
    })
}
