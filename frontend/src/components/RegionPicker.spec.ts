import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const countries = [
  { name: '中国', englishName: 'China', code: 'cn' },
  { name: '日本', englishName: 'Japan', code: 'jp' },
  { name: '美国', englishName: 'United States', code: 'us' },
  { name: '英国', englishName: 'United Kingdom', code: 'gb' },
  { name: '新加坡', englishName: 'Singapore', code: 'sg' },
  { name: '澳大利亚', englishName: 'Australia', code: 'au' },
  { name: '加拿大', englishName: 'Canada', code: 'ca' },
  { name: '韩国', englishName: 'South Korea', code: 'kr' },
  { name: '德国', englishName: 'Germany', code: 'de' },
  { name: '法国', englishName: 'France', code: 'fr' },
  { name: '印度', englishName: 'India', code: 'in' },
  { name: '巴西', englishName: 'Brazil', code: 'br' },
]

vi.mock('@/api/region', () => ({
  regionApi: {
    countries: vi.fn().mockResolvedValue([
      { name: '中国', englishName: 'China', code: 'cn' },
      { name: '日本', englishName: 'Japan', code: 'jp' },
      { name: '美国', englishName: 'United States', code: 'us' },
      { name: '英国', englishName: 'United Kingdom', code: 'gb' },
      { name: '新加坡', englishName: 'Singapore', code: 'sg' },
      { name: '澳大利亚', englishName: 'Australia', code: 'au' },
      { name: '加拿大', englishName: 'Canada', code: 'ca' },
      { name: '韩国', englishName: 'South Korea', code: 'kr' },
      { name: '德国', englishName: 'Germany', code: 'de' },
      { name: '法国', englishName: 'France', code: 'fr' },
      { name: '印度', englishName: 'India', code: 'in' },
      { name: '巴西', englishName: 'Brazil', code: 'br' },
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
    vi.mocked(regionApi.cities).mockImplementation(async (country: string, q: string) => {
      if (!q.trim()) return []
      if (country === '中国' && q.includes('上')) return [{ name: '上海', label: '中国 · 上海' }]
      if (country === '日本' && q.includes('东')) return [{ name: '东京', label: '日本 · 东京' }]
      return []
    })
  })

  async function mountHost() {
    const wrapper = mount(Host)
    await flushPromises()
    return wrapper
  }

  async function chooseCountry(wrapper: Awaited<ReturnType<typeof mountHost>>, query: string) {
    const input = wrapper.get('[data-testid="region-country"]')
    await input.setValue(query)
    await input.trigger('keydown', { key: 'Enter' })
  }

  function countryNames(wrapper: Awaited<ReturnType<typeof mountHost>>) {
    return wrapper.findAll('[data-testid="region-country-name"]').map((item) => item.text())
  }

  it('国家按置顶顺序展示，未选国家时城市不可用', async () => {
    const wrapper = await mountHost()
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('所在地区')
    await wrapper.get('[data-testid="region-country"]').trigger('focus')
    expect(countryNames(wrapper)).toEqual(countries.map((item) => item.name))
    expect(countryNames(wrapper).slice(0, 2)).toEqual(['中国', '日本'])
  })

  it('中文、英文和代码都能搜到国家，并按原顺序显示', async () => {
    const wrapper = await mountHost()
    const input = wrapper.get('[data-testid="region-country"]')

    await input.setValue('日本')
    expect(countryNames(wrapper)).toEqual(['日本'])
    await input.setValue('Japan')
    expect(countryNames(wrapper)).toEqual(['日本'])
    await input.setValue('jp')
    expect(countryNames(wrapper)).toEqual(['日本'])

    await input.setValue('India')
    expect(countryNames(wrapper)).toEqual(['印度'])
    await input.setValue('br')
    expect(countryNames(wrapper)).toEqual(['巴西'])
    await input.setValue('us')
    expect(countryNames(wrapper)).toEqual(['美国', '澳大利亚'])

    await input.setValue('   ')
    expect(countryNames(wrapper)).toEqual(countries.map((item) => item.name))
    expect(wrapper.find('[data-testid="region-country-empty"]').exists()).toBe(false)

    await input.setValue('没有这个国家')
    expect(countryNames(wrapper)).toEqual([])
    expect(wrapper.get('[data-testid="region-country-empty"]').text()).toContain('没有匹配的国家')
  })

  it('更换国家会清掉已选城市，城市仍按所选国家搜索', async () => {
    const wrapper = await mountHost()
    await chooseCountry(wrapper, '中国')
    await wrapper.get('[data-testid="region-city"]').setValue('上')
    await flushPromises()
    await wrapper.get('[data-testid="region-city-option"]').trigger('mousedown')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('中国 · 上海')

    await chooseCountry(wrapper, 'jp')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('未填写')
    expect((wrapper.get('[data-testid="region-city"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeUndefined()
    expect((wrapper.get('[data-testid="region-country"]').element as HTMLInputElement).value).toBe('日本')

    await wrapper.get('[data-testid="region-city"]').setValue('东')
    await flushPromises()
    expect(regionApi.cities).toHaveBeenLastCalledWith('日本', '东')
  })

  it('空搜索和选中后再清空都不会把无效内容提交出去', async () => {
    const wrapper = await mountHost()
    await wrapper.get('[data-testid="region-country"]').setValue('   ')
    await wrapper.get('[data-testid="region-country"]').trigger('keydown', { key: 'Enter' })
    await wrapper.get('form').trigger('submit')
    expect(wrapper.vm.submitted).toBe('')
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeDefined()

    await wrapper.get('[data-testid="region-country"]').setValue('法国')
    await wrapper.get('[data-testid="region-country"]').trigger('keydown', { key: 'Enter' })
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[data-testid="region-country"]').setValue('不是国家')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.vm.submitted).toBe('')
    expect(wrapper.get('[data-testid="region-city"]').attributes('disabled')).toBeDefined()

    await chooseCountry(wrapper, '中国')
    await flushPromises()
    await wrapper.get('[data-testid="region-city"]').setValue('   ')
    await wrapper.get('[data-testid="region-city"]').trigger('keydown', { key: 'Enter' })
    await wrapper.get('form').trigger('submit')
    expect(regionApi.cities).toHaveBeenCalledWith('中国', '')
    expect(vi.mocked(regionApi.cities).mock.calls.every((call) => String(call[1]).trim() === '')).toBe(true)
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

  it('选中日本后不用输入就能滚动到札幌，搜索会和常用城市去重', async () => {
    const japanCommons = [
      { name: '东京', label: '日本 · 东京' },
      { name: '大阪', label: '日本 · 大阪' },
      { name: '名古屋', label: '日本 · 名古屋' },
      { name: '札幌市', label: '日本 · 札幌市' },
      { name: '福冈', label: '日本 · 福冈' },
      { name: '京都', label: '日本 · 京都' },
      { name: '神户', label: '日本 · 神户' },
      { name: '横滨', label: '日本 · 横滨' },
    ]
    vi.mocked(regionApi.cities).mockImplementation(async (country: string, q: string) => {
      if (country !== '日本') return []
      if (!q.trim()) return japanCommons
      if (q === '大阪') return [{ name: '大阪市', label: '日本 · 大阪市' }]
      return []
    })

    const wrapper = await mountHost()
    await chooseCountry(wrapper, '日本')
    await flushPromises()

    expect(regionApi.cities).toHaveBeenCalledWith('日本', '')
    const list = wrapper.get('[data-testid="region-city-list"]')
    expect(list.classes()).toEqual(expect.arrayContaining(['max-h-60', 'overflow-auto']))
    expect(list.text()).toContain('札幌市')
    expect(list.text()).toContain('大阪')
    expect(list.text()).toContain('名古屋')
    expect(list.text()).toContain('东京')

    const sapporo = wrapper.get('[data-testid="region-city-list"]').findAll('[data-testid="region-city-option"]')
      .find((item) => item.text().includes('札幌'))
    expect(sapporo).toBeTruthy()
    await sapporo!.trigger('mousedown')
    expect(wrapper.get('[data-testid="region-status"]').text()).toContain('日本 · 札幌市')

    await wrapper.get('[data-testid="region-city"]').setValue('大阪')
    await flushPromises()
    expect(regionApi.cities).toHaveBeenLastCalledWith('日本', '大阪')
    const osaka = wrapper.findAll('[data-testid="region-city-option"]').map((item) => item.text())
    expect(osaka.filter((item) => item.includes('大阪'))).toEqual(['日本 · 大阪市'])

    await wrapper.get('[data-testid="region-city"]').setValue('札')
    await flushPromises()
    expect(regionApi.cities).toHaveBeenLastCalledWith('日本', '札')
    expect(wrapper.get('[data-testid="region-city-list"]').text()).toContain('札幌市')
  })
})
