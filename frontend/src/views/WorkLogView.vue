<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useWorkLogStore } from '@/stores/workLog'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'
import type { WorkLog, CreateWorkLogPayload, WorkLogStatus } from '@/types/workLog'

const store = useWorkLogStore()
const toast = useToast()

const keyword = ref('')
const dateFrom = ref('')
const dateTo = ref('')
const filterCategory = ref('')
const filterStatus = ref<WorkLogStatus | null>(null)

const drawerOpen = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)
// 抽屉内 编辑/预览 切换
const previewMode = ref(false)

const form = reactive<CreateWorkLogPayload>({
  title: '',
  purpose: '',
  content: '',
  tags: '',
  category: '',
  logDate: todayStr(),
  status: 0,
  statusRemark: '',
})

const isEdit = computed(() => editingId.value !== null)
const totalPages = computed(() => Math.ceil(store.total / store.pageSize))

// 智能页码列表（超过7页时显示省略号）
const pageNumbers = computed<(number | '...')[]>(() => {
  const total = totalPages.value
  const current = store.page
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const pages: (number | '...')[] = [1]
  if (current > 3) pages.push('...')
  for (let p = Math.max(2, current - 1); p <= Math.min(total - 1, current + 1); p++) pages.push(p)
  if (current < total - 2) pages.push('...')
  pages.push(total)
  return pages
})

// 每页条数选项
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100]
const selectedPageSize = ref(store.pageSize)

// 当前选中条目展开详情
const expandedId = ref<number | null>(null)

function todayStr(): string {
  const d = new Date()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${m}-${day}`
}

function fmtDate(iso: string): string {
  if (!iso) return ''
  return iso.slice(0, 10)
}

// ===== 日期快捷选项 =====
function setDateRange(range: 'today' | 'week' | 'month' | 'all') {
  const now = new Date()
  const fmt = (d: Date) => {
    const m = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    return `${d.getFullYear()}-${m}-${day}`
  }
  if (range === 'today') {
    dateFrom.value = fmt(now)
    dateTo.value = fmt(now)
  } else if (range === 'week') {
    const mon = new Date(now)
    mon.setDate(now.getDate() - ((now.getDay() + 6) % 7))
    dateFrom.value = fmt(mon)
    dateTo.value = fmt(now)
  } else if (range === 'month') {
    dateFrom.value = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`
    dateTo.value = fmt(now)
  } else {
    dateFrom.value = ''
    dateTo.value = ''
  }
  load(1)
}

const STATUS_LABELS: Record<number, string> = { 0: '未标记', 1: '进行中', 2: '已完成', 3: '已延期' }
const STATUS_COLORS: Record<number, string> = {
  0: 'bg-gray-100 text-gray-500',
  1: 'bg-blue-50 text-blue-600',
  2: 'bg-green-50 text-green-600',
  3: 'bg-orange-50 text-orange-600',
}

async function load(page = 1, size?: number) {
  try {
    await store.fetchList({
      page,
      pageSize: size ?? selectedPageSize.value,
      keyword: keyword.value || undefined,
      dateFrom: dateFrom.value || undefined,
      dateTo: dateTo.value || undefined,
      category: filterCategory.value || undefined,
      status: filterStatus.value ?? undefined,
    })
  } catch {
    // 错误已由拦截器处理
  }
}

async function changePageSize(e: Event) {
  const size = Number((e.target as HTMLSelectElement).value)
  selectedPageSize.value = size
  await load(1, size)
}

function resetForm() {
  editingId.value = null
  previewMode.value = false
  form.title = ''
  form.purpose = ''
  form.content = ''
  form.tags = ''
  form.category = ''
  form.logDate = todayStr()
  form.status = 0
  form.statusRemark = ''
}

function openCreate() {
  resetForm()
  drawerOpen.value = true
}

function openEdit(item: WorkLog) {
  editingId.value = item.id
  previewMode.value = false
  form.title = item.title
  form.purpose = item.purpose ?? ''
  form.content = item.content ?? ''
  form.tags = item.tags ?? ''
  form.category = item.category ?? ''
  form.logDate = fmtDate(item.logDate)
  form.status = item.status ?? 0
  form.statusRemark = item.statusRemark ?? ''
  drawerOpen.value = true
}

function toggleExpand(item: WorkLog) {
  expandedId.value = expandedId.value === item.id ? null : item.id
}

