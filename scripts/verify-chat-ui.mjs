/**
 * Browser regression for Mirai Chat. Every API request is intercepted; no real
 * backend, model, credentials, or user records are used.
 * Run: node scripts/verify-chat-ui.mjs [--baseline]
 * Optional: CHAT_UI_URL, PLAYWRIGHT_MODULE_PATH, CHAT_UI_OUTPUT_DIR.
 */
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { randomUUID } from 'node:crypto'

const require = createRequire(import.meta.url)
let playwright
for (const modulePath of [process.env.PLAYWRIGHT_MODULE_PATH, 'playwright', 'C:/Users/18852/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'].filter(Boolean)) {
  try { playwright = require(modulePath); break } catch { /* Try the next installation. */ }
}
if (!playwright) throw new Error('Playwright unavailable. Set PLAYWRIGHT_MODULE_PATH to an installed Playwright package.')

const baseURL = process.env.CHAT_UI_URL || 'http://localhost:5174'
const outputDir = resolve(process.env.CHAT_UI_OUTPUT_DIR || '.product-design')
const baseline = process.argv.includes('--baseline')
const report = { mode: baseline ? 'baseline' : 'regression', startedAt: new Date().toISOString(), checks: [], errors: [], screenshots: [], apiRequests: [], blockedRequests: [], unhandledApi: [] }
const now = new Date().toISOString()
const daysAgo = (days) => new Date(Date.now() - days * 86_400_000).toISOString()
const summary = (session) => { const { messages, ...rest } = session; return rest }
const clone = (value) => JSON.parse(JSON.stringify(value))

function fixtures() {
  return {
    nextId: 100,
    projects: [{ id: 1, name: '日常工作', instructions: '回答简洁清晰，帮助梳理工作记录。', color: '#0f766e', icon: '◇', sessionCount: 2, createdAt: now, updatedAt: now }],
    sessions: [
      { id: 1, title: '梳理本周的工作重点', isArchived: false, isPinned: true, projectId: 1, createdAt: now, updatedAt: now, messages: [
        { id: 11, role: 'user', content: '帮我整理本周的工作，看看下一步可以从哪里开始。', createdAt: now },
        { id: 12, role: 'assistant', content: '当然。我们可以先从最重要的三件事开始。\n\n### 本周的工作重点\n\n1. **完成聊天页面的交互优化**，让每一次记录更轻松。\n2. 整理项目进展，明确下周的优先事项。\n3. 留一点时间，回顾已经完成的工作。\n\n你想先聊哪一部分？\n\n[本周工作摘要.md](/api/v1/mirai/exports/11/2026/09/weekly-summary.md)', createdAt: now },
      ] },
      { id: 2, title: '周末散步与阅读计划', isArchived: false, isPinned: false, projectId: null, createdAt: daysAgo(1), updatedAt: daysAgo(1), messages: [
        { id: 21, role: 'user', content: '周末想去公园走走，也留一点时间读书。', createdAt: daysAgo(1) },
        { id: 22, role: 'assistant', content: '把上午留给散步，下午安静地读几页书。给自己一个不用赶路的周末。', createdAt: daysAgo(1) },
      ] },
      { id: 3, title: 'HTML 与长对话回归样本', isArchived: false, isPinned: false, projectId: 1, createdAt: daysAgo(3), updatedAt: daysAgo(3), messages: [
        { id: 31, role: 'user', content: '<b>原样文本</b>\n第二行应保留换行', createdAt: daysAgo(3) },
        ...Array.from({ length: 24 }, (_, index) => ({ id: 32 + index, role: index % 2 ? 'assistant' : 'user', content: index % 2 ? `这是第 ${index + 1} 条回顾。保持清楚的目标，也为调整留下空间。\n\n- 回顾已经完成的事项\n- 确认下一步的优先级` : `请帮我回顾第 ${index + 1} 项工作记录。`, createdAt: daysAgo(3) })),
      ] },
      { id: 4, title: '八月灵感记录', isArchived: true, isPinned: false, projectId: null, createdAt: daysAgo(14), updatedAt: daysAgo(14), messages: [] },
    ],
  }
}

