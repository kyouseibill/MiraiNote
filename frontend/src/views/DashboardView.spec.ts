import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

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
      content: 'tester',
      displayName: 'tester',
      dateLine: '10月9日 · 周五',
      weatherBrief: null,
      memoSummary: null,
      featureNote: null,
      weatherWarning: null,
      news: [],
    }),
  },
}))

import { welcomeApi, type WelcomeGreeting } from '@/api/welcome'
import DashboardView from '@/views/DashboardView.vue'

function greeting(partial: Partial<WelcomeGreeting> & Pick<WelcomeGreeting, 'displayName'>): WelcomeGreeting {
  return {
    content: partial.displayName,
    featureNote: null,
    weatherWarning: null,
    news: [],
    dateLine: '10月9日 · 周五',
    weatherBrief: null,
    memoSummary: null,
    ...partial,
  }
}

async function reveal() {
  await flushPromises()
  await vi.advanceTimersByTimeAsync(2500)
  await flushPromises()
}

describe('工作台欢迎语挂载', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({ displayName: 'tester' }))
  })

  afterEach(() => {
    vi.useRealTimers()
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
    await reveal()

    expect(wrapper.get('[data-testid="welcome-date"]').text()).toBe('10月9日 · 周五')
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('tester')
    expect(wrapper.find('[data-testid="weather-warning"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="welcome-news"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('有特别预警和新闻时显示文本和标题链接', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: 'tester',
      featureNote: '功能句还在',
      weatherWarning: '上海中心气象台发布暴雨红色预警',
      news: [
        { title: 'OpenAI 更新', url: 'https://openai.com/news/b' },
        { title: 'TechCrunch 最新', url: 'https://techcrunch.com/2026/10/09/newest' },
        { title: '不该出现', url: 'javascript:alert(1)' },
      ],
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

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

  it('有实况时只出现在日期行，大标题只有称呼', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: 'Bill.Gong',
      content: 'Bill.Gong，10月9日 · 周五 · 多云 24°C',
      dateLine: '10月9日 · 周五 · 多云 24°C',
      weatherBrief: '多云 24°C',
      memoSummary: '今天有 1 条备忘到期。',
      weatherWarning: '上海中心气象台发布暴雨红色预警',
      news: [{ title: 'OpenAI 更新', url: 'https://openai.com/news/b' }],
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(wrapper.get('[data-testid="welcome-date"]').text()).toBe('10月9日 · 周五 · 多云 24°C')
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('Bill.Gong')
    expect(wrapper.get('[data-testid="welcome-memo"]').text()).toBe('今天有 1 条备忘到期。')
    expect(wrapper.get('[data-testid="weather-warning"]').text()).toContain('暴雨红色预警')
    expect(wrapper.get('[data-testid="welcome-news-link"]').text()).toBe('OpenAI 更新')
    const heading = wrapper.get('[data-testid="welcome-greeting"]').text()
    expect(heading).not.toContain('10月')
    expect(heading).not.toContain('周五')
    expect(heading).not.toContain('多云')
    expect(heading).not.toContain('24')
    expect(heading).not.toContain('备忘')
    wrapper.unmount()
  })

  it('没有实况时日期行不带天气', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: 'Bill.Gong',
      dateLine: '10月9日 · 周五',
      weatherBrief: null,
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    const date = wrapper.get('[data-testid="welcome-date"]').text()
    expect(date).toBe('10月9日 · 周五')
    expect(date).not.toMatch(/· [^周]/)
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('Bill.Gong')
    wrapper.unmount()
  })

  it('只给实况、没有日期行时，把天气接在上海日期后面', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: '雅美',
      content: 'tester，10月9日 · 周五 · 多云 24°C',
      dateLine: '',
      weatherBrief: '多云 24°C',
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(wrapper.get('[data-testid="welcome-date"]').text()).toMatch(/^\d+月\d+日 · 周[一二三四五六日] · 多云 24°C$/)
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('雅美')
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).not.toContain('tester')
    wrapper.unmount()
  })

  it('昵称优先于用户名，大标题不重复日期和天气', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: '雅美',
      content: 'tester，10月9日 · 周五 · 多云 24°C',
      dateLine: '10月9日 · 周五 · 多云 24°C',
      weatherBrief: '多云 24°C',
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('雅美')
    expect(wrapper.get('[data-testid="welcome-date"]').text()).toBe('10月9日 · 周五 · 多云 24°C')
    expect(wrapper.text()).not.toContain('tester')
    wrapper.unmount()
  })
})
