import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/memo', () => ({
  memoApi: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 200 }),
    create: vi.fn(),
    update: vi.fn(),
    patchStatus: vi.fn(),
    remove: vi.fn(),
  },
}))

vi.mock('@/api/auth', () => ({
  bindAuthHooks: vi.fn(),
  authApi: {
    getMemoReminderSettings: vi.fn().mockResolvedValue({ barkConfigured: false }),
    updateMemoReminderSettings: vi.fn().mockImplementation(async (payload: { barkKey: string }) => ({
      barkConfigured: payload.barkKey.length > 0,
    })),
    getWelcomeSettings: vi.fn().mockResolvedValue({ place: '', nickname: '' }),
    updateWelcomeSettings: vi.fn().mockImplementation(async (payload: { place: string; nickname: string }) => ({
      place: payload.place.trim() || null,
      nickname: payload.nickname.trim() || null,
    })),
    forgotPassword: vi.fn(),
    logout: vi.fn(),
  },
}))

import { authApi } from '@/api/auth'
import MemoBoard from '@/components/MemoBoard.vue'
import { useAuthStore } from '@/stores/auth'
import ProfileView from '@/views/ProfileView.vue'

const Passthrough = defineComponent({ template: '<router-view />' })

describe('备忘提醒界面', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(authApi.getMemoReminderSettings).mockClear()
    vi.mocked(authApi.updateMemoReminderSettings).mockClear()
  })

  it('邮件勾选旁边写着没收到请看垃圾箱', async () => {
    const wrapper = mount(MemoBoard, {
      props: { section: 'work', accent: 'teal', title: '工作备忘' },
      global: { plugins: [createPinia()] },
    })
    await flushPromises()

    await wrapper.get('button').trigger('click')
    const hint = wrapper.get('[data-testid="email-spam-hint"]')
    expect(hint.text()).toBe('没收到请看垃圾箱')
    expect(hint.element.parentElement?.textContent).toContain('邮件')
  })

  it('个人设置可以填写或清空 Bark key，页面不回显已保存的 key', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Passthrough, children: [{ path: '', component: ProfileView }] },
        { path: '/login', component: { template: '<div>login</div>' } },
      ],
    })
    router.push('/')
    await router.isReady()

    const wrapper = mount(Passthrough, { global: { plugins: [createPinia(), router] } })
    await flushPromises()

    expect(wrapper.get('[data-testid="bark-key-status"]').text()).toContain('未填写')
    const input = wrapper.get('[data-testid="bark-key-input"]')
    await input.setValue('unitTestBarkKey1')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.updateMemoReminderSettings).toHaveBeenCalledWith({ barkKey: 'unitTestBarkKey1' })
    expect((input.element as HTMLInputElement).value).toBe('')
    expect(wrapper.text()).not.toContain('unitTestBarkKey1')
    expect(wrapper.get('[data-testid="bark-key-status"]').text()).toContain('已填写')

    await input.setValue('   ')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(authApi.updateMemoReminderSettings).toHaveBeenLastCalledWith({ barkKey: '' })
    expect(wrapper.get('[data-testid="bark-key-status"]').text()).toContain('未填写')
  })

  it('个人设置可以填写或清空国家-城市', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Passthrough, children: [{ path: '', component: ProfileView }] },
        { path: '/login', component: { template: '<div>login</div>' } },
      ],
    })
    router.push('/')
    await router.isReady()

    const wrapper = mount(Passthrough, { global: { plugins: [createPinia(), router] } })
    await flushPromises()

    expect(wrapper.get('[data-testid="weather-place-status"]').text()).toContain('未填写')
    const input = wrapper.get('[data-testid="weather-place-input"]')
    await input.setValue('  中国-上海  ')
    await wrapper.get('[data-testid="weather-place-form"]').trigger('submit')
    await flushPromises()

    expect(authApi.updateWelcomeSettings).toHaveBeenCalledWith({ place: '中国-上海', nickname: '' })
    expect((input.element as HTMLInputElement).value).toBe('中国-上海')
    expect(wrapper.get('[data-testid="weather-place-status"]').text()).toContain('中国-上海')

    await input.setValue('   ')
    await wrapper.get('[data-testid="weather-place-form"]').trigger('submit')
    await flushPromises()
    expect(authApi.updateWelcomeSettings).toHaveBeenLastCalledWith({ place: '', nickname: '' })
    expect(wrapper.get('[data-testid="weather-place-status"]').text()).toContain('未填写')
  })

  it('个人设置可以填写或清空昵称，账户名仍是用户名', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().setAuth({
      accessToken: 'token',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z',
      user: {
        id: 1,
        username: 'tester',
        email: 'tester@example.com',
        isAdmin: false,
        isEmailVerified: true,
        isActive: true,
        lastLoginAt: null,
        createdAt: '2026-10-01T00:00:00Z',
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Passthrough, children: [{ path: '', component: ProfileView }] },
        { path: '/login', component: { template: '<div>login</div>' } },
      ],
    })
    router.push('/')
    await router.isReady()

    const wrapper = mount(Passthrough, { global: { plugins: [pinia, router] } })
    await flushPromises()

    expect(wrapper.get('[data-testid="nickname-status"]').text()).toContain('使用用户名')
    expect(wrapper.text()).toContain('tester')
    const input = wrapper.get('[data-testid="nickname-input"]')
    await input.setValue('  雅美  ')
    await wrapper.get('[data-testid="nickname-form"]').trigger('submit')
    await flushPromises()

    expect(authApi.updateWelcomeSettings).toHaveBeenCalledWith({ place: '', nickname: '雅美' })
    expect((input.element as HTMLInputElement).value).toBe('雅美')
    expect(wrapper.get('[data-testid="nickname-status"]').text()).toContain('雅美')
    expect(wrapper.get('p.font-semibold').text()).toBe('tester')

    await input.setValue('   ')
    await wrapper.get('[data-testid="nickname-form"]').trigger('submit')
    await flushPromises()
    expect(authApi.updateWelcomeSettings).toHaveBeenLastCalledWith({ place: '', nickname: '' })
    expect(wrapper.get('[data-testid="nickname-status"]').text()).toContain('使用用户名')
    expect(wrapper.get('p.font-semibold').text()).toBe('tester')
  })
})