async function installMocks(context, state) {
  // route.fulfill returns a complete body. This test-only transport adapter turns
  // the marked mock response into timed SSE frames to exercise real UI streaming.
  await context.addInitScript(() => {
    const originalFetch = window.fetch.bind(window)
    window.fetch = async (...args) => {
      const response = await originalFetch(...args)
      if (response.headers.get('x-chat-ui-mock-stream') !== '1') return response
      const frames = (await response.text()).split('\n\n').filter(Boolean)
      const signal = args[1]?.signal
      let timer
      let cancelled = false
      const stream = new ReadableStream({
        start(controller) {
          let index = 0
          const onAbort = () => { cancelled = true; clearTimeout(timer); controller.error(new DOMException('Aborted', 'AbortError')) }
          signal?.addEventListener('abort', onAbort, { once: true })
          const emit = () => {
            if (cancelled) return
            if (index === frames.length) { signal?.removeEventListener('abort', onAbort); controller.close(); return }
            controller.enqueue(new TextEncoder().encode(frames[index++] + '\n\n'))
            timer = setTimeout(emit, 110)
          }
          if (signal?.aborted) onAbort(); else emit()
        },
        cancel() { cancelled = true; clearTimeout(timer) },
      })
      return new Response(stream, { status: response.status, headers: response.headers })
    }
  })
  await context.route('**/*', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const isApi = /^\/api\/v\d+(?:\/|$)/.test(url.pathname)
    if (!isApi) {
      if (url.origin === new URL(baseURL).origin && !url.pathname.includes('/exports/')) return route.continue()
      if (['data:', 'blob:'].includes(url.protocol)) return route.continue()
      report.blockedRequests.push({ method: request.method(), url: `${url.origin}${url.pathname}` })
      return route.abort()
    }
    const path = url.pathname.replace(/^.*\/api\/v\d+/, '')
    const method = request.method()
    report.apiRequests.push({ method, path })
    const body = request.headers()['content-type']?.includes('application/json') ? request.postDataJSON() || {} : {}
    const json = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ success: status < 400, message: status < 400 ? '' : 'Mock route not implemented', data }) })
    if (method === 'OPTIONS') return route.fulfill({ status: 204 })
    if (state.delayNext?.path === path && state.delayNext.method === method) {
      const delay = state.delayNext.ms
      delete state.delayNext
      await new Promise(resolve => setTimeout(resolve, delay))
    }
    if (state.failNext?.path === path && state.failNext.method === method) {
      const failure = state.failNext
      delete state.failNext
      if (!failure.stream) return json(null, 503)
      const frame = (event, data) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`
      let sse = ''
      if (failure.persistUser) {
        const sessionId = Number(path.match(/\/sessions\/(\d+)\//)?.[1])
        const targetSession = state.sessions.find(session => session.id === sessionId)
        const userMessage = { id: state.nextId++, role: 'user', content: body.content, createdAt: now }
        targetSession?.messages.push(userMessage)
        sse += frame('user_msg', { id: userMessage.id })
        sse += frame('token', { content: '已经开始整理，但模拟服务暂时中断。' })
      }
      sse += frame('error', { message: '模拟服务暂时不可用，请稍后重试。' })
      return route.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream', 'x-chat-ui-mock-stream': '1', 'access-control-expose-headers': 'x-chat-ui-mock-stream' }, body: sse })
    }
    if (path === '/auth/refresh') return json({ accessToken: randomUUID(), accessTokenExpiresAt: new Date(Date.now() + 3_600_000).toISOString(), user: { id: -1, username: 'Alex', email: 'preview@mirainote.local', isAdmin: false, isEmailVerified: true, isActive: true, lastLoginAt: now, createdAt: now } })
    if (path.startsWith('/mirai/exports/') && method === 'GET') {
      state.exportAuthorization = request.headers().authorization || ''
      return route.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.4 mock export' })
    }
    if (path === '/memos/due-popups') return json([])
    if (path === '/chat/sessions/archived') return json(state.sessions.filter(s => s.isArchived).map(summary))
    if (path === '/chat/sessions/search' || path === '/chat/sessions' && method === 'GET') {
      const query = url.searchParams.get('query')?.toLowerCase() || ''
      const project = url.searchParams.get('projectId')
      return json(state.sessions.filter(s => !s.isArchived && (!project || s.projectId === Number(project)) && (!query || (s.title + s.messages.map(m => m.content).join(' ')).toLowerCase().includes(query))).map(s => ({ ...summary(s), ...(query ? { matchSnippet: `搜索结果：${query}` } : {}) })))
    }
    if (path === '/chat/sessions' && method === 'POST') {
      const session = { id: state.nextId++, title: body.title || '新对话', isArchived: false, isPinned: false, projectId: body.projectId ?? null, createdAt: now, updatedAt: now, messages: [] }
      state.sessions.unshift(session)
      return json(summary(session))
    }
    if (path === '/chat/projects' && method === 'GET') return json(state.projects.map(p => ({ ...p, sessionCount: state.sessions.filter(s => s.projectId === p.id && !s.isArchived).length })))
    if (path === '/chat/projects' && method === 'POST') { const project = { id: state.nextId++, ...body, sessionCount: 0, createdAt: now, updatedAt: now }; state.projects.push(project); return json(project) }
    const projectMatch = path.match(/^\/chat\/projects\/(\d+)$/)
    if (projectMatch) {
      const project = state.projects.find(p => p.id === Number(projectMatch[1]))
      if (method === 'PUT') { Object.assign(project, body); return json(project) }
      if (method === 'DELETE') { state.projects = state.projects.filter(p => p.id !== Number(projectMatch[1])); state.sessions.forEach(s => { if (s.projectId === Number(projectMatch[1])) s.projectId = null }); return json(null) }
    }
    const sessionMatch = path.match(/^\/chat\/sessions\/(\d+)(?:\/(.*))?$/)
    const session = sessionMatch && state.sessions.find(s => s.id === Number(sessionMatch[1]))
    if (sessionMatch && !session) return json(null, 404)
    if (session && !sessionMatch[2]) {
      if (method === 'GET') return json(session)
      if (method === 'PUT') { Object.assign(session, body); return json(summary(session)) }
      if (method === 'DELETE') { state.sessions = state.sessions.filter(s => s.id !== session.id); return json(null) }
    }
    if (session && ['pin', 'project', 'archive', 'unarchive', 'branch'].includes(sessionMatch[2])) {
      const action = sessionMatch[2]
      if (action === 'pin') session.isPinned = body.isPinned
      if (action === 'project') session.projectId = body.projectId
      if (action === 'archive') session.isArchived = true
      if (action === 'unarchive') session.isArchived = false
      if (action === 'branch') { const branched = { ...clone(session), id: state.nextId++, title: body.title || `${session.title} · 分支`, branchedFromSessionId: session.id, messages: body.messageId ? session.messages.slice(0, session.messages.findIndex(m => m.id === body.messageId) + 1) : [] }; state.sessions.unshift(branched); return json(branched) }
      return json(summary(session))
    }
    if (path.endsWith('/stream')) {
      const id = state.nextId++
      const userMessage = { id, role: 'user', content: body.content + (body.attachments?.length ? '\n' + body.attachments.map(a => `📎${a.fileName}`).join(' ') : ''), createdAt: now }
      const content = body.content.includes('慢速') ? Array.from({ length: 40 }, (_, i) => `第 ${i + 1} 步：把事情慢慢整理清楚。\n\n`).join('') : '已经收到。让我们一起把想法整理清楚，再从最重要的一步开始。'
      const assistant = { id: state.nextId++, role: 'assistant', content, createdAt: now }
      if (session) session.messages.push(userMessage, assistant)
      state.lastSend = { path, body: clone(body) }
      state.sendCount = (state.sendCount || 0) + 1
      const frame = (event, data) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`
      const chunks = content.match(/.{1,16}/gs) || []
      const sse = frame('user_msg', { id }) + chunks.map(content => frame('token', { content })).join('') + frame('done', { messageId: assistant.id, content, title: session?.title || '临时聊天', createdAt: now })
      return route.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream', 'x-chat-ui-mock-stream': '1', 'access-control-expose-headers': 'x-chat-ui-mock-stream' }, body: sse })
    }
    if (path === '/chat/attachments') return json({ fileName: 'notes.txt', fileType: 'text', textContent: '用于验证附件的示例文本。', fileSizeBytes: 36, mimeType: 'text/plain', isImage: false })
    if (path === '/workspace/files') return json({ scope: url.searchParams.get('scope') || 'private', currentPath: '', entries: [] })
    if (path.endsWith('/confirm')) return json(null)
    report.unhandledApi.push({ method, path })
    return json(null, 501)
  })
}

