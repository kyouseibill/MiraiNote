import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/memo', () => ({
  memoApi: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 }),
    create: vi.fn(),
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
import { memoApi } from '@/api/memo'
import { welcomeApi, type WelcomeGreeting } from '@/api/welcome'
import { useToast } from '@/composables/useToast'
import type { CreateMemoPayload, Memo } from '@/types/memo'
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

function memo(partial: Partial<Memo> & Pick<Memo, 'id' | 'section' | 'content'>): Memo {
  return {
    remindAt: null,
    remindMethods: 0,
    emailReminderSent: false,
    popupAcknowledged: false,
    remindedAt: null,
    priority: 2,
    isPinned: false,
    isDone: false,
    isArchived: false,
    createdAt: '2026-10-10T08:00:00',
    updatedAt: '2026-10-10T08:00:00',
    ...partial,
  }
}

function stubPlatform(kind: 'mac' | 'windows' | 'linux') {
  const platform = kind === 'mac' ? 'MacIntel' : kind === 'windows' ? 'Win32' : 'Linux x86_64'
  const userAgent = kind === 'mac'
    ? 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)'
    : kind === 'windows'
      ? 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
      : 'Mozilla/5.0 (X11; Linux x86_64)'
  const uaPlatform = kind === 'mac' ? 'macOS' : kind === 'windows' ? 'Windows' : 'Linux'
  Object.defineProperty(navigator, 'platform', { value: platform, configurable: true })
  Object.defineProperty(navigator, 'userAgent', { value: userAgent, configurable: true })
  Object.defineProperty(navigator, 'userAgentData', { value: { platform: uaPlatform }, configurable: true })
}

async function mountDashboard() {
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
  return wrapper
}

