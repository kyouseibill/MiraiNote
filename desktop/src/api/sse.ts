// 从 frontend/src/api/sse.ts 复制适配（UI 流专用副本，不修改 frontend/ 本身）。
// 作用：健壮地消费 SSE fetch Response——网络分块与事件边界不对齐时仍能正确解析，
// 且强制要求以 done/error 终态收尾（防止代理静默断流被当作成功）。

export interface ParsedSseEvent {
  type: string
  data: unknown
}

export type ParsedSseCallback = (event: ParsedSseEvent) => void

export class IncompleteSseStreamError extends Error {
  constructor() {
    super('The streaming response ended before a terminal event was received')
    this.name = 'IncompleteSseStreamError'
  }
}

export async function consumeSseResponse(
  response: Response,
  onEvent: ParsedSseCallback,
): Promise<void> {
  if (!response.body) {
    throw new Error('The streaming response has no body')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  const dispatchBlock = (block: string) => {
    let eventType = 'message'
    const dataLines: string[] = []

    for (const rawLine of block.split(/\r?\n/)) {
      const line = rawLine.startsWith('\uFEFF') ? rawLine.slice(1) : rawLine
      if (!line || line.startsWith(':')) continue

      const separator = line.indexOf(':')
      const field = separator < 0 ? line : line.slice(0, separator)
      let value = separator < 0 ? '' : line.slice(separator + 1)
      if (value.startsWith(' ')) value = value.slice(1)

      if (field === 'event') eventType = value
      if (field === 'data') dataLines.push(value)
    }

    if (dataLines.length === 0) return

    const rawData = dataLines.join('\n')
    let data: unknown = rawData
    try {
      data = JSON.parse(rawData)
    } catch {
      // 纯文本 SSE data 同样合法
    }
    onEvent({ type: eventType, data })
  }

  const drainCompleteBlocks = () => {
    while (true) {
      const boundary = buffer.match(/\r?\n\r?\n/)
      if (!boundary || boundary.index == null) return

      const block = buffer.slice(0, boundary.index)
      buffer = buffer.slice(boundary.index + boundary[0].length)
      dispatchBlock(block)
    }
  }

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += decoder.decode(value, { stream: true })
    drainCompleteBlocks()
  }

  buffer += decoder.decode()
  drainCompleteBlocks()

  // 部分服务端在最后一条 data 后立即关闭连接、省略末尾空行；EOF 时事件仍算完整。
  if (buffer.trim()) dispatchBlock(buffer)
}

export async function consumeSseResponseUntilTerminal(
  response: Response,
  onEvent: ParsedSseCallback,
  signal?: AbortSignal,
): Promise<void> {
  let terminalReceived = false

  try {
    await consumeSseResponse(response, (event) => {
      if (event.type === 'done' || event.type === 'error') terminalReceived = true
      onEvent(event)
    })
  } catch (error) {
    // 服务端可能在终态事件后立即关闭连接，真实结果已交给上层，不再覆盖为通用网络错误。
    if (terminalReceived) return
    throw error
  }

  if (!terminalReceived && !signal?.aborted) {
    throw new IncompleteSseStreamError()
  }
}
