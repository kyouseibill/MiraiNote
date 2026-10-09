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
    getGreeting: vi.fn().mockResolvedValue({
      content: 'tester，10月9日',
      featureNote: null,
      weatherWarning: null,
      news: [],
    }),
  },
}))

import { welcomeApi } from '@/api/welcome'
import DashboardView from '@/views/DashboardView.vue'

describe('工作台欢迎语挂载', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue({
      content: 'tester，10月9日',
      featureNote: null,
      weatherWarning: null,
      news: [],
    })
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
    expect(wrapper.find('[data-testid="weather-warning"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="welcome-news"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('有特别预警和新闻时显示文本和标题链接', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue({
      content: 'tester，10月9日',
      featureNote: '功能句还在',
      weatherWarning: '上海中心气象台发布暴雨红色预警',
      news: [
        { title: 'OpenAI 更新', url: 'https://openai.com/news/b' },
        { title: 'TechCrunch 最新', url: 'https://techcrunch.com/2026/10/09/newest' },
        { title: '不该出现', url: 'javascript:alert(1)' },
      ],
    })

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await flushPromises()

    expect(wrapper.get('[data-testid="weather-warning"]').text()).toContain('暴雨红色预警')
    expect(wrapper.text()).toContain('功能句还在')
    const links = wrapper.get('[data-testid="welcome-news"]').findAll('[data-testid="welcome-news-link"]')
    expect(links).toHaveLength(2)
    expect(links[0].text()).toBe('OpenAI 更新')
    expect(links[0].attributes('href')).toBe('https://openai.com/news/b')
    expect(links[1].attributes('href')).toBe('https://techcrunch.com/2026/10/09/newest')
    expect(wrapper.text()).not.toContain('不该出现')
    wrapper.unmount()
  })
})
