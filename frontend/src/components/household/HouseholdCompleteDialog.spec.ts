import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HouseholdCompleteDialog from './HouseholdCompleteDialog.vue'
import { duplicateCompletionMessage } from '@/utils/householdFormat'
import type { HouseholdItem } from '@/types/household'

const completeItem = vi.fn()
const fetchServerToday = vi.fn()
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast,
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: {
      previewMode: false,
      calendarToday: '2026-10-01',
      fetchServerToday,
      completeItem,
      fetchItem: vi.fn(),
      fetchMembers: vi.fn(),
      fetchConsumables: vi.fn(),
      members: [{ id: 1, userId: 1, username: '林夏', email: null, role: 'Admin' }],
      consumables: [],
      household: { id: 1, name: '我的家庭', myMemberId: 1, myRole: 'Admin' },
      items: [{
        id: 3,
        name: '滤网',
        itemType: 'Recurring',
        lastDoneDate: '2026-09-01',
        nextDueDate: '2026-10-01',
        consumableId: null,
        isArchived: false,
        leadDays: 7,
      } as HouseholdItem],
    },
  }),
}))

describe('HouseholdCompleteDialog', () => {
  it('3 秒内换新 key 重复完成时提示刚刚已提交过', async () => {
    completeItem.mockReset()
    fetchServerToday.mockReset()
    toast.info.mockReset()
    fetchServerToday.mockResolvedValue('2026-10-01')
    const duplicate = new Error('请勿重复提交') as Error & { status: number }
    duplicate.status = 409
    completeItem.mockRejectedValue(duplicate)

    const wrapper = mount(HouseholdCompleteDialog, {
      props: { open: false, itemId: 3 },
      global: {
        stubs: {
          AppDialog: {
            props: ['open'],
            template: '<div v-if="open"><slot /><slot name="footer" /></div>',
          },
          HouseholdPhotoField: { template: '<div />' },
        },
      },
    })

    await wrapper.setProps({ open: true })
    await flushPromises()
    await wrapper.get('#household-complete-submit').trigger('click')
    await flushPromises()

    expect(toast.info).toHaveBeenCalledWith(duplicateCompletionMessage)
  })
})
