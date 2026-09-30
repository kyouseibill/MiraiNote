import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/household', () => ({
  householdApi: {
    serverToday: vi.fn(),
    getMine: vi.fn(),
    listIncomingInvitations: vi.fn(),
    listOutgoingInvitations: vi.fn(),
    listMembers: vi.fn(),
    listItems: vi.fn(),
    templates: vi.fn(),
    listConsumables: vi.fn(),
  },
}))

vi.mock('@/api/lifeLog', () => ({
  lifeLogApi: {},
}))

const { householdApi } = await import('@/api/household')
const { useHouseholdStore } = await import('@/stores/household')

describe('待处理邀请时不加载家庭数据', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(householdApi.serverToday).mockResolvedValue({ today: '2026-10-08' })
    vi.mocked(householdApi.getMine).mockResolvedValue({
      id: 0,
      name: '',
      myMemberId: 0,
      myRole: 'Member',
      hasHousehold: false,
      hasPendingInvitations: true,
    })
    vi.mocked(householdApi.listIncomingInvitations).mockResolvedValue([{
      id: 9,
      householdId: 4,
      householdName: '南边的家',
      inviteeUserId: 11,
      inviteeUsername: '阿宁',
      inviteeEmail: null,
      inviterUsername: '周周',
      role: 'Member',
      status: 'Pending',
      expiresAt: '2026-10-15T01:00:00.000Z',
      isExpired: false,
    }])
    vi.mocked(householdApi.listOutgoingInvitations).mockReset()
    vi.mocked(householdApi.listOutgoingInvitations).mockResolvedValue([])
    vi.mocked(householdApi.listMembers).mockReset()
    vi.mocked(householdApi.listItems).mockReset()
    vi.mocked(householdApi.listConsumables).mockReset()
  })

  it('先拉邀请，不请求事项、成员和耗材', async () => {
    const store = useHouseholdStore()
    await store.loadWorkspace(false)

    expect(store.awaitingInvitation).toBe(true)
    expect(store.incomingInvitations).toHaveLength(1)
    expect(householdApi.listIncomingInvitations).toHaveBeenCalledTimes(1)
    expect(householdApi.listOutgoingInvitations).not.toHaveBeenCalled()
    expect(householdApi.listItems).not.toHaveBeenCalled()
    expect(householdApi.listMembers).not.toHaveBeenCalled()
    expect(householdApi.listConsumables).not.toHaveBeenCalled()
  })

  it('已经有空家庭时，待处理邀请仍然挡住事项列表', async () => {
    vi.mocked(householdApi.getMine).mockResolvedValue({
      id: 3,
      name: '我的家庭',
      myMemberId: 8,
      myRole: 'Admin',
      hasHousehold: true,
      hasPendingInvitations: true,
    })
    const store = useHouseholdStore()
    await store.loadWorkspace(false)

    expect(store.awaitingInvitation).toBe(true)
    expect(store.incomingInvitations[0]?.householdName).toBe('南边的家')
    expect(householdApi.listItems).not.toHaveBeenCalled()
    expect(householdApi.listMembers).not.toHaveBeenCalled()
    expect(householdApi.listConsumables).not.toHaveBeenCalled()
  })
})
