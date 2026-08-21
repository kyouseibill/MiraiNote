<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useWeeklyReportStore } from '@/stores/weeklyReport'
import { useToast } from '@/composables/useToast'

const store = useWeeklyReportStore()
const toast = useToast()

const activeTab = ref<'reports' | 'references'>('reports')
const selectedReportId = ref<number | null>(null)
const editingContent = ref('')
const isEditing = ref(false)

// 生成周报参数
const weekStart = ref('')
const weekEnd = ref('')
// 输出内容复杂度：1=简洁，2=标准，3=详细
const detailLevel = ref(2)
const detailLevelOptions = [
  { value: 1, label: '简洁' },
  { value: 2, label: '标准' },
  { value: 3, label: '详细' },
]

// 上传参考文件
const refFile = ref<File | null>(null)
const refWeekStart = ref('')
const refWeekEnd = ref('')
const refRemark = ref('')
const uploading = ref(false)

const selectedReport = computed(() =>
  store.reports.find((r) => r.id === selectedReportId.value) ?? null
)

// 将 Date 对象格式化为本地日期字符串（不转 UTC），避免跨日偏移
function fmtLocalDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function getWeekRange(): { start: string; end: string } {
  const now = new Date()
  const day = now.getDay() || 7
  const monday = new Date(now)
  monday.setDate(now.getDate() - day + 1)
  const friday = new Date(monday)
  friday.setDate(monday.getDate() + 4)
  return {
    start: fmtLocalDate(monday),
    end: fmtLocalDate(friday),
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
    const report = await store.generate({
      weekStart: weekStart.value,
      weekEnd: weekEnd.value,
      detailLevel: detailLevel.value,
    })
    selectedReportId.value = report.id
    editingContent.value = report.content
    isEditing.value = false
    toast.success('周报生成成功')
  } catch (e) {
    // 流式请求不走 axios 拦截器，这里手动 toast（含"正在生成中"等业务错误）
    toast.error(e instanceof Error && e.message ? e.message : '周报生成失败')
  }
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
  await store.fetchReferences()
})
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
    <!-- Tab 切换 -->
    <div class="mb-6 inline-flex gap-1 rounded-xl border border-slate-200 bg-white p-1 shadow-sm">
      <button
        class="rounded-lg px-4 py-2 text-sm font-medium transition"
        :class="activeTab === 'reports' ? 'bg-slate-900 text-white shadow-sm' : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800'"
        @click="activeTab = 'reports'"
      >
        周报列表
      </button>
      <button
        class="rounded-lg px-4 py-2 text-sm font-medium transition"
        :class="activeTab === 'references' ? 'bg-slate-900 text-white shadow-sm' : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800'"
        @click="activeTab = 'references'"
      >
        参考文件 ({{ store.references.length }})
      </button>
    </div>

    <!-- 周报 Tab -->
    <div v-if="activeTab === 'reports'" class="flex flex-col gap-6 lg:flex-row">
        <!-- 左侧：生成表单 -->
        <div class="w-full shrink-0 space-y-4 lg:w-72">
          <div class="surface-card p-4 space-y-3">
            <h3 class="text-sm font-medium text-gray-700">生成周报</h3>
            <div>
              <label class="block text-xs text-gray-500 mb-1">开始日期</label>
              <input v-model="weekStart" type="date" class="w-full h-8 px-2 text-sm rounded-md border border-gray-200" />
            </div>
            <div>
              <label class="block text-xs text-gray-500 mb-1">结束日期</label>
              <input v-model="weekEnd" type="date" class="w-full h-8 px-2 text-sm rounded-md border border-gray-200" />
            </div>
            <div>
              <label class="block text-xs text-gray-500 mb-1">输出内容复杂度</label>
              <div class="flex rounded-md border border-gray-200 overflow-hidden">
                <button
                  v-for="opt in detailLevelOptions"
                  :key="opt.value"
                  type="button"
                  class="flex-1 h-8 text-xs transition"
                  :class="detailLevel === opt.value
                    ? 'bg-teal-600 text-white'
                    : 'bg-white text-gray-500 hover:bg-gray-50'"
                  @click="detailLevel = opt.value"
                >
                  {{ opt.label }}
                </button>
              </div>
            </div>
            <button
              class="w-full h-9 rounded-lg bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-50"
              :disabled="store.generating"
              @click="generate"
            >
              {{ store.generating ? 'AI 生成中…' : '✨ 一键生成' }}
            </button>
          </div>
        </div>

      <!-- 右侧：周报预览/编辑 -->
      <div class="flex-1 min-w-0">
        <!-- 生成中：流式预览 -->
        <div v-if="store.generating" class="surface-card h-full flex flex-col">
          <div class="flex items-center justify-between px-6 py-4 border-b">
            <span class="font-medium text-gray-800">AI 生成中…</span>
            <span class="text-xs text-gray-400">生成完成后自动保存</span>
          </div>
          <div class="flex-1 overflow-y-auto p-6">
            <pre class="text-sm text-gray-700 leading-relaxed whitespace-pre-wrap font-sans">{{ store.streamingContent || '正在等待 AI 输出…' }}</pre>
          </div>
        </div>
        <div v-else-if="!selectedReport" class="text-center text-gray-400 text-sm py-20">
          点击「一键生成」开始生成周报
        </div>
        <div v-else class="surface-card h-full flex flex-col">
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
                class="h-8 px-3 text-sm rounded bg-teal-50 text-teal-700 hover:bg-teal-100"
                @click="isEditing = true"
              >
                编辑
              </button>
              <button
                v-else
                class="h-8 px-3 text-sm rounded bg-teal-600 text-white hover:bg-teal-700"
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
            <!-- 编辑模式：原始 Markdown 文本 -->
            <textarea
              v-if="isEditing"
              v-model="editingContent"
              class="w-full h-full min-h-[400px] text-sm font-mono border border-gray-200 rounded-lg p-3 resize-none focus:outline-none focus:ring-2 focus:ring-teal-200"
            />
            <!-- 阅读模式：纯文本显示 -->
            <pre
              v-else
              class="text-sm text-gray-700 leading-relaxed whitespace-pre-wrap font-sans"
            >{{ selectedReport.content }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- 参考文件 Tab -->
    <div v-if="activeTab === 'references'" class="max-w-2xl">
      <div class="surface-card p-6 mb-6">
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
            class="h-9 px-4 rounded-lg bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-50"
            :disabled="uploading"
            @click="uploadRef"
          >
            {{ uploading ? '上传中…' : '上传' }}
          </button>
        </div>
      </div>

      <div class="surface-card overflow-hidden">
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
                <span v-if="ref.remark" class="mr-2 text-teal-600">{{ ref.remark }}</span>
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