describe('记录此刻', () => {
  let nextId = 100

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.setSystemTime(fixedNow)
    nextId = 100
    stubPlatform('linux')
    vi.mocked(welcomeApi.getGreeting).mockResolvedValue(greeting({ displayName: 'tester' }))
    vi.mocked(memoApi.list).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 })
    vi.mocked(memoApi.create).mockImplementation(async (payload: CreateMemoPayload) => memo({
      id: ++nextId,
      section: payload.section,
      content: payload.content,
    }))
    useToast().list.splice(0, useToast().list.length)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('默认记到工作备忘，切换后记到生活备忘，并立刻出现在今日焦点', async () => {
    const wrapper = await mountDashboard()
    const input = wrapper.get('[data-testid="quick-capture-input"]')

    expect(wrapper.get('[data-testid="capture-section-work"]').attributes('aria-checked')).toBe('true')
    expect(wrapper.get('[data-testid="capture-section-life"]').attributes('aria-checked')).toBe('false')
    expect(input.attributes('placeholder')).toBe('记一条工作备忘…')

    await input.setValue('写周报')
    await input.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect(memoApi.create).toHaveBeenCalledWith({ section: 'work', content: '写周报', priority: 2 })
    const workItem = wrapper.get('[data-testid="focus-item"]')
    expect(workItem.attributes('data-section')).toBe('work')
    expect(workItem.text()).toContain('写周报')
    expect(workItem.text()).toContain('工作')
    expect(workItem.get('[data-testid="focus-dot"]').classes()).toContain('bg-[#4c6178]')
    expect(useToast().list.map((item) => item.message)).toContain('已记到工作备忘')
    expect(input.element).toHaveProperty('value', '')

    await wrapper.get('[data-testid="capture-section-life"]').trigger('click')
    expect(input.attributes('placeholder')).toBe('记一条生活备忘…')
    await input.setValue('买花')
    expect(wrapper.get('[data-testid="quick-capture-submit"]').attributes('type')).toBe('submit')
    await wrapper.get('[data-testid="quick-capture-submit"]').trigger('click')
    await wrapper.get('[data-testid="quick-capture"]').trigger('submit')
    await flushPromises()

    expect(memoApi.create).toHaveBeenLastCalledWith({ section: 'life', content: '买花', priority: 2 })
    const lifeItem = wrapper.findAll('[data-testid="focus-item"]').find((item) => item.attributes('data-section') === 'life')
    expect(lifeItem).toBeTruthy()
    expect(lifeItem!.text()).toContain('买花')
    expect(lifeItem!.text()).toContain('生活')
    expect(lifeItem!.get('[data-testid="focus-dot"]').classes()).toContain('bg-[#b4493f]')
    expect(useToast().list.map((item) => item.message)).toContain('已记到生活备忘')
    wrapper.unmount()
  })

  it('已有三条到期焦点时，新记下的生活备忘仍出现在今日焦点', async () => {
    vi.mocked(memoApi.list).mockImplementation(async (query) => ({
      items: query.section === 'work'
        ? [1, 2, 3].map((id) => memo({
            id,
            section: 'work',
            content: `到期 ${id}`,
            remindAt: `2026-10-10T0${id}:00:00`,
          }))
        : [],
      total: query.section === 'work' ? 3 : 0,
      page: 1,
      pageSize: 20,
    }))

    const wrapper = await mountDashboard()
    await wrapper.get('[data-testid="capture-section-life"]').trigger('click')
    await wrapper.get('[data-testid="quick-capture-input"]').setValue('散步')
    await wrapper.get('[data-testid="quick-capture"]').trigger('submit')
    await flushPromises()

    const lifeItem = wrapper.findAll('[data-testid="focus-item"]').find((item) => item.text().includes('散步'))
    expect(lifeItem?.attributes('data-section')).toBe('life')
    expect(lifeItem?.get('[data-testid="focus-dot"]').classes()).toContain('bg-[#b4493f]')
    wrapper.unmount()
  })

  it('Enter、Cmd+Enter 和 Ctrl+Enter 都会提交', async () => {
    const wrapper = await mountDashboard()
    const input = wrapper.get('[data-testid="quick-capture-input"]')

    await input.setValue('第一条')
    await input.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    await input.setValue('第二条')
    await input.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    await input.setValue('第三条')
    await input.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await flushPromises()

    expect(vi.mocked(memoApi.create).mock.calls.map((call) => call[0].content)).toEqual(['第一条', '第二条', '第三条'])
    wrapper.unmount()
  })

  it('空内容和纯空格不会提交', async () => {
    const wrapper = await mountDashboard()
    const input = wrapper.get('[data-testid="quick-capture-input"]')

    await input.setValue('   ')
    await input.trigger('keydown', { key: 'Enter' })
    await input.trigger('keydown', { key: 'Enter', metaKey: true })
    await input.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await wrapper.get('[data-testid="quick-capture"]').trigger('submit')
    await flushPromises()

    expect(memoApi.create).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="focus-item"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('输入法组字中的 Enter 不提交', async () => {
    const wrapper = await mountDashboard()
    const input = wrapper.get('[data-testid="quick-capture-input"]')
    await input.setValue('组字')
    await input.trigger('keydown', { key: 'Enter', isComposing: true })
    await flushPromises()

    expect(memoApi.create).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('Mac 显示 ⌘+Enter，Windows 和 Linux 显示 Ctrl+Enter', async () => {
    stubPlatform('mac')
    const mac = await mountDashboard()
    expect(mac.get('[data-testid="capture-shortcut"]').text()).toBe('⌘+Enter')
    mac.unmount()

    stubPlatform('windows')
    const windows = await mountDashboard()
    expect(windows.get('[data-testid="capture-shortcut"]').text()).toBe('Ctrl+Enter')
    windows.unmount()

    stubPlatform('linux')
    const linux = await mountDashboard()
    expect(linux.get('[data-testid="capture-shortcut"]').text()).toBe('Ctrl+Enter')
    linux.unmount()
  })
})