async function copyToClipboard(item: WorkLog) {
  const lines: string[] = []
  lines.push(`## ${item.title}`)
  if (item.category) lines.push(`分类：${item.category}`)
  lines.push(`日期：${fmtDate(item.logDate)}`)
  if (item.purpose) lines.push(`\n目的：${item.purpose}`)
  if (item.content) lines.push(`\n${item.content}`)
  if (item.tags) lines.push(`\n标签：${item.tags}`)
  try {
    await navigator.clipboard.writeText(lines.join('\n'))
    toast.success('已复制到剪贴板')
  } catch {
    toast.error('复制失败，请手动选取')
  }
}

async function submit() {
  if (!form.title.trim()) {
    toast.error('请填写标题')
    return
  }
  submitting.value = true
  try {
    const payload: CreateWorkLogPayload = {
      title: form.title.trim(),
      purpose: form.purpose?.trim() || null,
      content: form.content?.trim() || null,
      tags: form.tags?.trim() || null,
      category: form.category?.trim() || null,
      logDate: form.logDate,
      status: form.status ?? 0,
      statusRemark: form.statusRemark?.trim() || null,
    }
    if (editingId.value !== null) {
      await store.update(editingId.value, payload)
      toast.success('已更新')
    } else {
      await store.create(payload)
      toast.success('已创建')
    }
    drawerOpen.value = false
    resetForm()
  } catch {
    // 拦截器已 toast
  } finally {
    submitting.value = false
  }
}

async function remove(item: WorkLog) {
  if (!confirm(`确定删除「${item.title}」？`)) return
  try {
    await store.remove(item.id)
    toast.success('已删除')
  } catch {
    // ignore
  }
}

function tagList(s: string | null): string[] {
  if (!s) return []
  return s.split(',').map((t) => t.trim()).filter(Boolean)
}

