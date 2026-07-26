export interface ParsedSseEvent {
  type: string
  data: unknown
}

export type ParsedSseCallback = (event: ParsedSseEvent) => void

/**
 * Consume an SSE response without assuming that network chunks line up with
 * event boundaries. In particular, `event:`, `data:` and the terminating
 * blank line may all arrive in different reads.
 */
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
      // Plain-text SSE data is valid too.
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

  // Some servers/proxies close immediately after the last data line and omit
  // the final blank line. The event is still complete once EOF is reached.
  if (buffer.trim()) dispatchBlock(buffer)
}
