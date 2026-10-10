import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/region', () => ({
  regionApi: {
    countries: vi.fn().mockResolvedValue([
      { name: '中国', code: 'cn' },
      { name: '日本', code: 'jp' },
    ]),
    cities: vi.fn().mockImplementation(async (country: string, q: string) => {
      if (country === '中国' && q.includes('上')) return [{ name: '上海', label: '中国 · 上海' }]
      return []
    }),
  },
}))

vi.mock('@/api/auth', () => ({
  bindAuthHooks: vi.fn(),
  authApi: {
    register: vi.fn().mockResolvedValue({ outcome: 'verification_disabled', message: '注册成功' }),
  },
}))

import { authApi } from '@/api/auth'
import RegisterView from '@/views/RegisterView.vue'

describe('注册必须选择所在地区', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(authApi.register).mockClear()
  })

  async function mountRegister() {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/register', name: 'register', component: RegisterView },
        { path: '/login', name: 'login', component: { template: '<div>login</div>' } },
      ],
    })
    await router.push('/register')
    await router.isReady()
    const wrapper = mount(RegisterView, { global: { plugins: [createPinia(), router] } })
    await flushPromises()
    return wrapper
  }

  async function fillAccount(wrapper: Awaited<ReturnType<typeof mountRegister>>) {
    await wrapper.get('input[autocomplete="username"]').setValue('mirai.user')
    await wrapper.get('input[autocomplete="email"]').setValue('you@example.com')
    const passwords = wrapper.findAll('input[type="password"]')
    await passwords[0].setValue('Password1')
    await passwords[1].setValue('Password1')
  }

  it('没选地区时不能提交', async () => {
    const wrapper = await mountRegister()
    expect(wrapper.text()).toContain('所在地区')
    await fillAccount(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.register).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="region-error"]').text()).toBe('请选择所在地区')
  })

  it('换国家后城市被清空，选好城市才能提交', async () => {
    const wrapper = await mountRegister()
    await fillAccount(wrapper)
    await wrapper.get('[data-testid="region-country"]').setValue('中国')
    await wrapper.get('[data-testid="region-city"]').setValue('上')
    await flushPromises()
    await wrapper.get('[data-testid="region-city-option"]').trigger('mousedown')
    await wrapper.get('[data-testid="region-country"]').setValue('日本')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.register).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="region-error"]').text()).toBe('请选择所在地区')

    await wrapper.get('[data-testid="region-country"]').setValue('中国')
    await wrapper.get('[data-testid="region-city"]').setValue('上')
    await flushPromises()
    await wrapper.get('[data-testid="region-city-option"]').trigger('mousedown')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.register).toHaveBeenCalledWith({
      username: 'mirai.user',
      email: 'you@example.com',
      password: 'Password1',
      confirmPassword: 'Password1',
      place: '中国 · 上海',
    })
  })
})
