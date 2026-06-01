<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useWeeklyReportStore } from '@/stores/weeklyReport'
import { useToast } from '@/composables/useToast'
import type { WeeklyReport } from '@/types/weeklyReport'

const store = useWeeklyReportStore()
const toast = useToast()

const activeTab = ref<'reports' | 'references'>('reports')
const selectedReportId = ref<number | null>(null)
const editingContent = ref('')
const isEditing = ref(false)

// 生成周报参数
const weekStart = ref('')
const weekEnd = ref('')

// 上传参考文件
const refFile = ref<File | null>(null)
const refWeekStart = ref('')
const refWeekEnd = ref('')
const refRemark = ref('')
const uploading = ref(false)

const selectedReport = computed(() =>
  store.reports.find((r) => r.id === selectedReportId.value) ?? null
)

function getWeekRange(): { start: string; end: string } {
  const now = new Date()
  const day = now.getDay() || 7
  const monday = new Date(now)
  monday.setDate(now.getDate() - day + 1)
  const sunday = new Date(monday)
  sunday.setDate(monday.getDate() + 6)
  return {
    start: fmtDate(monday.toISOString()),
    end: fmtDate(sunday.toISOString()),
  }
}

function fmtDate(iso: string): string {
  return iso ? iso.slice(0, 10) : ''
}

async function generate() {
  if (!weekStart.value || !weekEnd.value) {
    toast.error('请选择周次范围')
    return
  }
  try {
    const report = await store.generate({ weekStart: weekStart.value, weekEnd: weekEnd.value })
    selectedReportId.value = report.id
    editingContent.value = report.content
    isEditing.value = false
    toast.success('周报生成成功')
  } catch {
    // 拦截器已 toast
  }
}

function selectReport(report: WeeklyReport) {
  selectedReportId.value = report.id
  editingContent.value = report.content
  isEditing.value = false
}

async function saveEdit() {
  if (!selectedReportId.value) return
  try {
    await store.saveEdit(selectedReportId.value, { content: editingContent.value })
    isEditing.value = false
    toast.success('已保存')
  } catch {
    // ignore
  }
}

async function removeReport(id: number) {
  if (!confirm('确定删除这份周报？')) return
  await store.remove(id)
  if (selectedReportId.value === id) {
    selectedReportId.value = null
    editingContent.value = ''
  }
  toast.success('已删除')
}

function handleRefFileChange(e: Event) {
  refFile.value = (e.target as HTMLInputElement).files?.[0] ?? null
}

async function uploadRef() {
  if (!refFile.value) {
    toast.error('请选择文件')
    return
  }
  uploading.value = true
  try {
    await store.uploadReference(
      refFile.value,
      refWeekStart.value || undefined,
      refWeekEnd.value || undefined,
      refRemark.value || undefined,
    )
    refFile.value = null
    refWeekStart.value = ''
    refWeekEnd.value = ''
    refRemark.value = ''
    toast.success('上传成功')
  } catch {
    // ignore
  } finally {
    uploading.value = false
  }
}

async function deleteRef(id: number) {
  if (!confirm('确定删除此参考文件？')) return
  await store.deleteReference(id)
  toast.success('已删除')
}

function copyContent() {
  if (!editingContent.value) return
  navigator.clipboard.writeText(editingContent.value)
  toast.success('已复制到剪贴板')
}

onMounted(async () => {
  const { start, end } = getWeekRange()
  weekStart.value = start
  weekEnd.value = end
  await Promise.all([store.fetchList(), store.fetchReferences()])
  if (store.reports.length > 0) selectReport(store.reports[0])
})
</script>

