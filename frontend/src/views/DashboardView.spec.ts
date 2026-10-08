import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/memo', () => ({
  memoApi: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 }),
  },
}))

vi.mock('@/api/workLog', () => ({
  workLogApi: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 5 }),
  },
}))

vi.mock('@/api/welcome', () => ({
  welcomeApi: {
    getGreeting: vi.fn().mockResolvedValue({ content: 'tester，10月9日', featureNote: null }),
  },
}))

import DashboardView from '@/views/DashboardView.vue'

describe('工作台欢迎语挂载', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('打开时能挂上，日期按上海日历日显示', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: {
        plugins: [createPinia(), router],
      },
    })
    await flushPromises()

    expect(wrapper.text()).toMatch(/\d+月\d+日/)
    wrapper.unmount()
  })
})
