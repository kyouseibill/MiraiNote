import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import HouseholdView from './HouseholdView.vue'
import type { HouseholdItem } from '@/types/household'

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

const harness = vi.hoisted(() => ({
  isAdmin: false,
}))

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: {
      get isAdmin() {
        return harness.isAdmin
      },
      calendarToday: '2026-10-08',
      household: { id: 1, name: '林家', myMemberId: 2, myRole: harness.isAdmin ? 'Admin' : 'Member' },
      items: [item],
      loadWorkspace: vi.fn().mockResolvedValue(undefined),
      fetchItems: vi.fn().mockResolvedValue([item]),
      consumableName: (id: number | null) => (id === 8 ? 'PP棉' : ''),
    },
  }),
}))

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  })
  const wrapper = mount(HouseholdView, {
    global: {
      plugins: [router],
      stubs: {
        HouseholdConsumablePanel: true,
        HouseholdMemberPanel: true,
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
})
