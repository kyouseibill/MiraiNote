import axios from 'axios'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import HouseholdView from './HouseholdView.vue'
import { pendingInvitationConflictMessage } from '@/utils/apiError'
import type { HouseholdInvitation, HouseholdItem } from '@/types/household'

const item: HouseholdItem = {
  id: 3,
  householdId: 1,
  name: '滤网',
  category: 'HomeMaintenance',
  location: null,
  modelSpec: null,
  itemType: 'Recurring',
  cycleValue: 3,
  cycleUnit: 'Month',
  lastDoneDate: '2026-07-01',
  nextDueDate: '2026-10-01',
  expiryDate: null,
  leadDays: 7,
  assigneeMemberId: null,
  assigneeName: null,
  consumableId: 8,
  note: null,
  purchaseLink: null,
  isPaused: false,
  isArchived: false,
  mileageCycleKm: null,
  aliases: [],
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
}

const incomingInvite: HouseholdInvitation = {
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
}

const harness = vi.hoisted(() => ({
  isAdmin: false,
  awaitingInvitation: false,
  incomingInvitations: [] as HouseholdInvitation[],
  report: vi.fn(async () => ({ message: '失败', status: 400 })),
  loadWorkspace: vi.fn(),
  fetchItems: vi.fn(),
  showInvitationGate: vi.fn(),
}))

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
    report: harness.report,
    store: {
      get isAdmin() {
        return harness.isAdmin
      },
      get awaitingInvitation() {
        return harness.awaitingInvitation
      },
      get incomingInvitations() {
        return harness.incomingInvitations
      },
      calendarToday: '2026-10-08',
      household: { id: 1, name: '林家', myMemberId: 2, myRole: harness.isAdmin ? 'Admin' : 'Member' },
      items: [item],
      members: [],
      outgoingInvitations: [],
      loadWorkspace: harness.loadWorkspace,
      fetchItems: harness.fetchItems,
      fetchInvitations: vi.fn().mockResolvedValue(undefined),
      showInvitationGate: harness.showInvitationGate,
      acceptInvitation: vi.fn(),
      rejectInvitation: vi.fn(),
      createInvitation: vi.fn(),
      revokeInvitation: vi.fn(),
      removeMember: vi.fn(),
      leaveHousehold: vi.fn(),
      consumableName: (id: number | null) => (id === 8 ? 'PP棉' : ''),
    },
  }),
}))

function pendingInvitationError() {
  return new axios.AxiosError(
    pendingInvitationConflictMessage,
    'ERR_BAD_REQUEST',
    undefined,
    undefined,
    {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: new axios.AxiosHeaders() },
      data: { message: pendingInvitationConflictMessage },
    },
  )
}

beforeEach(() => {
  harness.isAdmin = false
  harness.awaitingInvitation = false
  harness.incomingInvitations = []
  harness.report.mockClear()
  harness.loadWorkspace.mockReset()
  harness.loadWorkspace.mockResolvedValue(undefined)
  harness.fetchItems.mockReset()
  harness.fetchItems.mockResolvedValue([item])
  harness.showInvitationGate.mockReset()
})

async function mountView(stubMembers = true) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  })
  const wrapper = mount(HouseholdView, {
    global: {
      plugins: [router],
      stubs: {
        HouseholdConsumablePanel: true,
        HouseholdMemberPanel: stubMembers,
        HouseholdNotificationPanel: true,
        HouseholdItemFormDialog: true,
        HouseholdCompleteDialog: true,
        HouseholdRestoreDialog: true,
        AppDialog: true,
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('HouseholdView', () => {
  it('事项行显示关联耗材，成员看不到编辑、暂停和删除', async () => {
    harness.isAdmin = false
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('PP棉')
    const labels = wrapper.findAll('button').map((button) => button.text())
    expect(labels).not.toContain('编辑')
    expect(labels).not.toContain('暂停')
    expect(labels).not.toContain('删除')
  })

  it('管理员能看到编辑、暂停和删除', async () => {
    harness.isAdmin = true
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('编辑')
    expect(wrapper.text()).toContain('暂停')
    expect(wrapper.text()).toContain('删除')
  })

  it('家庭页收到 409 时进入邀请页，不展示请先处理家庭邀请', async () => {
    harness.fetchItems.mockRejectedValueOnce(pendingInvitationError())
    harness.showInvitationGate.mockImplementation(async () => {
      harness.awaitingInvitation = true
      harness.incomingInvitations = [incomingInvite]
    })

    const wrapper = await mountView(false)

    expect(wrapper.get('[data-testid="invitation-gate"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('周周 邀请你加入『南边的家』，接受后加入这个家庭。')
    expect(wrapper.text()).not.toContain('@')
    expect(wrapper.get('[data-testid="accept-invitation"]').text()).toBe('接受')
    expect(wrapper.get('[data-testid="reject-invitation"]').text()).toBe('拒绝')
    expect(wrapper.text()).not.toContain(pendingInvitationConflictMessage)
    expect(wrapper.text()).not.toContain('空家庭')
    expect(harness.report).not.toHaveBeenCalled()
    expect(harness.showInvitationGate).toHaveBeenCalledTimes(1)
  })
})
