<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useWorkLogStore } from '@/stores/workLog'
import { useToast } from '@/composables/useToast'
import type { WorkLog, CreateWorkLogPayload } from '@/types/workLog'

const store = useWorkLogStore()
const toast = useToast()

const keyword = ref('')
const dateFrom = ref('')
const dateTo = ref('')

const drawerOpen = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)

const form = reactive<CreateWorkLogPayload>({
  title: '',
  purpose: '',
  content: '',
  tags: '',
  category: '',
  logDate: todayStr(),
})

const isEdit = computed(() => editingId.value !== null)

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

async function load() {
  try {
    await store.fetchList({
      page: 1,
      keyword: keyword.value || undefined,
      dateFrom: dateFrom.value || undefined,
      dateTo: dateTo.value || undefined,
    })
  } catch (e) {
    // 错误已由拦截器 toast
  }
}

function resetForm() {
  editingId.value = null
  form.title = ''
  form.purpose = ''
  form.content = ''
  form.tags = ''
  form.category = ''
  form.logDate = todayStr()
}

function openCreate() {
  resetForm()
  drawerOpen.value = true
}

function openEdit(item: WorkLog) {
  editingId.value = item.id
  form.title = item.title
  form.purpose = item.purpose ?? ''
  form.content = item.content ?? ''
  form.tags = item.tags ?? ''
  form.category = item.category ?? ''
  form.logDate = fmtDate(item.logDate)
  drawerOpen.value = true
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
  } catch (e) {
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

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto px-6 py-6">
    <!-- 顶部操作栏 -->
    <div class="flex flex-wrap items-end gap-3 mb-4">
      <div class="flex-1 min-w-[200px]">
        <label class="block text-xs text-gray-500 mb-1">关键字</label>
        <input
          v-model="keyword"
          type="text"
          placeholder="搜索标题 / 内容 / 标签"
          class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-200"
          @keyup.enter="load"
        />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">起始日期</label>
        <input v-model="dateFrom" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">结束日期</label>
        <input v-model="dateTo" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" />
      </div>
      <button
        class="h-9 px-4 rounded-md bg-gray-100 text-gray-700 text-sm hover:bg-gray-200"
        @click="load"
      >
        筛选
      </button>
      <button
        class="h-9 px-4 rounded-md bg-brand text-white text-sm hover:bg-indigo-700 shadow-sm"
        @click="openCreate"
      >
        + 新建记录
      </button>
    </div>

    <!-- 列表 -->
    <div class="bg-white border border-gray-100 rounded-xl shadow-sm overflow-hidden">
      <div v-if="store.loading" class="p-10 text-center text-gray-400 text-sm">加载中…</div>
      <div v-else-if="store.items.length === 0" class="p-10 text-center text-gray-400 text-sm">
        暂无记录，点击右上角「新建记录」开始
      </div>
      <ul v-else class="divide-y divide-gray-100">
        <li
          v-for="item in store.items"
          :key="item.id"
          class="p-4 hover:bg-gray-50 transition cursor-pointer"
          @click="openEdit(item)"
        >
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2 flex-wrap">
                <span class="text-xs text-gray-500 font-mono">{{ fmtDate(item.logDate) }}</span>
                <span v-if="item.category" class="text-xs px-1.5 py-0.5 rounded bg-indigo-50 text-indigo-700">
                  {{ item.category }}
                </span>
                <h3 class="font-medium text-gray-900 truncate">{{ item.title }}</h3>
              </div>
              <p v-if="item.purpose" class="mt-1 text-xs text-gray-500 line-clamp-1">目的：{{ item.purpose }}</p>
              <p v-if="item.content" class="mt-1 text-sm text-gray-600 line-clamp-2">{{ item.content }}</p>
              <div v-if="tagList(item.tags).length" class="mt-2 flex flex-wrap gap-1">
                <span
                  v-for="t in tagList(item.tags)"
                  :key="t"
                  class="text-xs px-1.5 py-0.5 rounded bg-gray-100 text-gray-600"
                >
                  #{{ t }}
                </span>
              </div>
            </div>
            <button
              class="shrink-0 text-xs text-red-500 hover:text-red-700 px-2 py-1"
              @click.stop="remove(item)"
            >
              删除
            </button>
          </div>
        </li>
      </ul>
    </div>

    <!-- 抽屉：新增 / 编辑 -->
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-40 bg-black/30 flex justify-end"
      @click.self="drawerOpen = false"
    >
      <div class="w-full max-w-xl h-full bg-white shadow-xl flex flex-col">
        <header class="h-14 px-5 border-b border-gray-100 flex items-center justify-between">
          <h3 class="font-semibold text-gray-900">{{ isEdit ? '编辑工作记录' : '新建工作记录' }}</h3>
          <button class="text-gray-400 hover:text-gray-600" @click="drawerOpen = false">✕</button>
        </header>

        <div class="flex-1 overflow-y-auto p-5 space-y-4">
          <div>
            <label class="block text-sm text-gray-700 mb-1">标题 <span class="text-red-500">*</span></label>
            <input v-model="form.title" type="text" maxlength="200" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-200" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-sm text-gray-700 mb-1">日期 <span class="text-red-500">*</span></label>
              <input v-model="form.logDate" type="date" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
            </div>
            <div>
              <label class="block text-sm text-gray-700 mb-1">分类</label>
              <input v-model="form.category" type="text" placeholder="如：开发 / 会议" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
            </div>
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">目的</label>
            <input v-model="form.purpose" type="text" maxlength="500" placeholder="本项工作的目的" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">内容（支持 Markdown）</label>
            <textarea v-model="form.content" rows="10" class="w-full p-3 rounded-md border border-gray-200 text-sm font-mono leading-6 focus:outline-none focus:ring-2 focus:ring-indigo-200" />
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">标签（逗号分隔）</label>
            <input v-model="form.tags" type="text" placeholder="例如：项目A, 紧急" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>
        </div>

        <footer class="h-14 px-5 border-t border-gray-100 flex items-center justify-end gap-2">
          <button class="h-9 px-4 rounded-md text-gray-700 hover:bg-gray-100" @click="drawerOpen = false">取消</button>
          <button
            class="h-9 px-4 rounded-md bg-brand text-white hover:bg-indigo-700 disabled:opacity-60"
            :disabled="submitting"
            @click="submit"
          >
            {{ submitting ? '保存中…' : '保存' }}
          </button>
        </footer>
      </div>
    </div>
  </div>
</template>
