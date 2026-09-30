import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HouseholdChatConfirmCard from './HouseholdChatConfirmCard.vue'
import { householdApi } from '@/api/household'
import type { HouseholdChatDraft } from '@/utils/householdChat'

vi.mock('@/api/household', () => ({
  householdApi: {
    confirmChatDraft: vi.fn(),
  },
}))

function draft(overrides: Partial<HouseholdChatDraft> = {}): HouseholdChatDraft {
  return {
    kind: 'confirm',
    message: '请确认后再写入。',
    draftId: 4,
    expiresAt: '2099-10-08T01:10:00Z',
    completedOn: '2026-10-08',
    cost: 12,
    deductConsumable: true,
    item: {
      id: 3,
      name: '厨房净水器 PP 棉',
      location: '厨房',
      isPaused: false,
      nextDueDate: '2026-11-08',
      consumableId: 9,
      consumableName: 'PP 棉',
      consumableStock: 3,
    },
    candidates: [{
      id: 3,
      name: '厨房净水器 PP 棉',
      location: '厨房',
      isPaused: false,
      nextDueDate: '2026-11-08',
      consumableId: 9,
      consumableName: 'PP 棉',
      consumableStock: 3,
    }],
    suggestedName: null,
    ...overrides,
  }
}

function mountCard(value: HouseholdChatDraft) {
  return mount(HouseholdChatConfirmCard, {
    props: { draft: value },
    global: {
      stubs: {
        RouterLink: {
          props: ['to'],
          template: '<a :href="to"><slot /></a>',
        },
      },
    },
  })
}

describe('HouseholdChatConfirmCard', () => {
  it('唯一匹配展示事项、日期、费用，取消扣减后按未勾选提交', async () => {
    vi.mocked(householdApi.confirmChatDraft).mockReset()
    vi.mocked(householdApi.confirmChatDraft).mockResolvedValue({} as never)
    const wrapper = mountCard(draft())

    expect(wrapper.get('[data-testid="household-chat-item"]').text()).toContain('厨房净水器 PP 棉')
    expect(wrapper.get('[data-testid="household-chat-date"]').text()).toBe('2026-10-08')
    expect(wrapper.get('[data-testid="household-chat-cost"]').text()).toBe('¥12.00')
    expect(wrapper.get('[data-testid="household-chat-expiry"]').text()).toContain('前确认')
    expect(wrapper.text()).toContain('扣减耗材')
    const checkbox = wrapper.get('[data-testid="household-chat-deduct"]')
    expect((checkbox.element as HTMLInputElement).checked).toBe(true)

    await checkbox.setValue(false)
    await wrapper.get('[data-testid="household-chat-confirm"]').trigger('click')
    await flushPromises()

    expect(householdApi.confirmChatDraft).toHaveBeenCalledTimes(1)
    expect(householdApi.confirmChatDraft).toHaveBeenCalledWith(
      expect.objectContaining({ draftId: 4, itemId: 3, deductConsumable: false, cost: 12 }),
      expect.any(String),
    )
  })

  it('多项时先选中候选再确认，提交选中的事项', async () => {
    vi.mocked(householdApi.confirmChatDraft).mockReset()
    vi.mocked(householdApi.confirmChatDraft).mockResolvedValue({} as never)
    const wrapper = mountCard(draft({
      kind: 'choose',
      message: '匹配到多项，请选择一项再确认。',
      item: null,
      deductConsumable: false,
      cost: null,
      candidates: [
        {
          id: 3,
          name: '厨房净水器 PP 棉',
          location: '厨房',
          isPaused: false,
          nextDueDate: null,
          consumableId: null,
          consumableName: null,
          consumableStock: null,
        },
        {
          id: 8,
          name: '客厅净水器 PP 棉',
          location: '客厅',
          isPaused: false,
          nextDueDate: null,
          consumableId: null,
          consumableName: null,
          consumableStock: null,
        },
      ],
    }))

    await wrapper.get('[data-candidate-id="8"]').trigger('click')
    await wrapper.get('[data-testid="household-chat-confirm"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="household-chat-item"]').text()).toContain('客厅净水器 PP 棉')
    expect(householdApi.confirmChatDraft).toHaveBeenCalledWith(
      expect.objectContaining({ itemId: 8, deductConsumable: null }),
      expect.any(String),
    )
  })

  it('过期后按钮不可用，重复点击也不提交', async () => {
    vi.mocked(householdApi.confirmChatDraft).mockReset()
    const wrapper = mountCard(draft({ expiresAt: '2000-01-01T00:00:00Z' }))
    const button = wrapper.get('[data-testid="household-chat-confirm"]')
    expect(button.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="household-chat-expiry"]').text()).toContain('确认已过期')
    await button.trigger('click')
    await button.trigger('click')
    expect(householdApi.confirmChatDraft).not.toHaveBeenCalled()
  })

  it('没有匹配时给出预填名称的新建入口', () => {
    const wrapper = mountCard(draft({
      kind: 'create',
      message: '没有匹配到事项。可以新建一个，名称已经预填。',
      draftId: null,
      item: null,
      candidates: [],
      suggestedName: '阳台纱窗',
      deductConsumable: false,
    }))
    expect(wrapper.get('[data-testid="household-chat-create"]').attributes('href')).toBe(
      '/household?prefill=%E9%98%B3%E5%8F%B0%E7%BA%B1%E7%AA%97',
    )
  })
})
