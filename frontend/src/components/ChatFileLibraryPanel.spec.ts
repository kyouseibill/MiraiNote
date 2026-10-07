import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChatFileLibraryPanel from './ChatFileLibraryPanel.vue'

vi.mock('@/api/workspace', () => ({
  workspaceApi: {
    library: vi.fn(),
    attach: vi.fn(),
    download: vi.fn(),
  },
}))

const { workspaceApi } = await import('@/api/workspace')

describe('聊天文件库', () => {
  beforeEach(() => {
    vi.mocked(workspaceApi.library).mockReset()
    vi.mocked(workspaceApi.attach).mockReset()
  })

  it('按上传、工作生成和导出成品分组，并说明已整理的散落文件', async () => {
    vi.mocked(workspaceApi.library).mockResolvedValue({
      organizedCount: 2,
      organizedPaths: ['generated/archive/report.md', 'generated/archive/charts'],
      uploads: [{
        kind: 'upload',
        name: '年报.pdf',
        relativePath: 'uploads/2026/10/20261007120000000_年报.pdf',
        sizeBytes: 2048,
        extension: '.pdf',
        modifiedAt: '2026-10-07T04:00:00Z',
        downloadUrl: null,
      }],
      generated: [{
        kind: 'generated',
        name: 'report.md',
        relativePath: 'generated/archive/report.md',
        sizeBytes: 12,
        extension: '.md',
        modifiedAt: '2026-09-30T04:00:00Z',
        downloadUrl: null,
      }],
      exports: [{
        kind: 'export',
        name: '周报.docx',
        relativePath: '4/2026/10/20261007010101000_周报.docx',
        sizeBytes: 4096,
        extension: '.docx',
        modifiedAt: '2026-10-07T01:01:01Z',
        downloadUrl: '/api/v1/mirai/exports/4/2026/10/file.docx',
      }],
      uploadsTruncated: false,
      generatedTruncated: false,
      exportsTruncated: false,
    })

    const wrapper = mount(ChatFileLibraryPanel)
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('已把 2 个散落在工作区根目录的文件归到「工作生成 / archive」')
    expect(text).toContain('上传的文件')
    expect(text).toContain('年报.pdf')
    expect(text).toContain('工作生成')
    expect(text).toContain('report.md')
    expect(text).toContain('generated/archive/report.md')
    expect(text).toContain('导出成品')
    expect(text).toContain('周报.docx')
    expect(wrapper.findAll('button[aria-label^="下载"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('附加')
  })
})
