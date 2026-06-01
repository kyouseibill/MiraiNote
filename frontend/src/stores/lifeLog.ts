import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {
  LifeLog,
  CreateLifeLogPayload,
  UpdateLifeLogPayload,
  LifeLogListQuery,
} from '@/types/lifeLog'
import { lifeLogApi } from '@/api/lifeLog'

export const useLifeLogStore = defineStore('lifeLog', () => {
  const items = ref<LifeLog[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(20)
  const loading = ref(false)

  async function fetchList(query: LifeLogListQuery = {}) {
    loading.value = true
    try {
      const q: LifeLogListQuery = {
        page: query.page ?? page.value,
        pageSize: query.pageSize ?? pageSize.value,
        ...query,
      }
      const result = await lifeLogApi.list(q)
      items.value = result.items
      total.value = result.total
      page.value = result.page
      pageSize.value = result.pageSize
      return result
    } finally {
      loading.value = false
    }
  }

  async function create(payload: CreateLifeLogPayload) {
    const created = await lifeLogApi.create(payload)
    await fetchList({ page: 1 })
    return created
  }

  async function update(id: number, payload: UpdateLifeLogPayload) {
    const updated = await lifeLogApi.update(id, payload)
    const idx = items.value.findIndex((l) => l.id === id)
    if (idx >= 0) items.value[idx] = updated
    return updated
  }

  async function remove(id: number) {
    await lifeLogApi.remove(id)
    items.value = items.value.filter((l) => l.id !== id)
    total.value = Math.max(0, total.value - 1)
  }

  return {
    items,
    total,
    page,
    pageSize,
    loading,
    fetchList,
    create,
    update,
    remove,
  }
})
