import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/region', () => ({
  regionApi: {
    countries: vi.fn().mockResolvedValue([{ name: '中国', code: 'cn' }]),
    cities: vi.fn().mockResolvedValue([]),
  },
}))

vi.mock('@/api/auth', () => ({
  bindAuthHooks: vi.fn(),
  authApi: {
    getMemoReminderSettings: vi.fn().mockResolvedValue({ barkConfigured: false }),
    getWelcomeSettings: vi.fn().mockResolvedValue({ place: '', nickname: '' }),
    forgotPassword: vi.fn().mockResolvedValue(null),
    resetPassword: vi.fn().mockResolvedValue(null),
    logout: vi.fn(),
  },
}))

import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import { useAuthStore } from '@/stores/auth'
import ProfileView from '@/views/ProfileView.vue'
import ResetPasswordView from '@/views/ResetPasswordView.vue'

const Passthrough = defineComponent({ template: '<router-view />' })

const RESET_SENT = '重置邮件已发送，请到邮箱查收（也可能在垃圾箱）'

function signedInUser() {
  useAuthStore().setAuth({
    accessToken: 'token',
    accessTokenExpiresAt: '2099-01-01T00:00:00Z',
    user: {
      id: 7,
      username: 'boss',
      email: 'boss@miraiai.net',
      isAdmin: false,
      isEmailVerified: true,
      isActive: true,
      lastLoginAt: null,
      createdAt: '2026-10-01T00:00:00Z',
    },
  })
}

async function mountProfile() {
  const pinia = createPinia()
  setActivePinia(pinia)
  signedInUser()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Passthrough, children: [{ path: '', component: ProfileView }] },
      { path: '/login', name: 'login', component: { template: '<div>login</div>' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(Passthrough, { global: { plugins: [pinia, router] } })
  await flushPromises()
  return wrapper
}

async function mountReset(token = 'reset-token') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/reset-password', name: 'reset-password', component: ResetPasswordView },
      { path: '/login', name: 'login', component: { template: '<div>login</div>' } },
    ],
  })
  await router.push({ path: '/reset-password', query: token ? { token } : {} })
  await router.isReady()
  const wrapper = mount(ResetPasswordView, { global: { plugins: [pinia, router] } })
  await flushPromises()
  return { wrapper, router }
}

describe('登录密码只走邮箱重置', () => {
  beforeEach(() => {
    vi.mocked(authApi.forgotPassword).mockClear()
    vi.mocked(authApi.resetPassword).mockReset()
    vi.mocked(authApi.resetPassword).mockResolvedValue(null)
    useToast().list.splice(0, useToast().list.length)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('设置页不再显示旧密码表单，只展示掩码邮箱和发送按钮', async () => {
    const wrapper = await mountProfile()
    const section = wrapper.get('[data-testid="login-password-section"]')

    expect(section.text()).toContain('登录密码')
    expect(section.get('[data-testid="masked-email"]').text()).toBe('b***@miraiai.net')
    expect(section.get('[data-testid="send-reset-email"]').text()).toContain('发送重置邮件')
    expect(section.find('input').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('当前密码')
    expect(wrapper.text()).not.toContain('确认新密码')
    expect(wrapper.text()).not.toContain('保存修改')
  })

  it('发送重置邮件后进入 60 秒冷却，冷却期内不会再次请求', async () => {
    vi.useFakeTimers()
    const wrapper = await mountProfile()
    const button = wrapper.get('[data-testid="send-reset-email"]')

    await button.trigger('click')
    await flushPromises()

    expect(authApi.forgotPassword).toHaveBeenCalledTimes(1)
    expect(authApi.forgotPassword).toHaveBeenCalledWith({ email: 'boss@miraiai.net' })
    expect(useToast().list.map((item) => item.message)).toContain(RESET_SENT)
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.text()).toContain('秒后可再次发送')

    await button.trigger('click')
    await flushPromises()
    expect(authApi.forgotPassword).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(59_000)
    await nextTick()
    expect(button.attributes('disabled')).toBeDefined()

    await vi.advanceTimersByTimeAsync(1_000)
    await nextTick()
    expect(button.attributes('disabled')).toBeUndefined()
    expect(button.text()).toContain('发送重置邮件')
  })

  it('重置页会把确认密码一并提交，成功后进入登录页', async () => {
    vi.useFakeTimers()
    const { wrapper, router } = await mountReset()
    await wrapper.get('[data-testid="new-password"] input').setValue('Newpass1')
    await wrapper.get('[data-testid="confirm-password"] input').setValue('Newpass1')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.resetPassword).toHaveBeenCalledWith({
      token: 'reset-token',
      newPassword: 'Newpass1',
      confirmPassword: 'Newpass1',
    })

    await vi.advanceTimersByTimeAsync(1200)
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('确认密码不一致时不提交', async () => {
    const { wrapper } = await mountReset()
    await wrapper.get('[data-testid="new-password"] input').setValue('Newpass1')
    await wrapper.get('[data-testid="confirm-password"] input').setValue('Newpass2')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.resetPassword).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('两次输入的密码不一致')
  })

  it('过期链接在页面上显示链接已过期', async () => {
    vi.mocked(authApi.resetPassword).mockRejectedValueOnce(new Error('链接已过期'))
    const { wrapper } = await mountReset('expired-token')
    await wrapper.get('[data-testid="new-password"] input').setValue('Newpass1')
    await wrapper.get('[data-testid="confirm-password"] input').setValue('Newpass1')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="reset-error"]').text()).toBe('链接已过期')
  })

  it('已使用链接在页面上显示链接已使用', async () => {
    vi.mocked(authApi.resetPassword).mockRejectedValueOnce(new Error('链接已使用'))
    const { wrapper } = await mountReset('used-token')
    await wrapper.get('[data-testid="new-password"] input').setValue('Newpass1')
    await wrapper.get('[data-testid="confirm-password"] input').setValue('Newpass1')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="reset-error"]').text()).toBe('链接已使用')
  })
})
