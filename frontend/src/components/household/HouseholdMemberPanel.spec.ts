import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import HouseholdMemberPanel from './HouseholdMemberPanel.vue'
import type { HouseholdInvitation, HouseholdMember } from '@/types/household'

const acceptKey = '11111111-1111-4111-8111-111111111111'

vi.mock('@/utils/idempotencyKey', () => ({
  createIdempotencyKey: () => acceptKey,
}))

const harness = vi.hoisted(() => {
  const members: HouseholdMember[] = [
    { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
    { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Member' },
  ]
  const outgoing: HouseholdInvitation[] = [{
    id: 7,
    householdId: 1,
    householdName: '林家',
    inviteeUserId: 12,
    inviteeUsername: '待加入',
    inviteeEmail: 'pending@example.com',
    inviterUsername: '林夏',
    role: 'Member',
    status: 'Pending',
    expiresAt: '2026-10-15T01:00:00.000Z',
    isExpired: false,
  }]
  const incoming: HouseholdInvitation[] = [{
    id: 9,
    householdId: 4,
    householdName: '南边的家',
    inviteeUserId: 10,
    inviteeUsername: '林夏',
    inviteeEmail: null,
    inviterUsername: '周周',
    role: 'Member',
    status: 'Pending',
    expiresAt: '2026-10-15T01:00:00.000Z',
    isExpired: false,
  }]
  return {
    store: {
      household: { id: 1, name: '林家', myMemberId: 1, myRole: 'Admin' as const },
      members,
      isAdmin: true,
      outgoingInvitations: outgoing,
      incomingInvitations: incoming,
      fetchInvitations: vi.fn().mockResolvedValue(undefined),
      createInvitation: vi.fn().mockResolvedValue(undefined),
      revokeInvitation: vi.fn().mockResolvedValue(undefined),
      acceptInvitation: vi.fn().mockResolvedValue(undefined),
      rejectInvitation: vi.fn().mockResolvedValue(undefined),
      removeMember: vi.fn().mockResolvedValue(undefined),
      changeMemberRole: vi.fn().mockResolvedValue(undefined),
      leaveHousehold: vi.fn().mockResolvedValue(undefined),
    },
  }
})

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: harness.store,
  }),
}))

