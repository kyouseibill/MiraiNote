<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import { useToast } from '@/composables/useToast'
import { apiFailure } from '@/utils/apiError'
import {
  welcomePhraseApi,
  type WelcomePhrase,
  type WelcomePhraseWrite,
} from '@/api/welcomePhrases'

const toast = useToast()
const loading = ref(true)
const saving = ref(false)
const rows = ref<WelcomePhrase[]>([])
const kindFilter = ref('')
const editingId = ref<number | null>(null)
const showDelete = ref(false)
const pendingDelete = ref<WelcomePhrase | null>(null)
const error = ref('')

const form = reactive<WelcomePhraseWrite>({
  kind: 'greeting',
  text: '',
  author: '',
  source: '',
  period: '',
  special: '',
  season: '',
  isEnabled: true,
  sortOrder: 0,
})

const kindOptions = [
  { value: 'greeting', label: '问候' },
  { value: 'poem', label: '诗词' },
  { value: 'quote', label: '句子' },
]
const periodOptions = [
  { value: '', label: '不限时段' },
  { value: 'morning', label: '早晨' },
  { value: 'noon', label: '中午' },
  { value: 'afternoon', label: '下午' },
  { value: 'evening', label: '晚上' },
  { value: 'latenight', label: '深夜' },
]
const specialOptions = [
  { value: '', label: '无' },
  { value: 'rain', label: '下雨' },
  { value: 'friday', label: '周五' },
]
const seasonOptions = [
  { value: '', label: '不限季节' },
  { value: 'spring', label: '春' },
  { value: 'summer', label: '夏' },
  { value: 'autumn', label: '秋' },
  { value: 'winter', label: '冬' },
]

const heading = computed(() => editingId.value == null ? '新建文案' : `编辑 #${editingId.value}`)

function labelOf(options: { value: string; label: string }[], value: string | null) {
  return options.find((item) => item.value === (value ?? ''))?.label ?? value ?? ''
}

function resetForm() {
  editingId.value = null
  form.kind = 'greeting'
  form.text = ''
  form.author = ''
  form.source = ''
  form.period = ''
  form.special = ''
  form.season = ''
  form.isEnabled = true
  form.sortOrder = 0
  error.value = ''
}

function edit(row: WelcomePhrase) {
  editingId.value = row.id
  form.kind = row.kind
  form.text = row.text
  form.author = row.author ?? ''
  form.source = row.source ?? ''
  form.period = row.period ?? ''
  form.special = row.special ?? ''
  form.season = row.season ?? ''
  form.isEnabled = row.isEnabled
  form.sortOrder = row.sortOrder
  error.value = ''
}

async function load() {
  loading.value = true
  try {
    rows.value = await welcomePhraseApi.list(kindFilter.value || undefined)
    error.value = ''
  } catch (cause) {
    error.value = apiFailure(cause, '列表加载失败').message
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!form.text.trim()) {
    error.value = '请填写正文'
    return
  }
  saving.value = true
  try {
    const payload: WelcomePhraseWrite = {
      ...form,
      text: form.text.trim(),
      author: form.author.trim(),
      source: form.source.trim(),
      sortOrder: Number(form.sortOrder) || 0,
    }
    if (editingId.value == null) await welcomePhraseApi.create(payload)
    else await welcomePhraseApi.update(editingId.value, payload)
    toast.success(editingId.value == null ? '已添加' : '已保存')
    resetForm()
    await load()
  } catch (cause) {
    error.value = apiFailure(cause, '保存失败').message
  } finally {
    saving.value = false
  }
}

async function toggle(row: WelcomePhrase) {
  try {
    await welcomePhraseApi.setEnabled(row.id, !row.isEnabled)
    await load()
  } catch (cause) {
    toast.error(apiFailure(cause, '状态更新失败').message)
  }
}

function askDelete(row: WelcomePhrase) {
  pendingDelete.value = row
  showDelete.value = true
}

