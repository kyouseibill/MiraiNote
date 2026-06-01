import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {
  WorkLog,
  CreateWorkLogPayload,
  UpdateWorkLogPayload,
  WorkLogListQuery,
} from '@/types/workLog'
import { workLogApi } from '@/api/workLog'

export const useWorkLogStore = defineStore('workLog', () => {
  const items = ref<WorkLog[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(20)
  const loading = ref(false)
  const categories = ref<string[]>([])

  async function fetchList(query: WorkLogListQuery = {}) {
    loading.value = true
    try {
      const q: WorkLogListQuery = {
        page: query.page ?? page.value,
        pageSize: query.pageSize ?? pageSize.value,
        ...query,
      }
      const result = await workLogApi.list(q)
      items.value = result.items
      total.value = result.total
      page.value = result.page
      pageSize.value = result.pageSize
      return result
    } finally {
      loading.value = false
    }
  }

  async function fetchCategories() {
    try {
      categories.value = await workLogApi.getCategories()
    } catch {
      // 静默：分类仅用于辅助提示
    }
  }

  async function create(payload: CreateWorkLogPayload) {
    const created = await workLogApi.create(payload)
    await fetchList({ page: 1 })
    // 刷新分类列表
    void fetchCategories()
    return created
  }

  async function update(id: number, payload: UpdateWorkLogPayload) {
    const updated = await workLogApi.update(id, payload)
    const idx = items.value.findIndex((w) => w.id === id)
    if (idx >= 0) items.value[idx] = updated
    void fetchCategories()
    return updated
  }

  async function remove(id: number) {
    await workLogApi.remove(id)
    items.value = items.value.filter((w) => w.id !== id)
    total.value = Math.max(0, total.value - 1)
  }

  return {
    items,
    total,
    page,
    pageSize,
    loading,
    categories,
    fetchList,
    fetchCategories,
    create,
    update,
    remove,
  }
})

