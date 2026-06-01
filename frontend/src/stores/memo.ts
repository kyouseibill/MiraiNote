import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {
  Memo,
  MemoSection,
  CreateMemoPayload,
  UpdateMemoPayload,
  PatchMemoStatusPayload,
  MemoListQuery,
} from '@/types/memo'
import { memoApi } from '@/api/memo'

/**
 * 备忘 Store。工作 / 生活共用，通过 section 参数区分两个独立缓存。
 */
export const useMemoStore = defineStore('memo', () => {
  // 按 section 分桶
  const buckets = ref<Record<MemoSection, Memo[]>>({ work: [], life: [] })
  const totals = ref<Record<MemoSection, number>>({ work: 0, life: 0 })
  const loading = ref(false)

  function list(section: MemoSection): Memo[] {
    return buckets.value[section]
  }

  async function fetchList(query: MemoListQuery) {
    loading.value = true
    try {
      const q: MemoListQuery = {
        page: 1,
        pageSize: 200,
        includeArchived: false,
        includeDone: true,
        ...query,
      }
      const result = await memoApi.list(q)
      buckets.value[query.section] = result.items
      totals.value[query.section] = result.total
      return result
    } finally {
      loading.value = false
    }
  }

  async function create(payload: CreateMemoPayload) {
    const created = await memoApi.create(payload)
    buckets.value[payload.section] = [created, ...buckets.value[payload.section]]
    totals.value[payload.section] += 1
    return created
  }

  async function update(section: MemoSection, id: number, payload: UpdateMemoPayload) {
    const updated = await memoApi.update(id, payload)
    replaceItem(section, updated)
    return updated
  }

  async function patchStatus(section: MemoSection, id: number, payload: PatchMemoStatusPayload) {
    const updated = await memoApi.patchStatus(id, payload)
    // 归档后从列表移除（默认不展示归档）
    if (updated.isArchived) {
      buckets.value[section] = buckets.value[section].filter((m) => m.id !== id)
    } else {
      replaceItem(section, updated)
    }
    return updated
  }

  async function remove(section: MemoSection, id: number) {
    await memoApi.remove(id)
    buckets.value[section] = buckets.value[section].filter((m) => m.id !== id)
    totals.value[section] = Math.max(0, totals.value[section] - 1)
  }

  function replaceItem(section: MemoSection, updated: Memo) {
    const arr = buckets.value[section]
    const idx = arr.findIndex((m) => m.id === updated.id)
    if (idx >= 0) arr[idx] = updated
  }

  return {
    buckets,
    totals,
    loading,
    list,
    fetchList,
    create,
    update,
    patchStatus,
    remove,
  }
})
