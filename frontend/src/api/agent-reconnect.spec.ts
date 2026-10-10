import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('./auth', () => ({
  http: {
    post: vi.fn(),
    get: vi.fn(),
  },
  unwrap: async <T>(promise: Promise<{ data: { success: boolean; data: T; message?: string } }>): Promise<T> => {
    const resp = await promise
    if (!resp.data.success) throw new Error(resp.data.message || '请求失败')
    return resp.data.data
  },
  getAccessToken: () => 'token',
  API_BASE_URL: 'http://api.test',
}))

import { agentApi } from './agent'
import { http } from './auth'

function ok<T>(data: T) {
  return Promise.resolve({ data: { success: true, data } })
}

function sse(text: string): Response {
  const stream = new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(text))
      controller.close()
    },
  })
  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}

describe('agent stream reconnect', () => {
  beforeEach(() => {
    vi.mocked(http.post).mockReset()
    vi.mocked(http.get).mockReset()
    vi.stubGlobal('fetch', vi.fn())
  })

  it('replays the missing events once when the stream drops after the run is already terminal', async () => {
    vi.mocked(http.post).mockReturnValue(ok({
      runId: 'run-1',
      sessionId: 3,
      status: 'queued',
      lastSequence: 0,
    }) as never)
    vi.mocked(http.get).mockReturnValue(ok({
      runId: 'run-1',
      sessionId: 3,
      status: 'completed',
      lastSequence: 6,
    }) as never)

    const fetchMock = vi.mocked(fetch)
    fetchMock
      .mockResolvedValueOnce(sse('id: 4\nevent: token\ndata: {"content":"hello"}\n\n'))
      .mockResolvedValueOnce(sse('id: 5\nevent: token\ndata: {"content":" world"}\n\nid: 6\nevent: done\ndata: {"messageId":1,"content":"hello world"}\n\n'))

    const events: { type: string; data: unknown }[] = []
    await agentApi.sendAgentMessageStream(3, { content: 'hi' }, (event) => {
      events.push(event)
    })

    expect(events.map(event => event.type)).toEqual(['token', 'token', 'done'])
    expect(events[1]?.data).toEqual({ content: ' world' })
    expect(events[2]?.data).toEqual({ messageId: 1, content: 'hello world' })
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(String(fetchMock.mock.calls[1]?.[0])).toBe('http://api.test/chat/agent-runs/run-1/events?afterSequence=4')
    expect(http.get).toHaveBeenCalledTimes(1)
  })
})
