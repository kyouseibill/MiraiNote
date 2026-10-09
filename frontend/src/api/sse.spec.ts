import { describe, expect, it } from 'vitest'
import { consumeSseResponseUntilTerminal } from './sse'
import { shouldReconnectAgentStream } from './agent'

function hangingStream(chunks: string[]): Response {
  const encoder = new TextEncoder()
  let index = 0
  const stream = new ReadableStream<Uint8Array>({
    async pull(controller) {
      if (index < chunks.length) {
        controller.enqueue(encoder.encode(chunks[index]))
        index += 1
        return
      }
      await new Promise(() => {})
    },
  })
  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}

describe('agent event stream', () => {
it('terminal event closes the reader even if the server leaves the body open', async () => {
  const types: string[] = []
  const finished = consumeSseResponseUntilTerminal(
    hangingStream(['id: 2\nevent: done\ndata: {"ok":true}\n\n']),
    (event) => { types.push(event.type) },
  )
  const result = await Promise.race([
    finished.then(() => 'closed'),
    new Promise<string>((resolve) => setTimeout(() => resolve('open'), 500)),
  ])
  expect(result).toBe('closed')
  expect(types).toEqual(['done'])
})

it('terminal and recoverable runs do not reconnect', () => {
  expect(shouldReconnectAgentStream('completed')).toBe(false)
  expect(shouldReconnectAgentStream('failed')).toBe(false)
  expect(shouldReconnectAgentStream('stopped')).toBe(false)
  expect(shouldReconnectAgentStream('recoverable')).toBe(false)
  expect(shouldReconnectAgentStream('queued')).toBe(true)
  expect(shouldReconnectAgentStream('running')).toBe(true)
})
})
