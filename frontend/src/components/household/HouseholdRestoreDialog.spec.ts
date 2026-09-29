import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HouseholdRestoreDialog from './HouseholdRestoreDialog.vue'
import type { HouseholdItem } from '@/types/household'

const restoreItem = vi.fn()
const fetchServerToday = vi.fn()

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn() },
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: {
      previewMode: false,
      calendarToday: '2026-10-01',
      fetchServerToday,
      restoreItem,
    },
  }),
}))

const item = { id: 7, name: '护照' } as HouseholdItem

describe('HouseholdRestoreDialog', () => {
  it('填好到期日后提交，会带着该日期调用恢复接口', async () => {
    restoreItem.mockReset()
    fetchServerToday.mockReset()
    restoreItem.mockResolvedValue({ id: 7 })
    fetchServerToday.mockResolvedValue('2026-10-01')

    const wrapper = mount(HouseholdRestoreDialog, {
      props: { open: false, item },
      global: {
        stubs: {
          AppDialog: {
            props: ['open'],
            template: '<div v-if="open"><slot /><slot name="footer" /></div>',
          },
        },
      },
    })

    await wrapper.setProps({ open: true })
    await flushPromises()
    await wrapper.get('#restore-expiry').setValue('2026-10-08')
    const submit = wrapper.findAll('button').find((button) => button.text() === '恢复')
    expect(submit).toBeTruthy()
    await submit!.trigger('click')
    await flushPromises()

    expect(restoreItem).toHaveBeenCalledTimes(1)
    expect(restoreItem).toHaveBeenCalledWith(7, '2026-10-08')
  })
})
