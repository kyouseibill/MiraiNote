/** Browser regression for the Skill manager; all API calls are mocked. */
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { randomUUID } from 'node:crypto'
import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'

const require = createRequire(import.meta.url)
let playwright
for (const modulePath of [process.env.PLAYWRIGHT_MODULE_PATH, 'playwright', 'C:/Users/18852/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'].filter(Boolean)) {
  try { playwright = require(modulePath); break } catch { /* Try next package location. */ }
}
if (!playwright) throw new Error('Playwright unavailable')

const baseURL = process.env.CHAT_UI_URL || 'http://127.0.0.1:5174'
const outputDir = resolve(process.env.SKILL_UI_OUTPUT_DIR || '.codex-tmp/skill-ui-check')
const entries = new Map([
  ['yahoo-transit-jp', {
    name: 'yahoo-transit-jp', description: '查询日本铁路和公交换乘。',
    markdown: '---\nname: yahoo-transit-jp\ndescription: 查询日本铁路和公交换乘。\n---\n\n先查询实时路线。',
    enabled: true, allowImplicitInvocation: true, error: null,
  }],
])
const browser = await playwright.chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } })
const page = await context.newPage()
const errors = []
page.on('pageerror', (error) => errors.push(error.message))

await context.route('**/*', async (route) => {
  const request = route.request()
  const url = new URL(request.url())
  if (!url.pathname.startsWith('/api/v1/')) return route.continue()
  const path = url.pathname.replace('/api/v1/', '')
  const method = request.method()
  const body = request.postDataJSON?.() || {}
  const respond = (data, status = 200, message = '') => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ success: status < 400, message, data }) })
  if (path === 'auth/refresh') return respond({ accessToken: randomUUID(), accessTokenExpiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 17, username: 'Test', email: 'test@example.invalid', isAdmin: false, isEmailVerified: true, isActive: true, createdAt: new Date().toISOString() } })
  if (path === 'skills' && method === 'GET') return respond([...entries.values()].map(({ markdown, ...rest }) => rest))
  if (path === 'skills' && method === 'POST') {
    const description = body.markdown.match(/^description:\s*(.*)$/m)?.[1] || ''
    const item = { name: body.name, description, markdown: body.markdown, enabled: true, allowImplicitInvocation: body.allowImplicitInvocation, error: null }
    entries.set(item.name, item)
    return respond(item)
  }
  const match = path.match(/^skills\/([^/]+)(?:\/(enabled))?$/)
  if (match) {
    const name = decodeURIComponent(match[1])
    const item = entries.get(name)
    if (!item) return respond(null, 404, 'Skill 不存在')
    if (method === 'GET') return respond(item)
    if (method === 'PUT') {
      item.markdown = body.markdown
      item.allowImplicitInvocation = body.allowImplicitInvocation
      item.description = body.markdown.match(/^description:\s*(.*)$/m)?.[1] || ''
      return respond(item)
    }
    if (method === 'PATCH' && match[2] === 'enabled') { item.enabled = body.enabled; return respond(item) }
    if (method === 'DELETE') { entries.delete(name); return respond(null) }
  }
  return respond([], 200)
})

try {
  await page.goto(`${baseURL}/skills`, { waitUntil: 'domcontentloaded' })
  await page.getByRole('heading', { name: '技能管理' }).waitFor()
  await page.getByRole('heading', { name: 'yahoo-transit-jp' }).waitFor()
  assert.match(await page.getByLabel('Skill 原文').inputValue(), /先查询实时路线/)
  await mkdir(outputDir, { recursive: true })
  await page.screenshot({ path: resolve(outputDir, 'skills-desktop.png'), fullPage: true })

  await page.getByRole('button', { name: '新建 Skill' }).click()
  await page.getByLabel('名称').fill('my-route')
  const longDescription = `${'甲'.repeat(300)}\n${'乙'.repeat(300)}`
  assert.equal(await page.getByLabel('何时使用').evaluate((element) => element.tagName), 'TEXTAREA')
  await page.getByLabel('何时使用').fill(longDescription)
  await page.getByLabel('执行步骤').fill('先核实出发和到达地，再查询路线。')
  await page.getByRole('button', { name: '创建 Skill' }).click()
  await page.getByRole('heading', { name: 'my-route' }).waitFor()
  assert.ok(entries.has('my-route'))
  assert.ok(entries.get('my-route').markdown.includes(`description: >\n  ${'甲'.repeat(300)}\n  ${'乙'.repeat(300)}\n`))

  await page.getByLabel('Skill 原文').fill('---\nname: my-route\ndescription: 查询跨城交通\n---\n\n更新后的步骤。')
  await page.getByRole('button', { name: '保存修改' }).click()
  assert.match(entries.get('my-route').markdown, /更新后的步骤/)

  await page.getByRole('checkbox', { name: '已启用' }).uncheck()
  assert.equal(entries.get('my-route').enabled, false)

  await page.setViewportSize({ width: 390, height: 844 })
  assert.equal(await page.locator('body').evaluate((body) => body.scrollWidth <= window.innerWidth + 1), true)
  await page.waitForTimeout(3500) // Let the existing mobile sidebar transition and toast complete.
  await page.screenshot({ path: resolve(outputDir, 'skills-mobile.png'), fullPage: true })
  await page.getByRole('button', { name: '删除', exact: true }).click()
  await page.getByRole('button', { name: '删除 Skill', exact: true }).click()
  assert.equal(entries.has('my-route'), false)
  assert.deepEqual(errors, [])
  process.stdout.write('Skill UI: discovery, create, edit, disable, delete, narrow viewport passed.\n')
} finally {
  await browser.close()
}
