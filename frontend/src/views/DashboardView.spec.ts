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

vi.mock('@/api/auth', () => ({
  bindAuthHooks: vi.fn(),
  authApi: {
    getWelcomeSettings: vi.fn().mockResolvedValue({ place: '中国 · 上海', nickname: null }),
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

import { authApi } from '@/api/auth'
import { welcomeApi, type WelcomeGreeting } from '@/api/welcome'
import { composeGreeting } from '@/utils/greetingPeriod'
import DashboardView from '@/views/DashboardView.vue'

/** 周四下午，避开周五句，方便断言时段池。 */
const fixedNow = new Date(2026, 9, 8, 15, 0, 0)

function expectedGreeting(name: string, weatherBrief: string | null = null, now = fixedNow): string {
  return composeGreeting({ name, now, weatherBrief })
}

function greeting(partial: Partial<WelcomeGreeting> & Pick<WelcomeGreeting, 'displayName'>): WelcomeGreeting {
  return {
    content: partial.displayName,
    featureNote: null,
    weatherWarning: null,
    news: [],
    dateLine: '10月9日 · 周五',
    weatherBrief: null,
    memoSummary: null,
    greetingLine: null,
    inspirationLine: null,
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
    vi.setSystemTime(fixedNow)
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({ displayName: 'tester' }))
    vi.mocked(authApi.getWelcomeSettings).mockResolvedValue({ place: '中国 · 上海', nickname: null })
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
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('tester'))
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).not.toMatch(/。$/)
    expect(wrapper.find('[data-testid="welcome-inspiration"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="weather-warning"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="welcome-news"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('有问候和小句时合成一行，预警单独在下面', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: '雅美',
      greetingLine: '下午好，雅美',
      inspirationLine: '今天不必赶完所有事，把眼前这一小步走稳。',
      memoSummary: '今天有 1 条备忘到期。',
      weatherWarning: '上海中心气象台发布暴雨红色预警',
      news: [{ title: 'OpenAI 更新', url: 'https://openai.com/news/b' }],
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/?welcomeNow=2026-10-09T04:59:00')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(welcomeApi.getGreeting).toHaveBeenCalledWith('2026-10-09T04:59:00')
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(
      '下午好，雅美。今天不必赶完所有事，把眼前这一小步走稳。',
    )
    expect(wrapper.find('[data-testid="welcome-inspiration"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).not.toContain('暴雨')
    expect(wrapper.text()).not.toContain('王维')
    expect(wrapper.text()).not.toContain('《')
    expect(wrapper.get('[data-testid="welcome-memo"]').text()).toBe('今天有 1 条备忘到期。')
    expect(wrapper.findAll('[data-testid="weather-warning"]')).toHaveLength(1)
    expect(wrapper.get('[data-testid="weather-warning"]').text()).toContain('暴雨红色预警')
    const order = wrapper.findAll('[data-testid]').map((node) => node.attributes('data-testid'))
    expect(order.indexOf('welcome-greeting')).toBeLessThan(order.indexOf('weather-warning'))
    expect(order.indexOf('weather-warning')).toBeLessThan(order.indexOf('welcome-memo'))
    expect(wrapper.get('[data-testid="welcome-news-link"]').text()).toBe('OpenAI 更新')
    wrapper.unmount()
  })

  it('示例把问候和小句接成一句，预警留在下一行', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: 'Bill',
      greetingLine: '早上好，Bill',
      inspirationLine: '上海秋日多云，梧桐叶在风里打着旋。',
      weatherWarning: '上海中心气象台发布暴雨红色预警',
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

    const heading = wrapper.get('[data-testid="welcome-greeting"]').text()
    expect(heading).toBe('早上好，Bill。上海秋日多云，梧桐叶在风里打着旋。')
    expect(heading).not.toContain('暴雨')
    expect(wrapper.get('[data-testid="weather-warning"]').text()).toContain('暴雨红色预警')
    const order = wrapper.findAll('[data-testid]').map((node) => node.attributes('data-testid'))
    expect(order.indexOf('welcome-greeting')).toBeLessThan(order.indexOf('weather-warning'))
    expect(order.indexOf('welcome-date')).toBeLessThan(order.indexOf('welcome-greeting'))
    wrapper.unmount()
  })

  it('小句为空时只留问候，并把 welcomeNow 传给接口', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: '雅美',
      greetingLine: null,
      inspirationLine: null,
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/?welcomeNow=2026-10-09T04:59:00')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(welcomeApi.getGreeting).toHaveBeenCalledWith('2026-10-09T04:59:00')
    const heading = wrapper.get('[data-testid="welcome-greeting"]').text()
    expect(heading).toBe('夜深了，雅美，早点休息')
    expect(heading.endsWith('。')).toBe(false)
    expect(wrapper.find('[data-testid="welcome-inspiration"]').exists()).toBe(false)
    expect(heading).not.toBe('周五了，雅美')
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

    expect(wrapper.findAll('[data-testid="weather-warning"]')).toHaveLength(1)
    expect(wrapper.get('[data-testid="weather-warning"]').text()).toContain('暴雨红色预警')
    const order = wrapper.findAll('[data-testid]').map((node) => node.attributes('data-testid'))
    expect(order.indexOf('welcome-greeting')).toBeLessThan(order.indexOf('weather-warning'))
    expect(order.indexOf('weather-warning')).toBeLessThan(order.indexOf('welcome-news'))
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('tester'))
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).not.toContain('暴雨')
    expect(wrapper.text()).toContain('功能句还在')
    const links = wrapper.get('[data-testid="welcome-news"]').findAll('[data-testid="welcome-news-link"]')
    expect(links).toHaveLength(2)
    expect(links[0].text()).toBe('OpenAI 更新')
    expect(links[0].attributes('href')).toBe('https://openai.com/news/b')
    expect(links[1].attributes('href')).toBe('https://techcrunch.com/2026/10/09/newest')
    expect(wrapper.text()).not.toContain('不该出现')
    wrapper.unmount()
  })

  it('有实况时只出现在日期行，大标题是问候句', async () => {
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
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('Bill.Gong', '多云 24°C'))
    expect(wrapper.get('[data-testid="welcome-memo"]').text()).toBe('今天有 1 条备忘到期。')
    expect(wrapper.findAll('[data-testid="weather-warning"]')).toHaveLength(1)
    const order = wrapper.findAll('[data-testid]').map((node) => node.attributes('data-testid'))
    expect(order.indexOf('welcome-greeting')).toBeLessThan(order.indexOf('weather-warning'))
    expect(order.indexOf('weather-warning')).toBeLessThan(order.indexOf('welcome-memo'))
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
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('Bill.Gong'))
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
    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('雅美', '多云 24°C'))
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

    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe(expectedGreeting('雅美', '多云 24°C'))
    expect(wrapper.get('[data-testid="welcome-date"]').text()).toBe('10月9日 · 周五 · 多云 24°C')
    expect(wrapper.text()).not.toContain('tester')
    wrapper.unmount()
  })

  it('开发构建的 welcomeNow 决定时段和日期', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/?welcomeNow=2026-10-09T15:00')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    expect(wrapper.get('[data-testid="welcome-greeting"]').text()).toBe('下午好，tester')
    wrapper.unmount()
  })

  it('welcomeNow 在周五深夜有雨时仍走深夜池', async () => {
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({
      displayName: 'Bill',
      weatherBrief: '雷阵雨',
    }))

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: DashboardView }],
    })
    await router.push('/?welcomeNow=2026-10-09T23:30')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: { plugins: [createPinia(), router] },
    })
    await reveal()

    const heading = wrapper.get('[data-testid="welcome-greeting"]').text()
    expect(heading).toBe('夜深了，Bill，早点休息')
    expect(heading.endsWith('。')).toBe(false)
    expect(heading).not.toBe('下雨了，Bill，记得带伞')
    expect(heading).not.toBe('周五了，Bill')
    expect(heading).not.toContain('雷阵雨')
    wrapper.unmount()
  })

  it('没填所在地区时每次进入工作台都提醒，跳过只对这一次生效', async () => {
    vi.mocked(authApi.getWelcomeSettings).mockResolvedValue({ place: null, nickname: null })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: DashboardView },
        { path: '/profile', component: { template: '<div>profile</div>' } },
      ],
    })
    await router.push('/')
    await router.isReady()

    const first = mount(DashboardView, { global: { plugins: [createPinia(), router] } })
    await reveal()
    expect(first.get('[data-testid="region-prompt"]').text()).toContain('所在地区')
    await first.get('[data-testid="region-prompt-skip"]').trigger('click')
    expect(first.find('[data-testid="region-prompt"]').exists()).toBe(false)
    first.unmount()

    const again = mount(DashboardView, { global: { plugins: [createPinia(), router] } })
    await reveal()
    expect(again.get('[data-testid="region-prompt"]').text()).toContain('下次打开工作台还会再提醒')
    again.unmount()
  })
})
