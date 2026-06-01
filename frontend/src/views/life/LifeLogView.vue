<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useLifeLogStore } from '@/stores/lifeLog'
import { lifeLogApi } from '@/api/lifeLog'
import { useToast } from '@/composables/useToast'
import type { LifeLog, CreateLifeLogPayload } from '@/types/lifeLog'

const store = useLifeLogStore()
const toast = useToast()

const keyword = ref('')
const selectedMood = ref('')
const selectedMonth = ref('')

const drawerOpen = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)
const imageUploading = ref(false)

const MOODS = ['开心', '平静', '疲惫', '难过', '兴奋', '焦虑', '感恩']

const form = reactive<CreateLifeLogPayload>({
  content: '',
  mood: '',
  imagePath: null,
  logDate: todayStr(),
})

const isEdit = computed(() => editingId.value !== null)

function todayStr(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function currentMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

function fmtDate(iso: string): string {
  return iso ? iso.slice(0, 10) : ''
}

function moodEmoji(mood: string | null): string {
  const map: Record<string, string> = {
    开心: '😊', 平静: '😌', 疲惫: '😪', 难过: '😢',
    兴奋: '🤩', 焦虑: '😰', 感恩: '🙏',
  }
  return mood ? (map[mood] ?? '😶') : ''
}

async function load() {
  try {
    await store.fetchList({
      page: 1,
      keyword: keyword.value || undefined,
      mood: selectedMood.value || undefined,
      month: selectedMonth.value || undefined,
    })
  } catch {
    // 错误已由拦截器 toast
  }
}

function resetForm() {
  editingId.value = null
  form.content = ''
  form.mood = ''
  form.imagePath = null
  form.logDate = todayStr()
}

function openCreate() {
  resetForm()
  drawerOpen.value = true
}

function openEdit(item: LifeLog) {
  editingId.value = item.id
  form.content = item.content
  form.mood = item.mood ?? ''
  form.imagePath = item.imagePath
  form.logDate = fmtDate(item.logDate)
  drawerOpen.value = true
}

async function handleImageUpload(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  imageUploading.value = true
  try {
    const path = await lifeLogApi.uploadImage(file)
    form.imagePath = path
  } catch {
    // 拦截器已 toast
  } finally {
    imageUploading.value = false
  }
}

async function submit() {
  if (!form.content.trim()) {
    toast.error('请填写内容')
    return
  }
  submitting.value = true
  try {
    const payload: CreateLifeLogPayload = {
      content: form.content.trim(),
      mood: form.mood?.trim() || null,
      imagePath: form.imagePath || null,
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
  } catch {
    // 拦截器已 toast
  } finally {
    submitting.value = false
  }
}

async function remove(item: LifeLog) {
  if (!confirm(`确定删除这条生活记录？`)) return
  try {
    await store.remove(item.id)
    toast.success('已删除')
  } catch {
    // ignore
  }
}

onMounted(() => {
  selectedMonth.value = currentMonth()
  load()
})
</script>

<template>
  <div class="max-w-3xl mx-auto px-6 py-6">
    <!-- 操作栏 -->
    <div class="flex flex-wrap items-end gap-3 mb-6">
      <div class="flex-1 min-w-[180px]">
        <label class="block text-xs text-gray-500 mb-1">关键字</label>
        <input
          v-model="keyword"
          type="text"
          placeholder="搜索内容"
          class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-rose-200"
          @keyup.enter="load"
        />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">心情</label>
        <select
          v-model="selectedMood"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm"
          @change="load"
        >
          <option value="">全部</option>
          <option v-for="m in MOODS" :key="m" :value="m">{{ m }}</option>
        </select>
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">月份</label>
        <input
          v-model="selectedMonth"
          type="month"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm"
          @change="load"
        />
      </div>
      <button
        class="h-9 px-4 rounded-md bg-rose-500 text-white text-sm hover:bg-rose-600 shadow-sm"
        @click="openCreate"
      >
        + 新建记录
      </button>
    </div>

    <!-- 时间轴列表 -->
    <div v-if="store.loading" class="text-center text-gray-400 text-sm py-10">加载中…</div>
    <div v-else-if="store.items.length === 0" class="text-center text-gray-400 text-sm py-10">
      暂无记录，点击右上角开始记录生活点滴
    </div>
    <div v-else class="space-y-4">
      <div
        v-for="item in store.items"
        :key="item.id"
        class="bg-white rounded-xl border border-gray-100 shadow-sm p-5 hover:shadow-md transition cursor-pointer"
        @click="openEdit(item)"
      >
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2 mb-2">
              <span class="text-xs text-gray-400 font-mono">{{ fmtDate(item.logDate) }}</span>
              <span v-if="item.mood" class="text-sm">
                {{ moodEmoji(item.mood) }} {{ item.mood }}
              </span>
            </div>
            <p class="text-gray-800 text-sm whitespace-pre-wrap line-clamp-3">{{ item.content }}</p>
            <img
              v-if="item.imagePath"
              :src="item.imagePath"
              class="mt-3 rounded-lg max-h-48 object-cover"
              alt="生活照片"
            />
          </div>
          <button
            class="shrink-0 text-xs text-red-400 hover:text-red-600 px-2 py-1"
            @click.stop="remove(item)"
          >
            删除
          </button>
        </div>
      </div>
    </div>

    <!-- 分页 -->
    <div v-if="store.total > store.pageSize" class="mt-6 flex justify-center gap-2">
      <button
        v-if="store.page > 1"
        class="px-3 py-1 text-sm rounded border border-gray-200 hover:bg-gray-50"
        @click="store.fetchList({ page: store.page - 1 })"
      >
        上一页
      </button>
      <span class="px-3 py-1 text-sm text-gray-500">
        {{ store.page }} / {{ Math.ceil(store.total / store.pageSize) }}
      </span>
      <button
        v-if="store.page < Math.ceil(store.total / store.pageSize)"
        class="px-3 py-1 text-sm rounded border border-gray-200 hover:bg-gray-50"
        @click="store.fetchList({ page: store.page + 1 })"
      >
        下一页
      </button>
    </div>
  </div>

  <!-- 侧边抽屉 -->
  <Teleport to="body">
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-50 flex"
      @click.self="drawerOpen = false"
    >
      <div class="absolute inset-0 bg-black/30" @click="drawerOpen = false" />
      <div class="relative ml-auto w-full max-w-md bg-white h-full shadow-xl flex flex-col overflow-hidden">
        <div class="flex items-center justify-between px-6 py-4 border-b">
          <h2 class="font-semibold text-gray-800">{{ isEdit ? '编辑生活记录' : '新建生活记录' }}</h2>
          <button class="text-gray-400 hover:text-gray-600" @click="drawerOpen = false">✕</button>
        </div>
        <div class="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">记录日期</label>
            <input v-model="form.logDate" type="date" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">心情</label>
            <div class="flex flex-wrap gap-2">
              <button
                v-for="m in MOODS"
                :key="m"
                type="button"
                class="px-3 py-1 rounded-full text-sm border transition"
                :class="form.mood === m
                  ? 'bg-rose-500 text-white border-rose-500'
                  : 'bg-white text-gray-600 border-gray-200 hover:border-rose-300'"
                @click="form.mood = form.mood === m ? '' : m"
              >
                {{ moodEmoji(m) }} {{ m }}
              </button>
            </div>
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">内容 <span class="text-red-500">*</span></label>
            <textarea
              v-model="form.content"
              rows="6"
              placeholder="记录今天的点滴…"
              class="w-full px-3 py-2 rounded-md border border-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-rose-200"
            />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">图片</label>
            <input
              type="file"
              accept="image/*"
              class="block text-sm text-gray-500"
              @change="handleImageUpload"
            />
            <div v-if="imageUploading" class="text-xs text-gray-400 mt-1">上传中…</div>
            <img
              v-if="form.imagePath"
              :src="form.imagePath"
              class="mt-2 rounded-lg max-h-40 object-cover"
              alt="预览"
            />
          </div>
        </div>
        <div class="px-6 py-4 border-t flex gap-3">
          <button
            class="flex-1 h-10 rounded-lg bg-rose-500 text-white text-sm hover:bg-rose-600 disabled:opacity-50"
            :disabled="submitting"
            @click="submit"
          >
            {{ submitting ? '保存中…' : (isEdit ? '保存修改' : '创建') }}
          </button>
          <button
            class="h-10 px-4 rounded-lg border border-gray-200 text-sm text-gray-600 hover:bg-gray-50"
            @click="drawerOpen = false"
          >
            取消
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
