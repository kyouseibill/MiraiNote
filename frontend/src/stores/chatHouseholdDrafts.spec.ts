import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { visibleRestoredDrafts, type HouseholdChatDraft } from '@/utils/householdChat'

vi.mock('@/api/chat', () => ({
  chatApi: {
    getSession: vi.fn(),
    createSession: vi.fn(),
  },
}))

vi.mock('@/api/household', () => ({
  householdApi: {
    listChatDrafts: vi.fn(),
  },
}))

vi.mock('@/api/agent', () => ({
  agentApi: {},
}))

const { chatApi } = await import('@/api/chat')
const { householdApi } = await import('@/api/household')
const { useChatStore } = await import('@/stores/chat')

function session(id: number) {
  const now = '2026-10-08T01:00:00Z'
  return {
    id,
    title: id === 9 ? '新对话' : '旧对话',
    isArchived: false,
    isPinned: false,
    projectId: null,
    messages: [{ id: 1, role: 'assistant' as const, content: '好', createdAt: now }],
    createdAt: now,
    updatedAt: now,
  }
}

function pendingDraft() {
  return {
    kind: 'confirm',
    message: '请确认后再写入。',
    draftId: 4,
    expiresAt: '2099-10-08T01:10:00Z',
    completedOn: '2026-10-08',
    cost: null,
    deductConsumable: false,
    item: {
      id: 3,
      name: '护照',
      location: null,
      isPaused: false,
      nextDueDate: null,
      consumableId: null,
      consumableName: null,
      consumableStock: null,
    },
    candidates: [],
    suggestedName: null,
  }
}

describe('家务草稿不串到新对话', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(chatApi.getSession).mockReset()
    vi.mocked(chatApi.createSession).mockReset()
    vi.mocked(householdApi.listChatDrafts).mockReset()
  })

  it('新对话会清掉上一会话的草稿，迟到的列表也不会写回来', async () => {
    let release: (rows: unknown[]) => void = () => {}
    vi.mocked(householdApi.listChatDrafts).mockReturnValue(new Promise((resolve) => {
      release = resolve
    }))
    vi.mocked(chatApi.getSession).mockResolvedValue(session(8))
    vi.mocked(chatApi.createSession).mockResolvedValue(session(9))

    const store = useChatStore()
    await store.openSession(8)
    await store.createSession()
    release([pendingDraft()])
    await Promise.resolve()
    await Promise.resolve()

    expect(store.currentSession?.id).toBe(9)
    expect(store.sessionHouseholdDrafts).toEqual([])
  })

  it('只渲染属于当前会话的草稿', () => {
    const draft = { ...pendingDraft(), sessionId: 8 } as HouseholdChatDraft
    expect(visibleRestoredDrafts([draft], 8, new Set())).toEqual([draft])
    expect(visibleRestoredDrafts([draft], 9, new Set())).toEqual([])
    expect(visibleRestoredDrafts([draft], null, new Set())).toEqual([])
  })
})
