/**
 * Lightweight path-rewrite checks for A2 export same-origin download.
 * Mirrors exportApiPath / exportRequestUrl core rules (no Vite/DOM deps).
 */
const EXPORT_API_MARKER = '/api/v1/mirai/exports/'

function exportApiPath(url) {
  const raw = String(url ?? '').trim()
  if (!raw) return ''
  try {
    const parsed = new URL(raw, 'http://localhost')
    let pathname = parsed.pathname.replace(/\\/g, '/')
    const apiIdx = pathname.indexOf(EXPORT_API_MARKER)
    if (apiIdx >= 0) return `${pathname.slice(apiIdx)}${parsed.search}`
    const exportsIdx = pathname.indexOf('/exports/')
    if (exportsIdx >= 0) return `/api/v1/mirai${pathname.slice(exportsIdx)}${parsed.search}`
  } catch {
    /* fall through */
  }
  const normalized = raw.replace(/\\/g, '/')
  const idx = normalized.indexOf(EXPORT_API_MARKER)
  if (idx >= 0) return normalized.slice(idx)
  if (normalized.startsWith('/exports/')) return `/api/v1/mirai${normalized}`
  return normalized.startsWith('/') ? normalized : `/${normalized}`
}

function exportRequestUrl(url) {
  const apiPath = exportApiPath(url)
  if (apiPath.startsWith('/api/v1/')) return apiPath.slice('/api/v1/'.length)
  if (apiPath.startsWith('api/v1/')) return apiPath.slice('api/v1/'.length)
  if (apiPath.startsWith('mirai/exports/')) return apiPath
  if (apiPath.startsWith('/mirai/exports/')) return apiPath.slice(1)
  return apiPath.replace(/^\//, '')
}

const cases = [
  [
    'http://192.168.1.8:5273/api/v1/mirai/exports/1/2026/09/file.md',
    '/api/v1/mirai/exports/1/2026/09/file.md',
    'mirai/exports/1/2026/09/file.md',
  ],
  [
    '/api/v1/mirai/exports/2/a/b/note.txt',
    '/api/v1/mirai/exports/2/a/b/note.txt',
    'mirai/exports/2/a/b/note.txt',
  ],
  [
    'https://app.example/api/v1/mirai/exports/9/x.pdf?x=1',
    '/api/v1/mirai/exports/9/x.pdf?x=1',
    'mirai/exports/9/x.pdf?x=1',
  ],
]

let failed = 0
for (const [input, apiPath, reqUrl] of cases) {
  const gotApi = exportApiPath(input)
  const gotReq = exportRequestUrl(input)
  const ok = gotApi === apiPath && gotReq === reqUrl
  console.log(ok ? 'OK ' : 'FAIL', input)
  if (!ok) {
    console.log('  expected api:', apiPath, 'got', gotApi)
    console.log('  expected req:', reqUrl, 'got', gotReq)
    failed++
  }
}

if (failed) {
  console.error(`\n${failed} case(s) failed`)
  process.exit(1)
}
console.log('\nAll export path rewrite cases passed')
