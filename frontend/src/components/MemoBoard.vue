<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { useMemoStore } from '@/stores/memo'
import { useToast } from '@/composables/useToast'
import type { Memo, MemoSection, CreateMemoPayload, UpdateMemoPayload } from '@/types/memo'

const props = defineProps<{
  section: MemoSection
  /** 主题色：teal（工作）/ rose（生活） */
  accent: 'teal' | 'rose'
  title: string
}>()

const store = useMemoStore()
const toast = useToast()

const includeDone = ref(true)
const includeArchived = ref(false)
const keyword = ref('')

// 新建表单
const newForm = reactive({
  content: '',
  priority: 2,
  remindLocal: '',
  popup: false,
  email: false,
})
const showRemind = ref(false)
const submitting = ref(false)

// 编辑表单
const editingId = ref<number | null>(null)
const editForm = reactive({
  content: '',
  priority: 2,
  isPinned: false,
  remindLocal: '',
  popup: false,
  email: false,
})

const items = computed(() => store.list(props.section))

const accentClasses = computed(() => {
  if (props.accent === 'rose') {
    return {
      btn: 'bg-rose-600 hover:bg-rose-700',
      ring: 'focus:ring-rose-200',
      pin: 'text-rose-600',
      link: 'text-rose-600 hover:text-rose-700',
    }
  }
  return {
    btn: 'bg-teal-600 hover:bg-teal-700',
    ring: 'focus:ring-teal-200',
    pin: 'text-teal-600',
    link: 'text-teal-600 hover:text-teal-700',
  }
})

// ===== 工具 =====