async function screenshot(page, name) {
  const path = resolve(outputDir, `chat-${name}.png`)
  await page.screenshot({ path, fullPage: true, animations: 'disabled' })
  report.screenshots.push(path)
}

async function check(name, operation) {
  const started = Date.now()
  try { const detail = await operation(); report.checks.push({ name, passed: true, ms: Date.now() - started, ...(detail ? { detail } : {}) }); console.log(`PASS ${name}`) }
  catch (error) { report.checks.push({ name, passed: false, ms: Date.now() - started, error: error.message }); console.error(`FAIL ${name}: ${error.message}`); throw error }
}

async function captureBaseline(page, state) {
  await page.goto(`${baseURL}/chat`)
  await page.getByText('梳理本周的工作重点', { exact: true }).first().waitFor()
  await page.getByText('帮我整理本周的工作，看看下一步可以从哪里开始。', { exact: true }).waitFor()
  await screenshot(page, 'baseline-desktop')
  await page.getByText('HTML 与长对话回归样本', { exact: true }).first().click()
  await page.getByText('原样文本', { exact: true }).waitFor()
  report.checks.push({ name: 'baseline-user-html-is-literal', passed: await page.getByText('<b>原样文本</b>', { exact: false }).count() > 0, detail: 'The baseline is expected to expose the existing HTML rendering defect.' })
  const input = page.locator('textarea').first()
  await input.fill('中文输入法确认候选词')
  const before = state.sendCount || 0
  await input.dispatchEvent('compositionstart')
  await input.dispatchEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 229, isComposing: true, bubbles: true })
  await page.waitForTimeout(250)
  report.checks.push({ name: 'baseline-ime-enter-does-not-send', passed: (state.sendCount || 0) === before, detail: `Mock send count changed by ${(state.sendCount || 0) - before}.` })
  await input.dispatchEvent('compositionend')
  await page.goto(`${baseURL}/dashboard?designPreview=1`)
  await page.getByText('今天，把重要的一件事做好', { exact: false }).waitFor()
  await screenshot(page, 'baseline-dashboard')
}