async function confirmDelete() {
  const row = pendingDelete.value
  if (!row) return
  saving.value = true
  try {
    await welcomePhraseApi.remove(row.id)
    toast.success('已删除')
    if (editingId.value === row.id) resetForm()
    showDelete.value = false
    pendingDelete.value = null
    await load()
  } catch (cause) {
    toast.error(apiFailure(cause, '删除失败').message)
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-6 sm:px-6 lg:px-8 lg:py-8 space-y-5">
    <section class="surface-card p-4 sm:p-5">
      <div class="flex items-start justify-between gap-3">
        <div>
          <h1 class="font-semibold text-gray-900">{{ heading }}</h1>
          <p class="mt-1 text-sm text-gray-500">问候里写 {name} 会换成称呼。停用或删除后，工作台马上不再使用。</p>
        </div>
        <button
          v-if="editingId != null"
          type="button"
          class="h-9 px-3 rounded-md border border-gray-200 text-sm text-gray-600 hover:bg-gray-50"
          @click="resetForm"
        >
          取消编辑
        </button>
      </div>
      <form class="mt-4 grid gap-3 sm:grid-cols-2" data-testid="welcome-phrase-form" @submit.prevent="save">
        <label class="block text-sm text-gray-700">
          类型
          <select v-model="form.kind" class="mt-1 w-full h-9 px-2 rounded-md border border-gray-200 bg-white text-sm">
            <option v-for="item in kindOptions" :key="item.value" :value="item.value">{{ item.label }}</option>
          </select>
        </label>
        <label class="block text-sm text-gray-700">
          排序
          <input v-model.number="form.sortOrder" type="number" class="mt-1 w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
        </label>
        <label class="block text-sm text-gray-700 sm:col-span-2">
          正文
          <textarea v-model="form.text" rows="2" class="mt-1 w-full px-3 py-2 rounded-md border border-gray-200 text-sm" data-testid="welcome-phrase-text" />
        </label>
        <label class="block text-sm text-gray-700">
          作者
          <input v-model="form.author" type="text" class="mt-1 w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
        </label>
        <label class="block text-sm text-gray-700">
          出处
          <input v-model="form.source" type="text" class="mt-1 w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
        </label>
        <label class="block text-sm text-gray-700">
          时段
          <select v-model="form.period" class="mt-1 w-full h-9 px-2 rounded-md border border-gray-200 bg-white text-sm">
            <option v-for="item in periodOptions" :key="item.value" :value="item.value">{{ item.label }}</option>
          </select>
        </label>
        <label class="block text-sm text-gray-700">
          特殊
          <select v-model="form.special" class="mt-1 w-full h-9 px-2 rounded-md border border-gray-200 bg-white text-sm">
            <option v-for="item in specialOptions" :key="item.value" :value="item.value">{{ item.label }}</option>
          </select>
        </label>
        <label class="block text-sm text-gray-700">
          季节
          <select v-model="form.season" class="mt-1 w-full h-9 px-2 rounded-md border border-gray-200 bg-white text-sm">
            <option v-for="item in seasonOptions" :key="item.value" :value="item.value">{{ item.label }}</option>
          </select>
        </label>
        <label class="flex items-center gap-2 text-sm text-gray-700 sm:mt-6">
          <input v-model="form.isEnabled" type="checkbox" />
          启用
        </label>
        <p v-if="error" class="sm:col-span-2 text-sm text-red-600">{{ error }}</p>
        <div class="sm:col-span-2">
          <button
            type="submit"
            class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60"
            :disabled="saving"
          >
            {{ saving ? '保存中…' : '保存' }}
          </button>
        </div>
      </form>
    </section>

    <div class="flex flex-wrap gap-2">
      <button
        v-for="item in [{ value: '', label: '全部' }, ...kindOptions]"
        :key="item.value"
        type="button"
        class="h-8 px-3 rounded-full border text-xs transition"
        :class="kindFilter === item.value ? 'border-teal-600 bg-teal-50 text-teal-700' : 'border-gray-200 text-gray-600 hover:bg-gray-50'"
        @click="kindFilter = item.value; load()"
      >
        {{ item.label }}
      </button>
    </div>

    <section class="surface-card overflow-hidden">
      <div v-if="loading" class="p-10 text-center text-sm text-gray-400">加载中…</div>
      <div v-else-if="rows.length === 0" class="p-10 text-center text-sm text-gray-400">还没有文案</div>
      <ul v-else class="divide-y divide-gray-100">
        <li v-for="row in rows" :key="row.id" class="p-4 flex items-start justify-between gap-3" :class="row.isEnabled ? '' : 'opacity-60'">
          <div class="min-w-0">
            <p class="text-sm text-gray-900">{{ row.text }}</p>
            <p class="mt-1 text-xs text-gray-500">
              {{ labelOf(kindOptions, row.kind) }}
              <template v-if="row.author"> · {{ row.author }}</template>
              <template v-if="row.source">《{{ row.source }}》</template>
              <template v-if="row.period"> · {{ labelOf(periodOptions, row.period) }}</template>
              <template v-if="row.special"> · {{ labelOf(specialOptions, row.special) }}</template>
              <template v-if="row.season"> · {{ labelOf(seasonOptions, row.season) }}</template>
              <template v-if="!row.isEnabled"> · 已停用</template>
            </p>
          </div>
          <div class="flex shrink-0 gap-2">
            <button type="button" class="h-8 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-gray-50" @click="toggle(row)">
              {{ row.isEnabled ? '停用' : '启用' }}
            </button>
            <button type="button" class="h-8 px-2.5 rounded-md border border-gray-200 text-xs text-gray-600 hover:bg-gray-50" @click="edit(row)">
              编辑
            </button>
            <button type="button" class="h-8 px-2.5 rounded-md border border-red-200 text-xs text-red-600 hover:bg-red-50" @click="askDelete(row)">
              删除
            </button>
          </div>
        </li>
      </ul>
    </section>

    <AppDialog :open="showDelete" title="删除这条文案？" description="删除后工作台马上不再使用。" :busy="saving" @close="showDelete = false">
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-gray-200 px-4 text-sm" :disabled="saving" @click="showDelete = false">取消</button>
        <button type="button" class="h-9 rounded-md bg-red-600 px-4 text-sm text-white disabled:opacity-60" :disabled="saving" @click="confirmDelete">删除</button>
      </template>
    </AppDialog>
  </div>
</template>