beforeEach(() => {
  harness.store.isAdmin = true
  harness.store.household.myRole = 'Admin'
  harness.store.household.myMemberId = 1
  harness.store.members = [
    { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
    { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Member' },
  ]
  harness.store.outgoingInvitations = [{
    id: 7,
    householdId: 1,
    householdName: '林家',
    inviteeUserId: 12,
    inviteeUsername: '待加入',
    inviteeEmail: 'pending@example.com',
    inviterUsername: '林夏',
    role: 'Member',
    status: 'Pending',
    expiresAt: '2026-10-15T01:00:00.000Z',
    isExpired: false,
  }]
  harness.store.incomingInvitations = [{
    id: 9,
    householdId: 4,
    householdName: '南边的家',
    inviteeUserId: 10,
    inviteeUsername: '林夏',
    inviteeEmail: null,
    inviterUsername: '周周',
    role: 'Member',
    status: 'Pending',
    expiresAt: '2026-10-15T01:00:00.000Z',
    isExpired: false,
  }]
})

function mountPanel() {
  return mount(HouseholdMemberPanel, {
    global: {
      stubs: {
        AppDialog: {
          props: ['open'],
          template: '<div v-if="open"><slot /><slot name="footer" /></div>',
        },
      },
    },
  })
}

describe('HouseholdMemberPanel', () => {
  it('管理员能看到邮箱、邀请、撤回和移除他人', async () => {
    harness.store.isAdmin = true
    harness.store.household.myRole = 'Admin'
    harness.store.household.myMemberId = 1
    harness.store.members = [
      { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
      { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Member' },
    ]
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-testid="invite-form"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="invite-form"] select').exists()).toBe(false)
    expect(wrapper.find('[data-testid="member-email"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('lin@example.com')
    expect(wrapper.find('[data-testid="revoke-invitation"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-testid="remove-member"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('阿宁')
  })

  it('成员看不到邮箱、邀请和移除', async () => {
    harness.store.isAdmin = false
    harness.store.household.myRole = 'Member'
    harness.store.household.myMemberId = 2
    harness.store.outgoingInvitations = []
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-testid="invite-form"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="member-email"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('ning@example.com')
    expect(wrapper.find('[data-testid="remove-member"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="change-role"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="leave-household"]').attributes('disabled')).toBeUndefined()
  })

  it('唯一管理员点退出时提示先指定另一位管理员', async () => {
    harness.store.isAdmin = true
    harness.store.household.myRole = 'Admin'
    harness.store.household.myMemberId = 1
    harness.store.members = [
      { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
    ]
    const wrapper = mountPanel()
    await flushPromises()

    harness.store.leaveHousehold.mockClear()
    expect(wrapper.get('[data-testid="leave-household"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[data-testid="leave-household"]').trigger('click')
    expect(wrapper.get('[data-testid="sole-admin-leave-prompt"]').text()).toContain('先指定另一位管理员')
    expect(harness.store.leaveHousehold).not.toHaveBeenCalled()
  })

  it('管理员可以把其他成员设为管理员或取消管理员', async () => {
    harness.store.changeMemberRole.mockClear()
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-testid="change-role"]').text()).toBe('设为管理员')
    await wrapper.get('[data-testid="change-role"]').trigger('click')
    await flushPromises()
    expect(harness.store.changeMemberRole).toHaveBeenCalledWith(2, 'Admin')

    harness.store.members = [
      { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
      { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Admin' },
    ]
    const demote = mountPanel()
    await flushPromises()
    expect(demote.get('[data-testid="change-role"]').text()).toBe('取消管理员')
    expect(demote.get('[data-testid="change-role"]').attributes('disabled')).toBeUndefined()
    await demote.get('[data-testid="change-role"]').trigger('click')
    await flushPromises()
    expect(harness.store.changeMemberRole).toHaveBeenCalledWith(2, 'Member')
  })

  it('最后一位管理员的取消按钮禁用并带提示', async () => {
    harness.store.household.myMemberId = 1
    harness.store.members = [
      { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Member' },
      { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Admin' },
    ]
    harness.store.changeMemberRole.mockClear()
    const wrapper = mountPanel()
    await flushPromises()

    const button = wrapper.get('[data-testid="change-role"]')
    expect(button.text()).toBe('取消管理员')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('title')).toContain('至少需要一名管理员')
    await button.trigger('click')
    await flushPromises()
    expect(harness.store.changeMemberRole).not.toHaveBeenCalled()
  })

  it('接受邀请使用同一条幂等键', async () => {
    harness.store.isAdmin = false
    harness.store.incomingInvitations = [{
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
    }]
    harness.store.acceptInvitation.mockClear()
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('周周 邀请你加入『南边的家』，接受后加入这个家庭。')
    expect(wrapper.get('[data-testid="accept-invitation"]').text()).toBe('接受')
    expect(wrapper.get('[data-testid="reject-invitation"]').text()).toBe('拒绝')
    expect(wrapper.text()).not.toContain('@')

    await wrapper.get('[data-testid="accept-invitation"]').trigger('click')
    await wrapper.get('[data-testid="accept-invitation"]').trigger('click')
    await flushPromises()

    expect(harness.store.acceptInvitation).toHaveBeenCalledWith(9, acceptKey)
    expect(harness.store.acceptInvitation.mock.calls.every((call) => call[1] === acceptKey)).toBe(true)
  })

  it('移除成员需要再次确认', async () => {
    harness.store.isAdmin = true
    harness.store.household.myMemberId = 1
    harness.store.members = [
      { id: 1, userId: 10, username: '林夏', email: 'lin@example.com', role: 'Admin' },
      { id: 2, userId: 11, username: '阿宁', email: 'ning@example.com', role: 'Admin' },
    ]
    harness.store.removeMember.mockClear()
    const wrapper = mountPanel()
    await flushPromises()

    await wrapper.get('[data-testid="remove-member"]').trigger('click')
    expect(harness.store.removeMember).not.toHaveBeenCalled()
    await wrapper.get('[data-testid="confirm-remove-member"]').trigger('click')
    await flushPromises()
    expect(harness.store.removeMember).toHaveBeenCalledWith(2)
    expect(wrapper.get('[data-testid="leave-household"]').attributes('disabled')).toBeUndefined()
  })
})
