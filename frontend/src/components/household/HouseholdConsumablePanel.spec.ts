import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HouseholdConsumablePanel from './HouseholdConsumablePanel.vue'
import type { HouseholdConsumable } from '@/types/household'

function consumable(overrides: Partial<HouseholdConsumable> = {}): HouseholdConsumable {
  return {
    id: 1,
    householdId: 1,
    name: 'PP棉',
    specModel: '10寸',
    currentStock: 0,
    restockThreshold: 1,
    isLowStock: true,
    lowStockReminderSent: true,
    unit: '支',
    purchaseLink: 'https://shop.example/pp',
    note: null,
    linkedItems: [
      { id: 9, name: '净水器', isArchived: false },
      { id: 10, name: '旧滤芯', isArchived: true },
    ],
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    ...overrides,
  }
}

const harness = vi.hoisted(() => ({
  isAdmin: true,
}))

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: {
      get isAdmin() {
        return harness.isAdmin
      },
      consumables: [
        consumable(),
        consumable({
          id: 2,
          name: '纱窗',
          currentStock: 4,
          isLowStock: false,
          purchaseLink: 'https://shop.example/screen',
          linkedItems: [],
        }),
      ],
    },
  }),
}))

describe('HouseholdConsumablePanel', () => {
  it('低库存高亮、展示购买链接和关联事项', () => {
    harness.isAdmin = true
    const wrapper = mount(HouseholdConsumablePanel)
    const low = wrapper.get('[data-low-stock="true"]')
    expect(low.classes()).toContain('bg-[#fff6f4]')
    expect(low.text()).toContain('需补货')
    expect(low.text()).toContain('净水器')
    expect(low.text()).toContain('旧滤芯（已归档）')
    const link = low.get('a')
    expect(link.attributes('href')).toBe('https://shop.example/pp')
    expect(link.attributes('rel')).toBe('noopener noreferrer')
    expect(wrapper.get('[data-low-stock="false"]').text()).toContain('纱窗')
    expect(wrapper.get('[data-low-stock="false"]').text()).toContain('https://shop.example/screen')
  })

  it('成员看不到删除，管理员看得到', () => {
    harness.isAdmin = false
    const memberView = mount(HouseholdConsumablePanel)
    expect(memberView.text()).not.toContain('删除')
    expect(memberView.text()).toContain('编辑')
    expect(memberView.text()).toContain('补货')

    harness.isAdmin = true
    const adminView = mount(HouseholdConsumablePanel)
    expect(adminView.text()).toContain('删除')
  })
})
