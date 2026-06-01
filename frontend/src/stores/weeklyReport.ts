import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {
  WeeklyReport,
  WeeklyReportReference,
  GenerateReportPayload,
  UpdateReportPayload,
} from '@/types/weeklyReport'
import { weeklyReportApi } from '@/api/weeklyReport'

export const useWeeklyReportStore = defineStore('weeklyReport', () => {
  const reports = ref<WeeklyReport[]>([])
  const references = ref<WeeklyReportReference[]>([])
  const currentReport = ref<WeeklyReport | null>(null)
  const loading = ref(false)
  const generating = ref(false)

  async function fetchList() {
    loading.value = true
    try {
      reports.value = await weeklyReportApi.list()
    } finally {
      loading.value = false
    }
  }

  async function generate(payload: GenerateReportPayload) {
    generating.value = true
    try {
      const report = await weeklyReportApi.generate(payload)
      currentReport.value = report
      // 刷新列表
      const idx = reports.value.findIndex((r) => r.id === report.id)
      if (idx >= 0) reports.value[idx] = report
      else reports.value.unshift(report)
      return report
    } finally {
      generating.value = false
    }
  }

  async function saveEdit(id: number, payload: UpdateReportPayload) {
    const updated = await weeklyReportApi.update(id, payload)
    if (currentReport.value?.id === id) currentReport.value = updated
    const idx = reports.value.findIndex((r) => r.id === id)
    if (idx >= 0) reports.value[idx] = updated
    return updated
  }

  async function remove(id: number) {
    await weeklyReportApi.remove(id)
    reports.value = reports.value.filter((r) => r.id !== id)
    if (currentReport.value?.id === id) currentReport.value = null
  }

  async function fetchReferences() {
    references.value = await weeklyReportApi.listReferences()
  }

  async function uploadReference(
    file: File,
    weekStart?: string,
    weekEnd?: string,
    remark?: string,
  ) {
    const ref_ = await weeklyReportApi.uploadReference(file, weekStart, weekEnd, remark)
    references.value.unshift(ref_)
    return ref_
  }

  async function deleteReference(id: number) {
    await weeklyReportApi.deleteReference(id)
    references.value = references.value.filter((r) => r.id !== id)
  }

  return {
    reports,
    references,
    currentReport,
    loading,
    generating,
    fetchList,
    generate,
    saveEdit,
    remove,
    fetchReferences,
    uploadReference,
    deleteReference,
  }
})