/** 后端 UTC ISO → datetime-local 输入 */
function isoToLocalInput(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** datetime-local → UTC ISO */
function localInputToIso(local: string): string | null {
  if (!local) return null
  const d = new Date(local)
  if (isNaN(d.getTime())) return null
  return d.toISOString()
}

function fmt(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function methodsFromFlags(popup: boolean, email: boolean): number {
  return (popup ? 1 : 0) | (email ? 2 : 0)
}

function isOverdue(iso: string | null): boolean {
  if (!iso) return false
  return new Date(iso).getTime() <= Date.now()
}

function priorityLabel(p: number): string {
  return p === 3 ? '高' : p === 1 ? '低' : '中'
}

function priorityColor(p: number): string {
  return p === 3 ? 'bg-red-50 text-red-600' : p === 1 ? 'bg-gray-100 text-gray-500' : 'bg-amber-50 text-amber-700'
}

// ===== 加载 / 创建 / 状态 / 编辑 =====

async function load() {
  try {
    await store.fetchList({
      section: props.section,
      includeDone: includeDone.value,
      includeArchived: includeArchived.value,
      keyword: keyword.value || undefined,
    })
  } catch { /* */ }
}

function resetNew() {
  newForm.content = ''
  newForm.priority = 2
  newForm.remindLocal = ''
  newForm.popup = false
  newForm.email = false
  showRemind.value = false
}

async function create() {
  const content = newForm.content.trim()
  if (!content) {
    toast.error('请输入内容')
    return
  }
  const remindAt = showRemind.value ? localInputToIso(newForm.remindLocal) : null
  const methods = showRemind.value ? methodsFromFlags(newForm.popup, newForm.email) : 0
  if (showRemind.value && methods !== 0 && !remindAt) {
    toast.error('请选择提醒时间')
    return
  }
  submitting.value = true
  try {
    const payload: CreateMemoPayload = {
      section: props.section,
      content,
      priority: newForm.priority,
      remindAt,
      remindMethods: methods,
    }
    await store.create(payload)
    resetNew()
  } catch { /* */ } finally {
    submitting.value = false
  }
}

async function toggleDone(item: Memo) {
  try {
    await store.patchStatus(props.section, item.id, { isDone: !item.isDone })
  } catch {
    toast.error('操作失败，请稍后重试')
  }
}
async function togglePin(item: Memo) {
  try {
    await store.patchStatus(props.section, item.id, { isPinned: !item.isPinned })
  } catch {
    toast.error('操作失败，请稍后重试')
  }
}
async function archive(item: Memo) {
  if (!confirm('归档后将从列表中移除，确认？')) return
  try {
    await store.patchStatus(props.section, item.id, { isArchived: true })
    toast.success('已归档')
  } catch {
    toast.error('归档失败，请稍后重试')
  }
}
async function remove(item: Memo) {
  if (!confirm('确定删除该备忘？')) return
  try {
    await store.remove(props.section, item.id)
    toast.success('已删除')
  } catch {
    toast.error('删除失败，请稍后重试')
  }
}

function startEdit(item: Memo) {
  editingId.value = item.id
  editForm.content = item.content
  editForm.priority = item.priority
  editForm.isPinned = item.isPinned
  editForm.remindLocal = isoToLocalInput(item.remindAt)
  editForm.popup = (item.remindMethods & 1) === 1
  editForm.email = (item.remindMethods & 2) === 2
}

async function saveEdit(item: Memo) {
  const content = editForm.content.trim()
  if (!content) {
    toast.error('内容不能为空')
    return
  }
  const hasRemind = !!editForm.remindLocal
  const remindAt = hasRemind ? localInputToIso(editForm.remindLocal) : null
  const methods = hasRemind ? methodsFromFlags(editForm.popup, editForm.email) : 0
  try {
    const payload: UpdateMemoPayload = {
      content,
      priority: editForm.priority,
      isPinned: editForm.isPinned,
      remindAt,
      remindMethods: methods,
    }
    await store.update(props.section, item.id, payload)
    editingId.value = null
  } catch { /* */ }
}

onMounted(load)
watch([includeDone, includeArchived], load)

// 实时搜索：keyword 变化后 300ms 防抖触发
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(keyword, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => load(), 300)
})
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6 sm:px-6 lg:py-8">
    <!-- 新建条 -->
    <div class="surface-card mb-5 p-4 sm:p-5">
      <textarea
        v-model="newForm.content"
        :placeholder="`记一条${title}…（Ctrl+Enter 保存）`"
        rows="2"
        class="w-full p-2 text-sm rounded-md border border-gray-200 focus:outline-none focus:ring-2"
        :class="accentClasses.ring"
        @keydown.ctrl.enter="create"
      />
      <div class="mt-2 flex items-center justify-between flex-wrap gap-2">
        <div class="flex items-center gap-3 text-sm">
          <label class="text-gray-500">优先级</label>
          <select v-model.number="newForm.priority" class="h-8 px-2 rounded-md border border-gray-200 text-sm">
            <option :value="1">低</option>
            <option :value="2">中</option>
            <option :value="3">高</option>
          </select>
          <button
            type="button"
            class="text-xs"
            :class="accentClasses.link"
            @click="showRemind = !showRemind"
          >
            {{ showRemind ? '取消提醒' : '⏰ 添加提醒' }}
          </button>
        </div>
        <button
          class="h-9 px-4 rounded-md text-white text-sm disabled:opacity-60"
          :class="accentClasses.btn"
          :disabled="submitting"
          @click="create"
        >
          {{ submitting ? '保存中…' : '添加' }}
        </button>
      </div>

      <div v-if="showRemind" class="mt-3 pt-3 border-t border-gray-100 grid grid-cols-1 sm:grid-cols-2 gap-3 text-sm">
        <div>
          <label class="block text-xs text-gray-500 mb-1">提醒时间</label>
          <input
            v-model="newForm.remindLocal"
            type="datetime-local"
            class="w-full h-8 px-2 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2"
            :class="accentClasses.ring"
          />
        </div>
        <div>
          <label class="block text-xs text-gray-500 mb-1">提醒方式</label>
          <div class="flex items-center gap-4 h-8 text-sm">
            <label class="flex items-center gap-1 cursor-pointer">
              <input v-model="newForm.popup" type="checkbox" /> 弹窗
            </label>
            <label class="flex items-center gap-1 cursor-pointer">
              <input v-model="newForm.email" type="checkbox" /> 邮件
            </label>
          </div>
        </div>
      </div>
    </div>

    <!-- 过滤栏 -->
    <div class="mb-3 flex flex-wrap items-center gap-3 rounded-xl border border-slate-200/80 bg-white/70 px-3 py-2.5 text-sm text-slate-600">
      <input
        v-model="keyword"
        type="text"
        placeholder="搜索内容…"
        class="h-8 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2"
        :class="accentClasses.ring"
        @keyup.enter="load"
      />
      <label class="flex items-center gap-1">
        <input v-model="includeDone" type="checkbox" /> 显示已完成
      </label>
      <label class="flex items-center gap-1">
        <input v-model="includeArchived" type="checkbox" /> 显示已归档
      </label>
      <span class="ml-auto text-xs text-gray-400">共 {{ items.length }} 条</span>
    </div>

    <!-- 列表 -->
    <div class="surface-card overflow-hidden">
      <div v-if="store.loading" class="p-10 text-center text-gray-400 text-sm">加载中…</div>
      <div v-else-if="items.length === 0" class="p-10 text-center text-gray-400 text-sm">
        还没有备忘，先记一条吧～
      </div>
      <ul v-else class="divide-y divide-gray-100">
        <li
          v-for="item in items" :key="item.id"
          class="p-4 group transition-colors"
          :class="item.isDone ? 'bg-green-50' : 'hover:bg-gray-50/40'"
        >
          <div class="flex items-start gap-3">
            <!-- 完成切换按钮 -->
            <button
              class="mt-0.5 w-5 h-5 shrink-0 rounded-full border-2 flex items-center justify-center transition-all"
              :class="item.isDone
                ? 'bg-green-500 border-green-500 text-white'
                : 'border-gray-300 hover:border-green-400 hover:bg-green-50'"
              :title="item.isDone ? '取消完成' : '标记为完成'"
              @click="toggleDone(item)"
            >
              <svg v-if="item.isDone" class="w-3 h-3" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <path d="M2 6l3 3 5-5"/>
              </svg>
            </button>
            <div class="flex-1 min-w-0">
              <!-- 编辑态 -->
              <div v-if="editingId === item.id" class="space-y-2">
                <textarea
                  v-model="editForm.content"
                  rows="3"
                  class="w-full p-2 text-sm rounded-md border border-gray-200 focus:outline-none focus:ring-2"
                  :class="accentClasses.ring"
                />
                <div class="flex items-center gap-3 text-sm">
                  <label class="text-gray-500 text-xs">优先级</label>
                  <select v-model.number="editForm.priority" class="h-8 px-2 rounded-md border border-gray-200 text-sm">
                    <option :value="1">低</option>
                    <option :value="2">中</option>
                    <option :value="3">高</option>
                  </select>
                  <label class="flex items-center gap-1 text-xs text-gray-600">
                    <input v-model="editForm.isPinned" type="checkbox" /> 置顶
                  </label>
                </div>
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
                  <div>
                    <label class="block text-xs text-gray-500 mb-1">提醒时间</label>
                    <input
                      v-model="editForm.remindLocal"
                      type="datetime-local"
                      class="w-full h-8 px-2 rounded-md border border-gray-200 text-sm"
                    />
                  </div>
                  <div>
                    <label class="block text-xs text-gray-500 mb-1">提醒方式</label>
                    <div class="flex items-center gap-4 h-8 text-xs">
                      <label class="flex items-center gap-1 cursor-pointer">
                        <input v-model="editForm.popup" type="checkbox" :disabled="!editForm.remindLocal" /> 弹窗
                      </label>
                      <label class="flex items-center gap-1 cursor-pointer">
                        <input v-model="editForm.email" type="checkbox" :disabled="!editForm.remindLocal" /> 邮件
                      </label>
                    </div>
                  </div>
                </div>
                <div class="flex gap-2">
                  <button class="h-8 px-3 rounded-md text-white text-xs" :class="accentClasses.btn" @click="saveEdit(item)">保存</button>
                  <button class="h-8 px-3 rounded-md text-gray-600 hover:bg-gray-100 text-xs" @click="editingId = null">取消</button>
                </div>
              </div>

              <!-- 显示态 -->
              <div v-else>
                <div class="flex items-center gap-2 flex-wrap">
                  <span v-if="item.isDone" class="text-xs px-1.5 py-0.5 rounded bg-green-100 text-green-700 font-medium">✓ 已完成</span>
                  <span v-if="item.isPinned" class="text-xs" :class="accentClasses.pin">📌</span>
                  <span class="text-xs px-1.5 py-0.5 rounded" :class="priorityColor(item.priority)">{{ priorityLabel(item.priority) }}</span>
                  <span
                    v-if="item.remindAt"
                    class="text-xs px-1.5 py-0.5 rounded inline-flex items-center gap-1"
                    :class="isOverdue(item.remindAt) && !item.isDone ? 'bg-red-50 text-red-600' : 'bg-gray-100 text-gray-600'"
                  >
                    ⏰ {{ fmt(item.remindAt) }}
                    <span v-if="(item.remindMethods & 1) === 1" class="opacity-70">弹</span>
                    <span v-if="(item.remindMethods & 2) === 2" class="opacity-70">邮</span>
                  </span>
                </div>
                <p
                  class="mt-1 text-sm whitespace-pre-wrap break-words"
                  :class="item.isDone ? 'text-gray-400 line-through' : 'text-gray-800'"
                >
                  {{ item.content }}
                </p>
              </div>
            </div>
            <div v-if="editingId !== item.id" class="shrink-0 flex items-center gap-1">
              <button
                class="text-xs px-2 py-1 rounded font-medium transition-colors"
                :class="item.isDone
                  ? 'text-green-700 bg-green-100 hover:bg-green-200'
                  : 'text-gray-500 hover:text-green-700 hover:bg-green-50'"
                @click="toggleDone(item)"
              >
                {{ item.isDone ? '取消完成' : '完成' }}
              </button>
              <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition">
                <button class="text-xs text-gray-500 hover:text-gray-800 px-1.5 py-1" @click="togglePin(item)">
                  {{ item.isPinned ? '取消置顶' : '置顶' }}
                </button>
                <button class="text-xs text-gray-500 hover:text-gray-800 px-1.5 py-1" @click="startEdit(item)">编辑</button>
                <button class="text-xs text-gray-500 hover:text-gray-800 px-1.5 py-1" @click="archive(item)">归档</button>
                <button class="text-xs text-red-500 hover:text-red-700 px-1.5 py-1" @click="remove(item)">删除</button>
              </div>
            </div>
          </div>
        </li>
      </ul>
    </div>
  </div>
</template>
