import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/skills', () => ({
  skillsApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    setEnabled: vi.fn(),
    remove: vi.fn(),
  },
}))

import { skillsApi, type SkillDocument, type SkillSummary } from '@/api/skills'
import SkillsView from '@/views/SkillsView.vue'

const CHAT_TIP = '也可以在对话里直接说「帮我创建一个 Skill」或「改一下这个 Skill」，Mirai 会帮你写好。'

const sample: SkillSummary = {
  name: 'yahoo-transit-jp',
  description: '查询日本铁路和公交换乘。',
  enabled: true,
  allowImplicitInvocation: true,
  error: null,
}

async function mountSkills() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/skills', component: SkillsView }],
  })
  await router.push('/skills')
  await router.isReady()
  const wrapper = mount(SkillsView, {
    global: { plugins: [router] },
  })
  await flushPromises()
  return wrapper
}

describe('技能管理页对话提示', () => {
  beforeEach(() => {
    vi.mocked(skillsApi.list).mockReset()
    vi.mocked(skillsApi.get).mockReset()
  })

  it('没有 Skill 时，提示仍在列表上方', async () => {
    vi.mocked(skillsApi.list).mockResolvedValue([])

    const wrapper = await mountSkills()
    const aside = wrapper.get('aside')
    const tip = aside.get('p')

    expect(tip.text()).toBe(CHAT_TIP)
    expect(tip.classes()).toContain('text-[var(--mn-muted)]')
    expect(aside.text()).toContain('还没有 Skill')
    expect(aside.text().indexOf(CHAT_TIP)).toBeLessThan(aside.text().indexOf('还没有 Skill'))
    wrapper.unmount()
  })

  it('已有 Skill 时，提示仍在列表上方', async () => {
    const detail: SkillDocument = { ...sample, markdown: '---\nname: yahoo-transit-jp\n---\n' }
    vi.mocked(skillsApi.list).mockResolvedValue([sample])
    vi.mocked(skillsApi.get).mockResolvedValue(detail)

    const wrapper = await mountSkills()
    const aside = wrapper.get('aside')

    expect(aside.get('p').text()).toBe(CHAT_TIP)
    expect(aside.text()).toContain(sample.name)
    expect(aside.text().indexOf(CHAT_TIP)).toBeLessThan(aside.text().indexOf(sample.name))
    wrapper.unmount()
  })
})
