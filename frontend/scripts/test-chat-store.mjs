import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { test } from 'node:test'
import vm from 'node:vm'
import ts from 'typescript'

// Exercise the real store and Vue/Pinia state; only HTTP/SSE and toast are isolated.
const require = createRequire(import.meta.url)
const { createPinia, setActivePinia } = require('pinia')
const source = await readFile(new URL('../src/stores/chat.ts', import.meta.url), 'utf8')
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
})

function deferred() {
  let resolve, reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function detail(id, title = `Session ${id}`, projectId = null) {
  return {
    id, title, projectId, isArchived: false, isPinned: false,
    branchedFromSessionId: null, branchedFromMessageId: null,
    createdAt: '2026-09-05T00:00:00Z', updatedAt: '2026-09-05T00:00:00Z', messages: [],
  }
}

function createStore(chatOverrides = {}, agentOverrides = {}) {
  const chatApi = { getSessions: async () => [], ...chatOverrides }
  const errors = []
  const module = { exports: {} }
  const load = (id) => {
    if (id === '@/api/chat') return { chatApi }
    if (id === '@/api/agent') return { agentApi: agentOverrides }
    if (id === '@/composables/useToast') return { useToast: () => ({ error: (text) => errors.push(text) }) }
    return require(id)
  }
  vm.runInThisContext(`(function(require, module, exports) { ${outputText}\n})`)(load, module, module.exports)
  setActivePinia(createPinia())
  return { store: module.exports.useChatStore(), errors }
}

test('a late initial list cannot overwrite newer search results', async () => {
  const initial = deferred(), search = deferred()
  const { store } = createStore({ getSessions: () => initial.promise, searchSessions: () => search.promise })
  const first = store.fetchSessions()
  const second = store.searchSessions('week')
  search.resolve([detail(2)])
  await second
  initial.resolve([detail(1)])
  await first
  assert.deepEqual(store.sessions.map((item) => item.id), [2])
})

test('old search results cannot replace the newly selected project', async () => {
  const search = deferred(), project = deferred()
  const { store } = createStore({ searchSessions: () => search.promise, getSessions: () => project.promise })
  const first = store.searchSessions('old')
  const second = store.selectProject(8)
  project.resolve([detail(8, 'Project conversation', 8)])
  await second
  search.resolve([detail(1)])
  await first
  assert.equal(store.selectedProjectId, 8)
  assert.deepEqual(store.sessions.map((item) => item.id), [8])
})

test('list loading is independent and stays active until the newest search completes', async () => {
  const firstSearch = deferred(), latestSearch = deferred()
  const { store } = createStore({ searchSessions: (query) => query === 'a' ? firstSearch.promise : latestSearch.promise })
  const first = store.searchSessions('a')
  const latest = store.searchSessions('ab')
  firstSearch.resolve([])
  await first
  assert.equal(store.loading, false, 'list requests must not blank the conversation')
  assert.equal(store.sessionsLoading, true)
  latestSearch.resolve([])
  await latest
  assert.equal(store.sessionsLoading, false)
})

test('a late conversation response cannot replace the latest opened conversation', async () => {
  const old = deferred(), recent = deferred()
  const { store } = createStore({ getSession: (id) => id === 1 ? old.promise : recent.promise })
  const first = store.openSession(1)
  const second = store.openSession(2)
  recent.resolve(detail(2))
  await second
  old.resolve(detail(1))
  await first
  assert.equal(store.currentSession.id, 2)
})

test('finishing an older detail request does not hide the latest loading state', async () => {
  const old = deferred(), recent = deferred()
  const { store } = createStore({ getSession: (id) => id === 1 ? old.promise : recent.promise })
  const first = store.openSession(1)
  const second = store.openSession(2)
  old.resolve(detail(1))
  await first
  assert.equal(store.loading, true)
  recent.resolve(detail(2))
  await second
  assert.equal(store.loading, false)
})

test('starting a temporary conversation invalidates pending detail requests', async () => {
  const old = deferred()
  const { store } = createStore({ getSession: () => old.promise })
  const pending = store.openSession(1)
  store.startTemporarySession()
  old.resolve(detail(1))
  await pending
  assert.equal(store.currentSession.id, 0)
  assert.equal(store.isTemporary, true)
  assert.equal(store.loading, false)
})

test('creating a conversation invalidates a pending open immediately', async () => {
  const old = deferred(), created = deferred()
  const { store } = createStore({ getSession: () => old.promise, createSession: () => created.promise })
  const opening = store.openSession(1)
  const creating = store.createSession()
  old.resolve(detail(1))
  await opening
  assert.equal(store.currentSession, null, 'the older open must not steal selection while creation is pending')
  created.resolve(detail(3))
  await creating
  assert.equal(store.currentSession.id, 3)
})

test('a pending creation does not steal a newer temporary conversation', async () => {
  const created = deferred()
  const { store } = createStore({ createSession: () => created.promise })
  const creating = store.createSession()
  store.startTemporarySession()
  created.resolve(detail(3))
  await creating
  assert.equal(store.currentSession.id, 0)
  assert.equal(store.isTemporary, true)
  assert.equal(store.sessions[0].id, 3, 'the created server conversation is still retained')
})

test('changing project invalidates a pending conversation open', async () => {
  const old = deferred()
  const { store } = createStore({ getSession: () => old.promise })
  const opening = store.openSession(1)
  await store.selectProject(8)
  old.resolve(detail(1))
  await opening
  assert.equal(store.currentSession, null)
  assert.equal(store.loading, false)
})

test('an older cache refresh cannot overwrite a newer refresh of the same conversation', async () => {
  const old = deferred(), latest = deferred()
  let count = 0
  const { store } = createStore({ getSession: () => ++count === 1 ? Promise.resolve(detail(1)) : count === 2 ? old.promise : latest.promise })
  await store.openSession(1)
  await store.openSession(1)
  await store.openSession(1)
  latest.resolve(detail(1, 'Latest title'))
  await latest.promise
  await Promise.resolve()
  old.resolve(detail(1, 'Outdated title'))
  await old.promise
  await Promise.resolve()
  assert.equal(store.currentSession.title, 'Latest title')
})

test('attachment drafts follow their conversation across cached and uncached opens', async () => {
  const { store } = createStore({ getSession: async (id) => detail(id) })
  await store.openSession(1)
  store.pendingAttachments = [{ fileName: 'one.txt' }]
  await store.openSession(2)
  assert.deepEqual(store.pendingAttachments, [])
  store.pendingAttachments = [{ fileName: 'two.txt' }]
  await store.openSession(1)
  assert.equal(store.pendingAttachments[0].fileName, 'one.txt')
  await store.openSession(2)
  assert.equal(store.pendingAttachments[0].fileName, 'two.txt')
})

test('attachments selected before first send move into the newly created conversation', async () => {
  const { store } = createStore({ createSession: async () => detail(1) })
  store.pendingAttachments = [{ fileName: 'new.txt' }]
  await store.createSession()
  assert.equal(store.pendingAttachments[0].fileName, 'new.txt')
})

test('new and temporary conversations preserve the previous conversation attachment draft', async () => {
  const { store } = createStore({ getSession: async (id) => detail(id), createSession: async () => detail(2) })
  await store.openSession(1)
  store.pendingAttachments = [{ fileName: 'one.txt' }]
  await store.createSession()
  assert.deepEqual(store.pendingAttachments, [])
  await store.openSession(1)
  assert.equal(store.pendingAttachments[0].fileName, 'one.txt')
  store.startTemporarySession()
  assert.deepEqual(store.pendingAttachments, [])
  await store.openSession(1)
  assert.equal(store.pendingAttachments[0].fileName, 'one.txt')
})

test('cached detail refresh preserves an active streamed reply', async () => {
  const refresh = deferred(), stream = deferred()
  let onEvent
  let count = 0
  const { store } = createStore({
    getSession: () => ++count === 1 ? Promise.resolve(detail(1)) : refresh.promise,
    sendMessageStream: (_id, _payload, callback) => { onEvent = callback; return stream.promise },
  })
  await store.openSession(1)
  const sending = store.sendMessageStream('Hello')
  await store.openSession(1)
  refresh.resolve(detail(1, 'Stale title'))
  await refresh.promise
  await Promise.resolve()
  onEvent({ type: 'user_msg', data: { id: 10 } })
  onEvent({ type: 'token', data: { content: 'Response' } })
  onEvent({ type: 'done', data: { messageId: 11, content: 'Response', title: 'Current title' } })
  stream.resolve()
  await sending
  assert.equal(store.currentSession.title, 'Current title')
  assert.deepEqual(store.currentSession.messages.map((item) => item.content), ['Hello', 'Response'])
})

for (const method of ['sendMessageStream', 'sendAgentMessageStream']) {
  test(`${method} restores failed attachments to their originating conversation`, async () => {
    const pending = deferred()
    const send = () => pending.promise
    const { store } = createStore({ getSession: async (id) => detail(id), sendMessageStream: send }, { sendAgentMessageStream: send })
    await store.openSession(1)
    store.pendingAttachments = [{ fileName: 'one.txt' }]
    const sending = store[method]('Hello')
    await store.openSession(2)
    store.pendingAttachments = [{ fileName: 'two.txt' }]
    pending.reject(new Error('Connection lost'))
    await sending
    assert.deepEqual(store.pendingAttachments.map((item) => item.fileName), ['two.txt'])
    await store.openSession(1)
    assert.deepEqual(store.pendingAttachments.map((item) => item.fileName), ['one.txt'])
  })

  for (const scenario of [
    { name: 'done', outcome: 'completed' },
    { name: 'error event', outcome: 'failed' },
    { name: 'network error', outcome: 'failed' },
    { name: 'aborted', outcome: 'stopped' },
    { name: 'abort without rejection', outcome: 'stopped' },
  ]) {
    test(`${method} reports ${scenario.outcome} after ${scenario.name}`, async () => {
      const pending = deferred()
      let callback
      const send = (_id, _payload, onEvent) => { callback = onEvent; return pending.promise }
      const { store } = createStore({ getSession: async () => detail(1), sendMessageStream: send }, { sendAgentMessageStream: send })
      await store.openSession(1)
      store.pendingAttachments = [{ fileName: 'notes.txt', content: 'notes', contentType: 'text/plain' }]
      const result = store[method]('Hello')
      if (scenario.name === 'done') {
        callback({ type: 'user_msg', data: { id: 10 } })
        callback({ type: 'done', data: { messageId: 11, content: 'Response', title: 'Current' } })
        pending.resolve()
      } else if (scenario.name === 'error event') {
        callback({ type: 'error', data: { message: 'HTTP 503' } })
        pending.resolve()
      } else if (scenario.name === 'network error') {
        pending.reject(new Error('Connection lost'))
      } else {
        store.stopGeneration()
        if (scenario.name === 'aborted') pending.reject(new DOMException('Cancelled', 'AbortError'))
        else pending.resolve()
      }
      assert.equal(await result, scenario.outcome)
      assert.equal(store.sending, false)
      assert.equal(store.streamMessage, null)
      if (scenario.outcome === 'failed') {
        assert.equal(store.pendingAttachments[0].fileName, 'notes.txt')
        assert.equal(store.currentSession.messages.length, 0, 'unsent optimistic messages must not duplicate on retry')
      }
    })
  }
  test(`${method} keeps a persisted user message when the reply fails`, async () => {
    const send = async (_id, _payload, callback) => {
      callback({ type: 'user_msg', data: { id: 10 } })
      callback({ type: 'error', data: { message: 'Model error' } })
    }
    const { store } = createStore({ getSession: async () => detail(1), sendMessageStream: send }, { sendAgentMessageStream: send })
    await store.openSession(1)
    assert.equal(await store[method]('Hello'), 'failed')
    assert.equal(store.currentSession.messages.length, 1)
    assert.equal(store.currentSession.messages[0].id, 10)
  })
}
