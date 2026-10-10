import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const countries = [
  { name: '中国', code: 'cn' },
  { name: '日本', code: 'jp' },
  { name: '美国', code: 'us' },
  { name: '英国', code: 'gb' },
  { name: '新加坡', code: 'sg' },
  { name: '澳大利亚', code: 'au' },
  { name: '加拿大', code: 'ca' },
  { name: '韩国', code: 'kr' },
  { name: '德国', code: 'de' },
  { name: '法国', code: 'fr' },
]

vi.mock('@/api/region', () => ({
  regionApi: {
    countries: vi.fn().mockResolvedValue([
      { name: '中国', code: 'cn' },
      { name: '日本', code: 'jp' },
      { name: '美国', code: 'us' },
      { name: '英国', code: 'gb' },
      { name: '新加坡', code: 'sg' },
      { name: '澳大利亚', code: 'au' },
      { name: '加拿大', code: 'ca' },
      { name: '韩国', code: 'kr' },
      { name: '德国', code: 'de' },
      { name: '法国', code: 'fr' },
    ]),
    cities: vi.fn().mockImplementation(async (country: string, q: string) => {
      if (!q.trim()) return []
      if (country === '中国' && q.includes('上')) return [{ name: '上海', label: '中国 · 上海' }]
      if (country === '日本' && q.includes('东')) return [{ name: '东京', label: '日本 · 东京' }]
      return []
    }),
  },
}))

import { regionApi } from '@/api/region'
import RegionPicker from '@/components/RegionPicker.vue'

const Host = defineComponent({
  components: { RegionPicker },
  setup() {
    const place = ref('')
    const submitted = ref<string | null>(null)
    function onSubmit() {
      submitted.value = place.value
    }
    return { place, submitted, onSubmit }
  },
  template: `
    <form @submit.prevent="onSubmit">
      <RegionPicker v-model="place" />
      <button type="submit">保存</button>
    </form>
  `,
})

describe('所在地区选择器', () => {
  beforeEach(() => {
    vi.mocked(regionApi.cities).mockClear()
  })

  async function mountHost() {
    const wrapper = mount(Host)
    await flushPromises()
    return wrapper
  }

  it('国家按给定顺序展示，未选国家时城市不可用', async () => {
    const wrapper = await mountHost()
    const names = wrapper.get('[data-testid="region-country"]').findAll('option').map((option) => option.text())
    expect(names.slice(1)).toEqual(countries.map((item) => item.name))
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('所在地区')
  })

  it('更换国家会清掉已选城市', async () => {
    const wrapper = await mountHost()
    await wrapper.get('[data-testid="region-country"]').setValue('中国')
    await wrapper.get('[data-testid="region-city"]').setValue('上')
    await flushPromises()
    await wrapper.get('[data-testid="region-city-option"]').trigger('mousedown')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('中国 · 上海')

    await wrapper.get('[data-testid="region-country"]').setValue('日本')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('未填写')
    expect((wrapper.get('[data-testid="region-city"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeUndefined()
  })

  it('空搜索和选中后再清空都不会把无效内容提交出去', async () => {
    const wrapper = await mountHost()
    await wrapper.get('[data-testid="region-country"]').setValue('中国')
    await wrapper.get('[data-testid="region-city"]').setValue('   ')
    await wrapper.get('[data-testid="region-city"]').trigger('keydown', { key: 'Enter' })
    await wrapper.get('form').trigger('submit')
    expect(regionApi.cities).not.toHaveBeenCalled()
    expect(wrapper.vm.submitted).toBe('')

    await wrapper.get('[data-testid="region-city"]').setValue('上')
    await flushPromises()
    await wrapper.get('form').trigger('submit')
    expect(wrapper.vm.submitted).toBe('')

    await wrapper.get('[data-testid="region-city-option"]').trigger('mousedown')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('中国 · 上海')
    await wrapper.get('[data-testid="region-city"]').setValue('')
    await wrapper.get('[data-testid="region-city"]').trigger('keydown', { key: 'Enter' })
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('未填写')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.vm.submitted).toBe('')
    expect(wrapper.vm.submitted).not.toBe('上')
  })
})