onMounted(() => {
  load(1)
  store.fetchCategories()
})
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
    <!-- 顶部操作栏 -->
    <div class="surface-card mb-5 flex flex-wrap items-end gap-3 p-4 sm:p-5">
      <div class="flex-1 min-w-[200px]">
        <label class="block text-xs text-gray-500 mb-1">关键字</label>
        <input
          v-model="keyword"
          type="text"
          placeholder="搜索标题 / 内容 / 标签"
          class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          @keyup.enter="load(1)"
        />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">分类</label>
        <select
          v-model="filterCategory"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm bg-white"
          @change="load(1)"
        >
          <option value="">全部分类</option>
          <option v-for="cat in store.categories" :key="cat" :value="cat">{{ cat }}</option>
        </select>
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">状态</label>
        <select
          v-model="filterStatus"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm bg-white"
          @change="load(1)"
        >
          <option :value="null">全部</option>
          <option :value="1">进行中</option>
          <option :value="2">已完成</option>
          <option :value="3">已延期</option>
          <option :value="0">未标记</option>
        </select>
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">日期快捷</label>
        <div class="flex gap-1">
          <button class="h-9 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-teal-50 hover:border-teal-300 hover:text-teal-600 transition" @click="setDateRange('today')">今天</button>
          <button class="h-9 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-teal-50 hover:border-teal-300 hover:text-teal-600 transition" @click="setDateRange('week')">本周</button>
          <button class="h-9 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-teal-50 hover:border-teal-300 hover:text-teal-600 transition" @click="setDateRange('month')">本月</button>
          <button class="h-9 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-gray-100 transition" @click="setDateRange('all')">全部</button>
        </div>
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">起始日期</label>
        <input v-model="dateFrom" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" @change="load(1)" />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">结束日期</label>
        <input v-model="dateTo" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" @change="load(1)" />
      </div>
      <button
        class="h-9 px-4 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 shadow-sm"
        @click="openCreate"
      >
        + 新建记录
      </button>
    </div>

    <!-- 数量提示 -->
    <p v-if="!store.loading && store.total > 0" class="text-xs text-gray-400 mb-2">
      共 {{ store.total }} 条记录，当前显示第 {{ (store.page - 1) * store.pageSize + 1 }}–{{ Math.min(store.page * store.pageSize, store.total) }} 条
    </p>

    <!-- 列表 -->
    <div class="surface-card overflow-hidden">
      <div v-if="store.loading" class="p-10 text-center text-gray-400 text-sm">加载中…</div>
      <div v-else-if="store.items.length === 0" class="p-10 text-center text-gray-400 text-sm">
        暂无记录，点击右上角「新建记录」开始
      </div>
      <ul v-else class="divide-y divide-gray-100">
        <li
          v-for="item in store.items"
          :key="item.id"
          class="hover:bg-gray-50 transition"
        >
          <!-- 摘要行 -->
          <div
            class="p-4 cursor-pointer flex items-start justify-between gap-3"
            @click="toggleExpand(item)"
          >
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2 flex-wrap">
                <span class="text-xs text-gray-400 font-mono shrink-0">{{ fmtDate(item.logDate) }}</span>
                <span v-if="item.category" class="text-xs px-1.5 py-0.5 rounded bg-teal-50 text-teal-600 shrink-0">
                  {{ item.category }}
                </span>
                <span
                  v-if="item.status !== 0"
                  class="text-xs px-1.5 py-0.5 rounded shrink-0"
                  :class="STATUS_COLORS[item.status]"
                >
                  {{ STATUS_LABELS[item.status] }}
                </span>
                <span
                  v-if="item.statusRemark"
                  class="text-xs text-gray-500 shrink-0"
                >
                  {{ item.statusRemark }}
                </span>
                <h3 class="font-medium text-gray-900 truncate">{{ item.title }}</h3>
              </div>
              <p v-if="item.purpose" class="mt-1 text-xs text-gray-500 line-clamp-1">目的：{{ item.purpose }}</p>
              <!-- 折叠时显示纯文本摘要 -->
              <p
                v-if="expandedId !== item.id && item.content"
                class="mt-1 text-sm text-gray-500 line-clamp-2"
              >
                {{ item.content }}
              </p>
              <div v-if="tagList(item.tags).length" class="mt-2 flex flex-wrap gap-1">
                <span
                  v-for="t in tagList(item.tags)"
                  :key="t"
                  class="text-xs px-1.5 py-0.5 rounded bg-gray-100 text-gray-500"
                >
                  #{{ t }}
                </span>
              </div>
            </div>
            <div class="flex items-center gap-1 shrink-0">
              <button
                class="text-xs text-gray-400 hover:text-gray-600 px-2 py-1"
                title="复制到剪贴板"
                @click.stop="copyToClipboard(item)"
              >
                复制
              </button>
              <button
                class="text-xs text-teal-500 hover:text-teal-700 px-2 py-1"
                @click.stop="openEdit(item)"
              >
                编辑
              </button>
              <button
                class="text-xs text-red-400 hover:text-red-600 px-2 py-1"
                @click.stop="remove(item)"
              >
                删除
              </button>
              <span class="text-xs text-gray-300 ml-1">{{ expandedId === item.id ? '▲' : '▼' }}</span>
            </div>
          </div>

          <!-- 展开：Markdown 渲染内容 -->
          <div v-if="expandedId === item.id && item.content" class="px-4 pb-4">
            <div
              class="prose prose-sm max-w-none text-gray-700 bg-gray-50 rounded-lg p-4 border border-gray-100"
              v-html="renderMarkdown(item.content)"
            />
          </div>
        </li>
      </ul>
    </div>

    <!-- 分页 -->
    <div v-if="store.total > 0" class="mt-4 flex items-center justify-between gap-2">
      <!-- 每页条数 -->
      <div class="flex items-center gap-2 text-xs text-gray-500">
        <span>每页</span>
        <select
          :value="selectedPageSize"
          class="h-7 px-1.5 rounded border border-gray-200 text-xs bg-white"
          @change="changePageSize"
        >
          <option v-for="s in PAGE_SIZE_OPTIONS" :key="s" :value="s">{{ s }} 条</option>
        </select>
      </div>
      <!-- 页码 -->
      <div v-if="totalPages > 1" class="flex items-center gap-1">
        <button
          :disabled="store.page <= 1"
          class="px-3 py-1.5 text-sm rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
          @click="load(store.page - 1)"
        >
          上一页
        </button>
        <template v-for="(p, idx) in pageNumbers" :key="idx">
          <span v-if="p === '...'" class="px-2 text-gray-400 text-sm">…</span>
          <button
            v-else
            class="w-8 h-8 text-sm rounded-md transition"
            :class="p === store.page
              ? 'bg-teal-600 text-white'
              : 'border border-gray-200 text-gray-600 hover:bg-gray-50'"
            @click="load(p)"
          >
            {{ p }}
          </button>
        </template>
        <button
          :disabled="store.page >= totalPages"
          class="px-3 py-1.5 text-sm rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
          @click="load(store.page + 1)"
        >
          下一页
        </button>
      </div>
    </div>
  </div>

  <!-- 抽屉：新增 / 编辑 -->
  <Teleport to="body">
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-50 bg-black/30 flex justify-end"
    >
      <div class="w-full max-w-xl h-full bg-white shadow-xl flex flex-col">
        <header class="h-14 px-5 border-b border-gray-100 flex items-center justify-between">
          <h3 class="font-semibold text-gray-900">{{ isEdit ? '编辑工作记录' : '新建工作记录' }}</h3>
          <button class="text-gray-400 hover:text-gray-600" @click="drawerOpen = false">✕</button>
        </header>

        <div class="flex-1 overflow-y-auto p-5 space-y-4">
          <div>
            <label class="block text-sm text-gray-700 mb-1">标题 <span class="text-red-500">*</span></label>
            <input v-model="form.title" type="text" maxlength="200" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-sm text-gray-700 mb-1">日期 <span class="text-red-500">*</span></label>
              <input v-model="form.logDate" type="date" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
            </div>
            <div>
              <label class="block text-sm text-gray-700 mb-1">分类</label>
              <input
                v-model="form.category"
                type="text"
                list="category-suggestions"
                placeholder="如：开发 / 会议"
                class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm"
              />
              <datalist id="category-suggestions">
                <option v-for="cat in store.categories" :key="cat" :value="cat" />
              </datalist>
            </div>
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">工作状态</label>
            <div class="flex gap-2">
              <button
                v-for="(label, val) in STATUS_LABELS"
                :key="val"
                type="button"
                class="h-8 px-3 rounded-md text-xs border transition"
                :class="form.status === Number(val)
                  ? STATUS_COLORS[Number(val)] + ' border-current font-medium'
                  : 'border-gray-200 text-gray-500 hover:bg-gray-50'"
                @click="form.status = Number(val) as WorkLogStatus"
              >
                {{ label }}
              </button>
            </div>
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">
              状态备注
              <span class="text-xs text-gray-400 font-normal ml-1">（可选，如：计划下周完成）</span>
            </label>
            <input
              v-model="form.statusRemark"
              type="text"
              maxlength="500"
              placeholder="例如：进行中，还差排水、环卫等条线数据未统计"
              class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
            />
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">目的</label>
            <input v-model="form.purpose" type="text" maxlength="500" placeholder="本项工作的目的" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>

          <!-- 内容：编辑/预览 切换 -->
          <div>
            <div class="flex items-center justify-between mb-1">
              <label class="text-sm text-gray-700">内容（支持 Markdown）</label>
              <div class="flex rounded-md overflow-hidden border border-gray-200 text-xs">
                <button
                  class="px-2 py-0.5 transition"
                  :class="!previewMode ? 'bg-teal-600 text-white' : 'text-gray-500 hover:bg-gray-50'"
                  @click="previewMode = false"
                >
                  编辑
                </button>
                <button
                  class="px-2 py-0.5 transition"
                  :class="previewMode ? 'bg-teal-600 text-white' : 'text-gray-500 hover:bg-gray-50'"
                  @click="previewMode = true"
                >
                  预览
                </button>
              </div>
            </div>
            <textarea
              v-if="!previewMode"
              v-model="form.content"
              rows="12"
              class="w-full p-3 rounded-md border border-gray-200 text-sm font-mono leading-6 focus:outline-none focus:ring-2 focus:ring-teal-200"
              placeholder="支持 Markdown 语法，如 **粗体**、# 标题、- 列表…"
            />
            <div
              v-else
              class="min-h-[12rem] p-3 rounded-md border border-gray-200 bg-gray-50 prose prose-sm max-w-none text-gray-700"
              v-html="renderMarkdown(form.content)"
            />
          </div>

          <div>
            <label class="block text-sm text-gray-700 mb-1">标签（逗号分隔）</label>
            <input v-model="form.tags" type="text" placeholder="例如：项目A, 紧急" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>
        </div>

        <footer class="h-14 px-5 border-t border-gray-100 flex items-center justify-end gap-2">
          <button class="h-9 px-4 rounded-md text-gray-700 hover:bg-gray-100" @click="drawerOpen = false">取消</button>
          <button
            class="h-9 px-4 rounded-md bg-teal-600 text-white hover:bg-teal-700 disabled:opacity-60"
            :disabled="submitting"
            @click="submit"
          >
            {{ submitting ? '保存中…' : '保存' }}
          </button>
        </footer>
      </div>
    </div>
  </Teleport>
</template>