<template>
  <div class="max-w-6xl mx-auto px-6 py-6">
    <!-- Tab 切换 -->
    <div class="flex gap-4 mb-6 border-b border-gray-200">
      <button
        class="pb-2 text-sm font-medium border-b-2 transition"
        :class="activeTab === 'reports' ? 'border-indigo-500 text-indigo-600' : 'border-transparent text-gray-500 hover:text-gray-700'"
        @click="activeTab = 'reports'"
      >
        周报列表
      </button>
      <button
        class="pb-2 text-sm font-medium border-b-2 transition"
        :class="activeTab === 'references' ? 'border-indigo-500 text-indigo-600' : 'border-transparent text-gray-500 hover:text-gray-700'"
        @click="activeTab = 'references'"
      >
        参考文件 ({{ store.references.length }})
      </button>
    </div>

    <!-- 周报 Tab -->
    <div v-if="activeTab === 'reports'" class="flex gap-6">
      <!-- 左侧：生成 + 历史列表 -->
      <div class="w-64 shrink-0 space-y-4">
        <div class="bg-white rounded-xl border border-gray-100 shadow-sm p-4 space-y-3">
          <h3 class="text-sm font-medium text-gray-700">生成周报</h3>
          <div>
            <label class="block text-xs text-gray-500 mb-1">开始日期</label>
            <input v-model="weekStart" type="date" class="w-full h-8 px-2 text-sm rounded-md border border-gray-200" />
          </div>
          <div>
            <label class="block text-xs text-gray-500 mb-1">结束日期</label>
            <input v-model="weekEnd" type="date" class="w-full h-8 px-2 text-sm rounded-md border border-gray-200" />
          </div>
          <button
            class="w-full h-9 rounded-lg bg-indigo-600 text-white text-sm hover:bg-indigo-700 disabled:opacity-50"
            :disabled="store.generating"
            @click="generate"
          >
            {{ store.generating ? 'AI 生成中…' : '✨ 一键生成' }}
          </button>
        </div>

        <div class="bg-white rounded-xl border border-gray-100 shadow-sm overflow-hidden">
          <div class="px-4 py-3 border-b text-sm font-medium text-gray-700">历史周报</div>
          <div v-if="store.reports.length === 0" class="px-4 py-4 text-xs text-gray-400">暂无周报</div>
          <ul class="divide-y divide-gray-100">
            <li
              v-for="r in store.reports"
              :key="r.id"
              class="px-4 py-3 cursor-pointer hover:bg-gray-50 transition"
              :class="{ 'bg-indigo-50': selectedReportId === r.id }"
              @click="selectReport(r)"
            >
              <div class="text-xs font-medium text-gray-800">
                {{ fmtDate(r.weekStart) }} ~ {{ fmtDate(r.weekEnd) }}
              </div>
              <div class="flex items-center gap-1 mt-0.5">
                <span v-if="r.isEdited" class="text-xs text-amber-500">已编辑</span>
                <span class="text-xs text-gray-400">{{ fmtDate(r.generatedAt) }}</span>
              </div>
            </li>
          </ul>
        </div>
      </div>

      <!-- 右侧：周报预览/编辑 -->
      <div class="flex-1 min-w-0">
        <div v-if="!selectedReport" class="text-center text-gray-400 text-sm py-20">
          选择左侧周报查看内容，或点击「一键生成」创建新周报
        </div>
        <div v-else class="bg-white rounded-xl border border-gray-100 shadow-sm h-full flex flex-col">
          <div class="flex items-center justify-between px-6 py-4 border-b gap-3 flex-wrap">
            <div>
              <span class="font-medium text-gray-800">
                {{ fmtDate(selectedReport.weekStart) }} ~ {{ fmtDate(selectedReport.weekEnd) }}
              </span>
              <span v-if="selectedReport.isEdited" class="ml-2 text-xs text-amber-500">已手动编辑</span>
            </div>
            <div class="flex gap-2">
              <button
                class="h-8 px-3 text-sm rounded border border-gray-200 hover:bg-gray-50"
                @click="copyContent"
              >
                复制
              </button>
              <button
                v-if="!isEditing"
                class="h-8 px-3 text-sm rounded bg-indigo-50 text-indigo-700 hover:bg-indigo-100"
                @click="isEditing = true"
              >
                编辑
              </button>
              <button
                v-else
                class="h-8 px-3 text-sm rounded bg-indigo-600 text-white hover:bg-indigo-700"
                @click="saveEdit"
              >
                保存
              </button>
              <button
                class="h-8 px-3 text-sm rounded border border-red-200 text-red-500 hover:bg-red-50"
                @click="removeReport(selectedReport.id)"
              >
                删除
              </button>
            </div>
          </div>
          <div class="flex-1 overflow-y-auto p-6">
            <textarea
              v-if="isEditing"
              v-model="editingContent"
              class="w-full h-full min-h-[400px] text-sm font-mono border border-gray-200 rounded-lg p-3 resize-none focus:outline-none focus:ring-2 focus:ring-indigo-200"
            />
            <pre v-else class="text-sm text-gray-700 whitespace-pre-wrap leading-relaxed font-sans">{{ selectedReport.content }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- 参考文件 Tab -->
    <div v-if="activeTab === 'references'" class="max-w-2xl">
      <div class="bg-white rounded-xl border border-gray-100 shadow-sm p-6 mb-6">
        <h3 class="text-sm font-medium text-gray-700 mb-4">上传参考文件（.xlsx / .xls）</h3>
        <div class="space-y-3">
          <div>
            <input type="file" accept=".xlsx,.xls" @change="handleRefFileChange" class="text-sm text-gray-600" />
          </div>
          <div class="flex gap-3">
            <div>
              <label class="block text-xs text-gray-500 mb-1">对应周次起始</label>
              <input v-model="refWeekStart" type="date" class="h-8 px-2 text-sm rounded-md border border-gray-200" />
            </div>
            <div>
              <label class="block text-xs text-gray-500 mb-1">对应周次结束</label>
              <input v-model="refWeekEnd" type="date" class="h-8 px-2 text-sm rounded-md border border-gray-200" />
            </div>
          </div>
          <div>
            <label class="block text-xs text-gray-500 mb-1">备注</label>
            <input
              v-model="refRemark"
              type="text"
              placeholder="如：2025年Q4模板"
              class="w-full h-8 px-3 text-sm rounded-md border border-gray-200"
            />
          </div>
          <button
            class="h-9 px-4 rounded-lg bg-indigo-600 text-white text-sm hover:bg-indigo-700 disabled:opacity-50"
            :disabled="uploading"
            @click="uploadRef"
          >
            {{ uploading ? '上传中…' : '上传' }}
          </button>
        </div>
      </div>

      <div class="bg-white rounded-xl border border-gray-100 shadow-sm overflow-hidden">
        <div v-if="store.references.length === 0" class="p-6 text-sm text-gray-400 text-center">
          暂无参考文件，上传后 AI 生成周报时将自动参考格式和历史内容
        </div>
        <ul v-else class="divide-y divide-gray-100">
          <li
            v-for="ref in store.references"
            :key="ref.id"
            class="flex items-center justify-between px-5 py-4"
          >
            <div>
              <div class="text-sm font-medium text-gray-800">{{ ref.fileName }}</div>
              <div class="text-xs text-gray-400 mt-0.5">
                <span v-if="ref.remark" class="mr-2 text-indigo-600">{{ ref.remark }}</span>
                <span v-if="ref.weekStart">{{ fmtDate(ref.weekStart) }} ~ {{ fmtDate(ref.weekEnd ?? '') }}</span>
                <span v-else>上传于 {{ fmtDate(ref.createdAt) }}</span>
              </div>
            </div>
            <button
              class="text-xs text-red-400 hover:text-red-600 px-2 py-1"
              @click="deleteRef(ref.id)"
            >
              删除
            </button>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>