async function runRegression(page, state) {
  const input = page.getByTestId('chat-input')
  const search = page.getByTestId('chat-search')
  const messages = page.getByTestId('chat-messages')
  const sessionButton = title => page.locator('.chat-session-select').filter({ has: page.getByText(title, { exact: true }) })
  const openSession = async title => { await sessionButton(title).click(); await page.getByRole('heading', { name: title, exact: true }).waitFor() }
  const openMenu = async title => page.getByRole('button', { name: `更多操作：${title}`, exact: true }).click()
  const waitForSend = async () => { await page.getByRole('button', { name: '停止生成', exact: true }).waitFor({ state: 'hidden' }); await page.getByTestId('chat-send').waitFor() }
  const closeDialog = async name => { await page.getByRole('dialog', { name, exact: true }).getByRole('button', { name: '关闭对话框', exact: true }).click(); await page.getByRole('dialog', { name, exact: true }).waitFor({ state: 'hidden' }) }
  await page.goto(`${baseURL}/chat`)
  await page.getByTestId('chat-shell').waitFor()
  await page.getByText('帮我整理本周的工作，看看下一步可以从哪里开始。', { exact: true }).waitFor()
  await check('desktop-layout', async () => {
    assert(await page.getByTestId('chat-sidebar').isVisible())
    assert(await page.getByTestId('chat-composer').isVisible())
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1))
    await screenshot(page, 'desktop')
  })
  await check('search-results-no-results-and-clear', async () => {
    await search.fill('zz-no-matching-conversation')
    await page.getByText('没有找到相关对话', { exact: true }).waitFor()
    assert(await messages.getByText('帮我整理本周的工作，看看下一步可以从哪里开始。', { exact: true }).isVisible(), 'Searching must preserve the open conversation.')
    await page.getByTestId('chat-search-clear').click()
    await sessionButton('周末散步与阅读计划').waitFor()
    assert.equal(await search.inputValue(), '')
    await search.fill('阅读')
    await sessionButton('周末散步与阅读计划').waitFor()
    await page.waitForFunction(() => document.querySelectorAll('.chat-session-select').length === 1)
    await page.getByTestId('chat-search-clear').click()
    await sessionButton('HTML 与长对话回归样本').waitFor()
  })
  await check('drafts-survive-conversation-switch', async () => {
    await input.fill('工作对话未发送的草稿')
    await openSession('周末散步与阅读计划')
    assert.equal(await input.inputValue(), '')
    await input.fill('周末对话的另一份草稿')
    await openSession('梳理本周的工作重点')
    assert.equal(await input.inputValue(), '工作对话未发送的草稿')
    await openSession('周末散步与阅读计划')
    assert.equal(await input.inputValue(), '周末对话的另一份草稿')
    await input.fill('')
  })
  await check('user-html-is-literal-and-newlines-preserved', async () => {
    await openSession('HTML 与长对话回归样本')
    const userMessage = page.locator('.chat-user-content').first()
    assert.equal(await userMessage.textContent(), '<b>原样文本</b>\n第二行应保留换行')
    assert.equal(await userMessage.locator('b').count(), 0)
    assert(['pre-wrap', 'break-spaces', 'pre-line'].includes(await userMessage.evaluate(el => getComputedStyle(el).whiteSpace)))
  })
  await check('ime-enter-shift-enter-and-normal-enter', async () => {
    await input.fill('中文输入法确认候选词')
    const before = state.sendCount || 0
    await input.dispatchEvent('compositionstart')
    await input.dispatchEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 229, isComposing: true, bubbles: true })
    await page.waitForTimeout(200)
    assert.equal(state.sendCount || 0, before, 'IME Enter must not send.')
    assert.equal(await input.inputValue(), '中文输入法确认候选词')
    await input.dispatchEvent('compositionend')
    await input.press('End')
    await input.press('Shift+Enter')
    assert((await input.inputValue()).includes('\n'), 'Shift+Enter should insert a newline.')
    assert.equal(state.sendCount || 0, before)
    await input.fill('   ')
    assert(await page.getByTestId('chat-send').isDisabled())
    await input.fill('你好，聊一聊吧')
    await input.press('Enter')
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await waitForSend()
    assert.equal(state.sendCount, before + 1)
    assert.equal(state.lastSend.body.content, '你好，聊一聊吧')
    assert.equal(await input.inputValue(), '')
    assert(await messages.getByText('已经收到。让我们一起把想法整理清楚，再从最重要的一步开始。', { exact: true }).isVisible())
  })
  await check('streaming-respects-reading-position-and-can-stop', async () => {
    await input.fill('慢速回复，帮我分析工作记录')
    await page.getByTestId('chat-send').click()
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await messages.getByText('第 1 步：把事情慢慢整理清楚。', { exact: false }).waitFor()
    await messages.evaluate(el => { el.scrollTop = 0; el.dispatchEvent(new Event('scroll')) })
    await page.waitForTimeout(650)
    assert((await messages.evaluate(el => el.scrollTop)) < 5, 'A streaming reply must not pull a reader back to the bottom.')
    await page.getByTestId('chat-latest').waitFor()
    await screenshot(page, 'streaming-reading-history')
    await page.getByTestId('chat-latest').click()
    assert((await messages.evaluate(el => el.scrollHeight - el.scrollTop - el.clientHeight)) < 10)
    await page.getByRole('button', { name: '停止生成', exact: true }).click()
    await waitForSend()
  })
  await check('conversation-files-and-copy', async () => {
    await openSession('梳理本周的工作重点')
    await page.getByRole('button', { name: '复制消息', exact: true }).first().click()
    assert.equal(await page.evaluate(() => navigator.clipboard.readText()), '帮我整理本周的工作，看看下一步可以从哪里开始。')
    await page.getByRole('button', { name: '对话文件', exact: false }).click()
    const dialog = page.getByRole('dialog', { name: '对话文件', exact: true })
    await dialog.waitFor()
    const downloadLink = dialog.getByRole('link', { name: '下载 本周工作摘要.md', exact: true })
    assert.equal(await downloadLink.getAttribute('href'), 'http://localhost:5273/api/v1/mirai/exports/11/2026/09/weekly-summary.md')
    const download = page.waitForEvent('download')
    await downloadLink.click()
    await download
    assert.match(state.exportAuthorization, /^Bearer /, 'Export download must use the current access token.')
    await screenshot(page, 'files-drawer')
    await closeDialog('对话文件')
  })
  await check('new-conversation-starters-and-text-attachment', async () => {
    await page.getByTestId('chat-new').click()
    await page.getByRole('heading', { name: '把想法，慢慢聊清楚。', exact: true }).waitFor()
    await screenshot(page, 'welcome-desktop')
    const sends = state.sendCount
    await page.locator('.chat-starters button').first().click()
    assert((await input.inputValue()).length > 0)
    assert.equal(state.sendCount, sends, 'Conversation starters should prepare a draft.')
    const file = { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('这是待分析的附件示例文本。', 'utf8') }
    await page.locator('input[type=file]').setInputFiles(file)
    const attachment = page.getByRole('button', { name: '移除附件 notes.txt', exact: true })
    await attachment.waitFor()
    await attachment.click()
    assert.equal(await attachment.count(), 0)
    await page.locator('input[type=file]').setInputFiles(file)
    await attachment.waitFor()
    await input.fill('请分析这份附件')
    await page.getByTestId('chat-send').click()
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await waitForSend()
    assert.equal(state.lastSend.body.attachments[0].fileName, 'notes.txt')
    assert.equal(state.lastSend.body.attachments[0].textContent, '这是待分析的附件示例文本。')
    assert.equal(await attachment.count(), 0)
  })
  await check('project-form-validation-save-and-dialog-keyboard', async () => {
    await page.getByRole('button', { name: '管理项目', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '项目空间', exact: true })
    await dialog.waitFor()
    await dialog.getByRole('button', { name: '保存项目', exact: true }).click()
    await dialog.getByText('请填写项目名称。', { exact: true }).waitFor()
    await dialog.getByRole('textbox', { name: '项目名称', exact: true }).fill('浏览器回归项目')
    await dialog.getByRole('textbox', { name: '项目专属指令', exact: true }).fill('使用中文，先整理想法，再给出下一步。')
    await dialog.getByRole('button', { name: '保存项目', exact: true }).click()
    await dialog.getByRole('button', { name: /浏览器回归项目/ }).waitFor()
    assert(state.projects.some(p => p.name === '浏览器回归项目'))
    await screenshot(page, 'project-dialog')
    for (let i = 0; i < 16; i++) {
      await page.keyboard.press('Tab')
      // Chromium can send Tab to browser chrome at the end of a native dialog;
      // document.body is then active. Background application controls must never
      // receive focus, and the following Tab must return inside the dialog.
      const focused = await dialog.evaluate(el => ({ inside: el.contains(document.activeElement), browserChrome: document.activeElement === document.body, html: document.activeElement?.outerHTML.slice(0, 250) }))
      if (focused.browserChrome) {
        await page.keyboard.press('Tab')
        assert(await dialog.evaluate(el => el.contains(document.activeElement)), 'Focus should return to the modal after browser chrome.')
      } else assert(focused.inside, `Modal focus reached a background control: ${focused.html}`)
    }
    await page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'hidden' })
    assert(await page.getByRole('button', { name: '管理项目', exact: true }).evaluate(el => el === document.activeElement))
    await page.getByRole('combobox', { name: '项目空间', exact: true }).selectOption('')
    await sessionButton('周末散步与阅读计划').waitFor()
  })
  await check('rename-archive-restore-and-delete-confirmation', async () => {
    await openMenu('周末散步与阅读计划')
    await page.getByRole('button', { name: '重命名', exact: true }).click()
    const rename = page.getByRole('dialog', { name: '重命名对话', exact: true })
    await rename.waitFor()
    await rename.getByRole('textbox', { name: '对话名称', exact: true }).fill('周末留白计划')
    await rename.getByRole('button', { name: '保存名称', exact: true }).click()
    await rename.waitFor({ state: 'hidden' })
    await sessionButton('周末留白计划').waitFor()
    assert.equal(state.sessions.find(s => s.id === 2).title, '周末留白计划')
    await openMenu('周末留白计划')
    await page.getByRole('button', { name: '归档', exact: true }).click()
    const archive = page.getByRole('dialog', { name: '归档这段对话？', exact: true })
    await archive.waitFor()
    await archive.getByRole('button', { name: '取消', exact: true }).click()
    assert.equal(state.sessions.find(s => s.id === 2).isArchived, false)
    await openMenu('周末留白计划')
    await page.getByRole('button', { name: '归档', exact: true }).click()
    await archive.getByRole('button', { name: '归档', exact: true }).click()
    await archive.waitFor({ state: 'hidden' })
    assert.equal(state.sessions.find(s => s.id === 2).isArchived, true)
    await page.getByRole('button', { name: '归档管理', exact: true }).click()
    const archiveManager = page.getByRole('dialog', { name: '归档管理', exact: true })
    await archiveManager.getByText('周末留白计划', { exact: false }).waitFor()
    await screenshot(page, 'archive-dialog')
    await archiveManager.locator('.chat-archive-list > div').filter({ hasText: '周末留白计划' }).getByRole('button', { name: '还原', exact: true }).click()
    await sessionButton('周末留白计划').waitFor({ state: 'attached' })
    assert.equal(state.sessions.find(s => s.id === 2).isArchived, false)
    await closeDialog('归档管理')
    await openMenu('周末留白计划')
    await page.getByRole('button', { name: '删除', exact: true }).click()
    const deletion = page.getByRole('dialog', { name: '删除这段对话？', exact: true })
    await deletion.waitFor()
    await screenshot(page, 'delete-confirmation')
    await deletion.getByRole('button', { name: '取消', exact: true }).click()
    assert(state.sessions.some(s => s.id === 2), 'Cancel must preserve the conversation.')
    await openMenu('周末留白计划')
    await page.getByRole('button', { name: '删除', exact: true }).click()
    await deletion.getByRole('button', { name: '删除', exact: true }).click()
    await deletion.waitFor({ state: 'hidden' })
    assert(!state.sessions.some(s => s.id === 2))
    assert.equal(await sessionButton('周末留白计划').count(), 0)
  })
  await check('temporary-conversation-is-clear-and-not-persisted', async () => {
    const count = state.sessions.length
    await page.getByTestId('chat-sidebar').getByRole('button', { name: /临时聊天/ }).click()
    await page.getByText('这段对话只留在此刻。关闭或切换后，内容会丢失，不会保存到历史记录。', { exact: true }).waitFor()
    await screenshot(page, 'temporary-desktop')
    await input.fill('你好，这是临时对话')
    await page.getByTestId('chat-send').click()
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await waitForSend()
    assert(state.lastSend.path.includes('/chat/temporary/'))
    assert.equal(state.sessions.length, count)
    assert.deepEqual(state.lastSend.body.history, [])
    await input.fill('继续聊聊')
    await page.getByTestId('chat-send').click()
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await waitForSend()
    assert.equal(state.lastSend.body.history.length, 2)
    await openSession('梳理本周的工作重点')
    assert.equal(await messages.getByText('你好，这是临时对话', { exact: true }).count(), 0)
  })
  await check('desktop-sidebar-collapse', async () => {
    const toggle = page.getByTestId('chat-sidebar-toggle')
    await toggle.click()
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false')
    await toggle.click()
    assert.equal(await toggle.getAttribute('aria-expanded'), 'true')
  })
  await check('mobile-layout-navigation-and-dialog', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto(`${baseURL}/chat`)
    await page.getByTestId('chat-shell').waitFor()
    await page.getByTestId('chat-input').waitFor()
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'The mobile page must not overflow horizontally.')
    const composer = await page.getByTestId('chat-composer').boundingBox()
    assert(composer && composer.y + composer.height <= 844, 'The mobile composer must fit inside the visible viewport.')
    await screenshot(page, 'mobile')
    await page.getByRole('button', { name: '打开对话列表', exact: true }).click()
    await page.getByTestId('chat-sidebar').getByRole('button', { name: '新对话', exact: true }).waitFor()
    await screenshot(page, 'mobile-navigation')
    for (let i = 0; i < 18; i++) {
      await page.keyboard.press('Tab')
      const focus = await page.getByTestId('chat-sidebar').evaluate(el => ({ inside: el.contains(document.activeElement), dialog: !!document.activeElement?.closest('dialog[open]'), browserChrome: document.activeElement === document.body }))
      assert(focus.inside || focus.dialog || focus.browserChrome, 'Mobile navigation must keep keyboard focus away from the background.')
    }
    await page.keyboard.press('Escape')
    const mobileToggle = page.getByRole('button', { name: '打开对话列表', exact: true })
    assert.equal(await mobileToggle.getAttribute('aria-expanded'), 'false')
    assert(await mobileToggle.evaluate(el => el === document.activeElement), 'Escape must return focus to the mobile navigation trigger.')
    await mobileToggle.click()
    await openSession('梳理本周的工作重点')
    assert.equal(await page.getByRole('button', { name: '打开对话列表', exact: true }).getAttribute('aria-expanded'), 'false')
    await page.getByRole('button', { name: '对话文件', exact: false }).click()
    const drawer = page.getByRole('dialog', { name: '对话文件', exact: true })
    await drawer.waitFor()
    const bounds = await drawer.boundingBox()
    assert(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 391)
    await screenshot(page, 'mobile-files')
    await closeDialog('对话文件')
    await page.getByRole('button', { name: '打开对话列表', exact: true }).click()
    await page.getByTestId('chat-new').click()
    await page.getByRole('heading', { name: '把想法，慢慢聊清楚。', exact: true }).waitFor()
    await screenshot(page, 'welcome-mobile')
  })
  await check('compact-viewport-and-two-hundred-percent-layout', async () => {
    // A desktop browser zoom of 200% halves the effective CSS viewport. This
    // verifies reflow at that equivalent width; it does not simulate OS scaling.
    for (const viewport of [{ width: 768, height: 900 }, { width: 720, height: 500 }]) {
      await page.setViewportSize(viewport)
      await page.goto(`${baseURL}/chat`)
      await input.waitFor()
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `Layout overflows at ${viewport.width}px.`)
      const composer = await page.getByTestId('chat-composer').boundingBox()
      assert(composer && composer.y >= 0 && composer.y + composer.height <= viewport.height + 1, `Composer is clipped at ${viewport.width}x${viewport.height}.`)
      assert(await page.getByRole('button', { name: '对话文件', exact: false }).isVisible())
      await screenshot(page, viewport.width === 768 ? 'tablet-768' : 'zoom-200-equivalent')
    }
  })
  await check('list-error-reload-and-detail-loading', async () => {
    await page.setViewportSize({ width: 1440, height: 1000 })
    state.failNext = { path: '/chat/sessions', method: 'GET' }
    await page.goto(`${baseURL}/chat`)
    await page.getByRole('alert').filter({ hasText: '聊天加载失败，请检查网络后重试。' }).waitFor()
    await screenshot(page, 'load-error')
    await page.getByRole('button', { name: '重新加载', exact: true }).click()
    await sessionButton('梳理本周的工作重点').waitFor()
    await page.getByRole('alert').filter({ hasText: '聊天加载失败，请检查网络后重试。' }).waitFor({ state: 'hidden' })
    state.delayNext = { path: '/chat/sessions/3', method: 'GET', ms: 1000 }
    await sessionButton('HTML 与长对话回归样本').click()
    await page.getByRole('status').filter({ hasText: '正在打开对话…' }).waitFor()
    await screenshot(page, 'detail-loading')
    await page.getByRole('heading', { name: 'HTML 与长对话回归样本', exact: true }).waitFor()
    await page.getByRole('status').filter({ hasText: '正在打开对话…' }).waitFor({ state: 'hidden' })
    await openSession('梳理本周的工作重点')
    state.failNext = { path: '/chat/sessions', method: 'POST' }
    await page.getByTestId('chat-new').click()
    await page.getByRole('alert').filter({ hasText: '新对话创建失败，请重试。' }).waitFor()
    assert(await messages.getByText('帮我整理本周的工作，看看下一步可以从哪里开始。', { exact: true }).isVisible())
    await page.getByRole('button', { name: '重试创建', exact: true }).click()
    await page.getByRole('heading', { name: '把想法，慢慢聊清楚。', exact: true }).waitFor()
  })
  await check('http-and-sse-send-failure-draft-recovery-and-retry', async () => {
    for (const stream of [false, true]) {
      const targetSession = state.sessions[0]
      // 不使用“HTTP”等会触发 Agent 路由的词，确保这里覆盖普通对话的
      // HTTP 非 2xx 与 SSE error 两条失败路径。
      const content = stream ? '模拟流式失败后保留的草稿' : '模拟请求失败后保留的草稿'
      const originalCount = targetSession.messages.length
      state.failNext = { path: `/chat/sessions/${targetSession.id}/messages/stream`, method: 'POST', stream }
      await input.fill(content)
      await page.getByTestId('chat-send').click()
      await page.waitForFunction(expected => document.querySelector('[data-testid="chat-input"]')?.value === expected, content)
      assert.equal(targetSession.messages.length, originalCount, 'A pre-persistence failure must not add a server message.')
      assert.equal(await messages.getByText(content, { exact: true }).count(), 0, 'A failed optimistic message must not remain duplicated above the recovered draft.')
      assert(await page.getByTestId('chat-send').isEnabled())
      await screenshot(page, stream ? 'sse-error-draft' : 'http-error-draft')
      if (!stream) await page.getByRole('button', { name: '重试发送', exact: true }).click()
      else await page.getByTestId('chat-send').click()
      await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
      await waitForSend()
      assert.equal(await input.inputValue(), '')
      assert.equal(targetSession.messages.filter(message => message.role === 'user' && message.content === content).length, 1)
      assert.equal(await messages.getByText(content, { exact: true }).count(), 1)
    }
    const targetSession = state.sessions[0]
    state.delayNext = { path: `/chat/sessions/${targetSession.id}/messages/stream`, method: 'POST', ms: 1000 }
    state.failNext = { path: `/chat/sessions/${targetSession.id}/messages/stream`, method: 'POST' }
    await input.fill('即将失败的前一份草稿')
    await page.getByTestId('chat-send').click()
    await page.getByRole('button', { name: '停止生成', exact: true }).waitFor()
    await input.fill('发送过程中写下的新草稿')
    await waitForSend()
    assert.equal(await input.inputValue(), '发送过程中写下的新草稿', 'A failed request must not overwrite newer typing.')
    const content = '模拟已保存消息后的回复中断'
    state.failNext = { path: `/chat/sessions/${targetSession.id}/messages/stream`, method: 'POST', stream: true, persistUser: true }
    await input.fill(content)
    await page.getByTestId('chat-send').click()
    await page.getByText('模拟服务暂时不可用，请稍后重试。', { exact: true }).waitFor()
    await waitForSend()
    assert.equal(await messages.getByText(content, { exact: true }).count(), 1)
    assert.equal(targetSession.messages.filter(message => message.content === content).length, 1)
    await input.fill('继续下一步')
    assert(await page.getByTestId('chat-send').isEnabled())
    await input.fill('')
  })
  await check('pin-project-move-and-message-branches-preserve-original', async () => {
    await openMenu('HTML 与长对话回归样本')
    await page.getByRole('button', { name: '置顶', exact: true }).click()
    await page.locator('.chat-session-group').filter({ has: page.getByRole('heading', { name: '已置顶', exact: true }) }).getByText('HTML 与长对话回归样本', { exact: true }).waitFor()
    assert.equal(state.sessions.find(session => session.id === 3).isPinned, true)
    await openMenu('HTML 与长对话回归样本')
    const moved = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/chat/sessions/3/project'))
    await page.getByRole('combobox', { name: '移动到项目', exact: true }).selectOption('')
    await moved
    assert.equal(state.sessions.find(session => session.id === 3).projectId, null)
    await page.keyboard.press('Escape')
    await openSession('梳理本周的工作重点')
    const originalMessages = clone(state.sessions.find(session => session.id === 1).messages)
    await page.getByRole('button', { name: '从这里分支', exact: true }).first().click()
    await page.getByRole('heading', { name: '梳理本周的工作重点 · 分支', exact: true }).waitFor()
    assert.equal(state.sessions[0].branchedFromSessionId, 1)
    assert.equal(state.sessions[0].messages.length, 1)
    assert.deepEqual(state.sessions.find(session => session.id === 1).messages, originalMessages)
    await openSession('梳理本周的工作重点')
    await page.getByRole('button', { name: '编辑', exact: true }).first().click()
    const edit = page.getByRole('dialog', { name: '编辑消息', exact: true })
    await edit.waitFor()
    await edit.getByRole('textbox', { name: '消息内容', exact: true }).fill('请重新整理我本周最重要的三件事。')
    await edit.getByRole('button', { name: '创建分支并发送', exact: true }).click()
    await page.getByRole('heading', { name: '梳理本周的工作重点 · 编辑', exact: true }).waitFor()
    await waitForSend()
    assert.equal(state.lastSend.body.content, '请重新整理我本周最重要的三件事。')
    assert.deepEqual(state.sessions.find(session => session.id === 1).messages, originalMessages)
    await openSession('梳理本周的工作重点')
    await page.getByRole('button', { name: '重新回答', exact: true }).first().click()
    await page.getByRole('heading', { name: '梳理本周的工作重点 · 重试', exact: true }).waitFor()
    await waitForSend()
    assert.deepEqual(state.sessions.find(session => session.id === 1).messages, originalMessages)
    await screenshot(page, 'message-retry-branch')
  })
}

