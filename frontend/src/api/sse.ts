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

/**
 * Chat streams must finish with either `done` or `error`. A proxy can close an
 * SSE response cleanly at the transport layer even though the task is still
 * running; treat that as an interrupted stream instead of silent success.
 */
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
    // The server may close immediately after its terminal event. The user has
    // already received the real result/error, so do not replace it with a
    // second generic network warning.
    if (terminalReceived) return
    throw error
  }

  if (!terminalReceived && !signal?.aborted) {
    throw new IncompleteSseStreamError()
  }
}
