/** 大标题下的本地一句。接口没有句子、超时或超长时用，避免小字空着。 */
export const inspirationFallbacks = [
  '把今天过得轻一点，也把心里的事放得慢一点。',
  '不必把每件事都做完，先把眼前这一件做好。',
  '窗外的光刚好，适合把一件小事安静地做完。',
  '茶凉了再续，日子不必赶，句子也可以短一点。',
  '允许自己先停一下，然后再把下一步走稳些。',
] as const

function hashKey(input: string): number {
  let hash = 2166136261
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

/** 同一天稳定，隔天换一句。 */
export function fallbackInspiration(date: Date): string {
  const key = `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`
  const index = hashKey(key) % inspirationFallbacks.length
  return inspirationFallbacks[index] ?? inspirationFallbacks[0]
}

/** 服务端一句优先。空或超过 40 字时改用本地句。 */
export function resolveInspiration(line: string | null | undefined, now: Date): string {
  const text = line?.trim() ?? ''
  if (!text || text.length > 40) return fallbackInspiration(now)
  return text
}