await mkdir(outputDir, { recursive: true })
const browser = await playwright.chromium.launch({ headless: true })
let activePage
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, locale: 'zh-CN', permissions: ['clipboard-read', 'clipboard-write'], reducedMotion: 'reduce' })
  const state = fixtures()
  await installMocks(context, state)
  const page = await context.newPage()
  activePage = page
  page.setDefaultTimeout(8000)
  page.on('pageerror', error => report.errors.push(error.message))
  if (baseline) await captureBaseline(page, state)
  else await runRegression(page, state)
  assert.equal(report.errors.length, 0, `Browser errors: ${report.errors.join('; ')}`)
  assert.equal(report.unhandledApi.length, 0, `Unhandled API mocks: ${JSON.stringify(report.unhandledApi)}`)
} catch (error) {
  report.failure = error.stack || error.message
  if (activePage) { await screenshot(activePage, baseline ? 'baseline-failure' : 'verification-failure'); report.visibleTextOnFailure = await activePage.locator('body').innerText() }
  process.exitCode = 1
} finally {
  report.finishedAt = new Date().toISOString()
  await writeFile(resolve(outputDir, `chat-${baseline ? 'baseline' : 'verification'}.json`), JSON.stringify(report, null, 2) + '\n')
  await browser.close()
  console.log(JSON.stringify({ mode: report.mode, checks: report.checks.map(({ name, passed }) => ({ name, passed })), errors: report.errors, failure: report.failure, screenshots: report.screenshots }, null, 2))
}
