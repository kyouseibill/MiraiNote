/** 服务端小句。空、空白或超过 40 字时不拼进标题。 */
export function visibleInspiration(line: string | null | undefined): string {
  const text = line?.trim() ?? ''
  if (!text || text.length > 40) return ''
  return text
}

/**
 * 问候和小句合成一行。有小句时用中文句号隔开；没有小句时只留问候，不加句号。
 */
export function joinWelcomeHeadline(greeting: string, inspiration: string | null | undefined): string {
  const head = greeting.trim()
  const line = visibleInspiration(inspiration)
  if (!line) return head
  const stem = head.replace(/。+$/u, '')
  return stem ? `${stem}。${line}` : line
}
