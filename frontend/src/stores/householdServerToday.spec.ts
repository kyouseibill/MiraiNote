import { createPinia, setActivePinia } from 'pinia'
import axios from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/household', () => ({
  householdApi: {
    serverToday: vi.fn(),
  },
}))

vi.mock('@/api/lifeLog', () => ({
  lifeLogApi: {},
}))

const { householdApi } = await import('@/api/household')
const { useHouseholdStore } = await import('@/stores/household')

describe('fetchServerToday', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(householdApi.serverToday).mockReset()
  })

  it('首次 404 之后不再请求', async () => {
    const error = new axios.AxiosError('missing')
    error.response = {
      status: 404,
      statusText: 'Not Found',
      headers: {},
      config: { headers: new axios.AxiosHeaders() },
      data: { success: false, message: '测试时钟未启用' },
    }
    vi.mocked(householdApi.serverToday).mockRejectedValue(error)

    const store = useHouseholdStore()
    await store.fetchServerToday()
    await store.fetchServerToday()

    expect(householdApi.serverToday).toHaveBeenCalledTimes(1)
    expect(store.calendarToday).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})
