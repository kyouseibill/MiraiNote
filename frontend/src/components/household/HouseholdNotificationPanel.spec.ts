import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HouseholdNotificationPanel from './HouseholdNotificationPanel.vue'
import { barkReentryMessage, deliveryFailureText } from '@/utils/householdFormat'
import type { HouseholdNotificationSettings } from '@/types/household'

const harness = vi.hoisted(() => {
  const store = {
    previewMode: false,
    notificationSettings: null as {
      barkEnabled: boolean
      barkConfigured: boolean
      barkAddressSuffix: string | null
      barkAddressUnreadable: boolean
      emailEnabled: boolean
      email: string | null
      pushHour: number
      pushMinute: number
      leadChannel: 'Email' | 'Bark'
      dueChannel: 'Email' | 'Bark'
      overdueIntervalDays: number
      notificationsEnabled: boolean
      hasDeliverableChannel: boolean
      barkFailure: { failedAt: string; reason: string } | null
      emailFailure: { failedAt: string; reason: string } | null
    } | null,
    fetchNotificationSettings: vi.fn(),
  }
  store.fetchNotificationSettings.mockImplementation(async () => store.notificationSettings)
  return { store }
})

function useSettings(overrides: Partial<HouseholdNotificationSettings> = {}) {
  harness.store.notificationSettings = {
    barkEnabled: true,
    barkConfigured: true,
    barkAddressSuffix: '9f3a',
    barkAddressUnreadable: false,
    emailEnabled: true,
    email: 'tester@example.com',
    pushHour: 9,
    pushMinute: 0,
    leadChannel: 'Email',
    dueChannel: 'Bark',
    overdueIntervalDays: 3,
    notificationsEnabled: false,
    hasDeliverableChannel: true,
    barkFailure: { failedAt: '2026-10-08T01:05:00.000Z', reason: '发送失败' },
    emailFailure: null,
    ...overrides,
  }
}

vi.mock('@/composables/useHouseholdFeedback', () => ({
  useHouseholdFeedback: () => ({
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
    report: vi.fn(async () => ({ message: '失败', status: 400 })),
    store: harness.store,
  }),
}))

describe('HouseholdNotificationPanel', () => {
  it('显示最近一次失败，成功后的通道不再警告', async () => {
    useSettings()
    const wrapper = mount(HouseholdNotificationPanel)
    await flushPromises()

    expect(wrapper.get('[data-testid="bark-delivery-failure"]').text()).toBe(
      deliveryFailureText({ failedAt: '2026-10-08T01:05:00.000Z', reason: '发送失败' }),
    )
    expect(wrapper.text()).toContain('请点发送测试检查')
    expect(wrapper.text()).not.toContain('Exception')
    expect(wrapper.find('[data-testid="email-delivery-failure"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('已配置')
  })

  it('Bark 地址无法解密时不再显示已配置，并要求重新填写', async () => {
    useSettings({
      barkConfigured: false,
      barkAddressSuffix: null,
      barkAddressUnreadable: true,
      barkFailure: null,
    })
    const wrapper = mount(HouseholdNotificationPanel)
    await flushPromises()

    expect(wrapper.get('[data-testid="bark-reentry"]').text()).toBe(barkReentryMessage)
    expect(wrapper.text()).not.toContain('已配置')
    expect(wrapper.find('[data-testid="bark-delivery-failure"]').exists()).toBe(false)
  })

  it('不渲染分类以外的失败原因', async () => {
    useSettings({
      barkFailure: { failedAt: '2026-10-08T01:05:00.000Z', reason: 'https://api.day.app/secret-key-should-not-render' },
      emailFailure: { failedAt: '2026-10-08T01:07:00.000Z', reason: 'other-inbox@leak.test' },
    })
    const wrapper = mount(HouseholdNotificationPanel)
    await flushPromises()

    expect(wrapper.find('[data-testid="bark-delivery-failure"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="email-delivery-failure"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('secret-key-should-not-render')
    expect(wrapper.text()).not.toContain('leak.test')
    expect(wrapper.text()).not.toContain('TimeoutException')
  })

  it('两个通道都不可用时在顶部提示提醒不会发出', async () => {
    useSettings({ hasDeliverableChannel: false })
    const wrapper = mount(HouseholdNotificationPanel)
    await flushPromises()

    const banner = wrapper.get('[data-testid="no-deliverable-channel"]')
    expect(banner.text()).toBe('当前没有可送达的通道，提醒不会发出')
    expect(wrapper.element.firstElementChild).toBe(banner.element)
  })

  it('至少有一个通道可送达时不显示该提示', async () => {
    useSettings({ hasDeliverableChannel: true })
    const wrapper = mount(HouseholdNotificationPanel)
    await flushPromises()

    expect(wrapper.find('[data-testid="no-deliverable-channel"]').exists()).toBe(false)
  })
})
