<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useLifeLogStore } from '@/stores/lifeLog'
import { lifeLogApi } from '@/api/lifeLog'
import { useToast } from '@/composables/useToast'
import { staticUrl } from '@/composables/useStaticUrl'
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
const MAX_IMAGES = 9
const MAX_IMAGE_SIZE = 5 * 1024 * 1024

// 展开详情的记录 ID
const expandedId = ref<number | null>(null)

const MOODS = ['开心', '平静', '疲惫', '难过', '兴奋', '焦虑', '感恩']

const MOOD_COLOR: Record<string, string> = {
  开心: '#f59e0b', 平静: '#0ea5e9', 疲惫: '#94a3b8', 难过: '#64748b',
  兴奋: '#ec4899', 焦虑: '#ef4444', 感恩: '#10b981',
}

const form = reactive<CreateLifeLogPayload>({
  content: '',
  mood: '',
  imagePath: null,
  imagePaths: [],
  logDate: todayStr(),
})

const isEdit = computed(() => editingId.value !== null)
const totalPages = computed(() => Math.ceil(store.total / store.pageSize))

// 当月心情统计（基于已加载数据）
const moodStats = computed(() => {
  const counts: Record<string, number> = {}
  for (const item of store.items) {
    if (item.mood) counts[item.mood] = (counts[item.mood] ?? 0) + 1
  }
  const total = Object.values(counts).reduce((s, v) => s + v, 0)
  return MOODS
    .filter((m) => counts[m])
    .sort((a, b) => (counts[b] ?? 0) - (counts[a] ?? 0))
    .map((m) => ({
      mood: m,
      count: counts[m] ?? 0,
      pct: total > 0 ? Math.round(((counts[m] ?? 0) / total) * 100) : 0,
      color: MOOD_COLOR[m] ?? '#94a3b8',
    }))
})

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

async function load(page = 1) {
  try {
    await store.fetchList({
      page,
      keyword: keyword.value || undefined,
      mood: selectedMood.value || undefined,
      month: selectedMonth.value || undefined,
    })
  } catch {
    // 错误已由拦截器处理
  }
}

function resetForm() {
  editingId.value = null
  form.content = ''
  form.mood = ''
  form.imagePath = null
  form.imagePaths = []
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
  form.imagePaths = getImagePaths(item)
  form.imagePath = form.imagePaths[0] ?? null
  form.logDate = fmtDate(item.logDate)
  drawerOpen.value = true
}

function toggleExpand(id: number) {
  expandedId.value = expandedId.value === id ? null : id
}

async function handleImageUpload(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  if (files.length === 0) return

  const existingPaths = form.imagePaths ?? []
  const remaining = MAX_IMAGES - existingPaths.length
  if (files.length > remaining) {
    toast.error(`最多上传 ${MAX_IMAGES} 张图片，还可选择 ${remaining} 张`)
    return
  }

  const invalidType = files.find((file) => !file.type.startsWith('image/'))
  if (invalidType) {
    toast.error(`「${invalidType.name}」不是有效的图片文件`)
    return
  }

  const oversized = files.find((file) => file.size > MAX_IMAGE_SIZE)
  if (oversized) {
    toast.error(`「${oversized.name}」超过 5MB`)
    return
  }

  imageUploading.value = true
  const uploadedPaths: string[] = []
  try {
    for (const file of files) {
      try {
        uploadedPaths.push(await lifeLogApi.uploadImage(file))
      } catch {
        // 单张失败时继续上传其余图片，错误由拦截器提示。
      }
    }
    form.imagePaths = [...existingPaths, ...uploadedPaths]
    form.imagePath = form.imagePaths[0] ?? null
  } finally {
    imageUploading.value = false
  }
}

function removeImage(index: number) {
  form.imagePaths = (form.imagePaths ?? []).filter((_, currentIndex) => currentIndex !== index)
  form.imagePath = form.imagePaths[0] ?? null
}

function getImagePaths(item: Pick<LifeLog, 'imagePath' | 'imagePaths'>): string[] {
  if (item.imagePaths?.length) return item.imagePaths
  return item.imagePath ? [item.imagePath] : []
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
      imagePath: form.imagePaths?.[0] || null,
      imagePaths: form.imagePaths ?? [],
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
  if (!confirm('确定删除这条生活记录？')) return
  try {
    await store.remove(item.id)
    toast.success('已删除')
  } catch {
    // ignore
  }
}

onMounted(() => {
  selectedMonth.value = currentMonth()
  load(1)
})
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-6 sm:px-6 lg:py-8">
    <!-- 操作栏 -->
    <div class="flex flex-wrap items-end gap-3 mb-6">
      <div class="flex-1 min-w-[180px]">
        <label class="block text-xs text-gray-500 mb-1">关键字</label>
        <input
          v-model="keyword"
          type="text"
          placeholder="搜索内容"
          class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-rose-200"
          @keyup.enter="load(1)"
        />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">心情</label>
        <select
          v-model="selectedMood"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm bg-white"
          @change="load(1)"
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
          @change="load(1)"
        />
      </div>
      <button
        class="h-9 px-4 rounded-md bg-rose-500 text-white text-sm hover:bg-rose-600 shadow-sm"
        @click="openCreate"
      >
        + 新建记录
      </button>
    </div>

    <!-- 心情分布统计卡 -->
    <div
      v-if="moodStats.length >= 2"
      class="mb-5 surface-card p-4"
    >
      <p class="text-xs text-gray-500 mb-3">
        {{ selectedMonth || '当前筛选' }}心情分布（{{ store.items.filter(i => i.mood).length }} 条有记录）
      </p>
      <div class="flex h-3 rounded-full overflow-hidden gap-0.5 mb-3">
        <div
          v-for="s in moodStats"
          :key="s.mood"
          :style="{ width: s.pct + '%', backgroundColor: s.color }"
          :title="`${s.mood} ${s.pct}%`"
        />
      </div>
      <div class="flex flex-wrap gap-x-4 gap-y-1">
        <button
          v-for="s in moodStats"
          :key="s.mood"
          class="flex items-center gap-1 text-xs text-gray-600 hover:text-gray-900 transition"
          @click="selectedMood = selectedMood === s.mood ? '' : s.mood; load(1)"
        >
          <span class="inline-block w-2.5 h-2.5 rounded-full" :style="{ backgroundColor: s.color }" />
          {{ moodEmoji(s.mood) }} {{ s.mood }}
          <span class="text-gray-400">{{ s.pct }}% ({{ s.count }})</span>
        </button>
      </div>
    </div>

    <!-- 数量提示 -->
    <p v-if="!store.loading && store.total > 0" class="text-xs text-gray-400 mb-3">
      共 {{ store.total }} 条记录
    </p>

    <!-- 列表 -->
    <div v-if="store.loading" class="text-center text-gray-400 text-sm py-10">加载中…</div>
    <div v-else-if="store.items.length === 0" class="text-center text-gray-400 text-sm py-10">
      暂无记录，点击右上角开始记录生活点滴
    </div>
    <div v-else class="space-y-3">
      <div
        v-for="item in store.items"
        :key="item.id"
        class="surface-card hover:shadow-md transition"
      >
        <!-- 摘要行 -->
        <div class="p-4">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0 flex-1">
              <!-- 日期 + 心情 -->
              <div class="flex items-center gap-2 mb-2">
                <span class="text-xs text-gray-400 font-mono">{{ fmtDate(item.logDate) }}</span>
                <span v-if="item.mood" class="text-sm">
                  {{ moodEmoji(item.mood) }}
                  <span
                    class="text-xs px-1.5 py-0.5 rounded-full"
                    :style="{ backgroundColor: MOOD_COLOR[item.mood] + '22', color: MOOD_COLOR[item.mood] }"
                  >{{ item.mood }}</span>
                </span>
                <span v-if="getImagePaths(item).length" class="text-xs text-gray-400">
                  📷 {{ getImagePaths(item).length }}
                </span>
              </div>

              <!-- 内容 -->
              <p
                class="text-gray-800 text-sm whitespace-pre-wrap"
                :class="expandedId === item.id ? '' : 'line-clamp-3'"
              >
                {{ item.content }}
              </p>

              <!-- 展开后显示图片 -->
              <div
                v-if="expandedId === item.id && getImagePaths(item).length"
                class="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3"
              >
                <img
                  v-for="(path, imageIndex) in getImagePaths(item)"
                  :key="path"
                  :src="staticUrl(path)"
                  class="aspect-square w-full rounded-xl border border-slate-100 object-cover"
                  :alt="`生活照片 ${imageIndex + 1}`"
                  loading="lazy"
                />
              </div>
            </div>

            <!-- 操作按钮 -->
            <div class="flex items-center gap-1 shrink-0">
              <button
                class="text-xs text-gray-400 hover:text-gray-700 px-2 py-1"
                @click="toggleExpand(item.id)"
              >
                {{ expandedId === item.id ? '收起' : '展开' }}
              </button>
              <button
                class="text-xs text-rose-400 hover:text-rose-600 px-2 py-1"
                @click="openEdit(item)"
              >
                编辑
              </button>
              <button
                class="text-xs text-red-300 hover:text-red-500 px-2 py-1"
                @click="remove(item)"
              >
                删除
              </button>
            </div>
          </div>

          <!-- 折叠时有图片显示缩略图 -->
          <div v-if="expandedId !== item.id && getImagePaths(item).length" class="mt-3 flex gap-2 overflow-hidden">
            <img
              v-for="(path, imageIndex) in getImagePaths(item).slice(0, 4)"
              :key="path"
              :src="staticUrl(path)"
              class="h-20 w-20 cursor-pointer rounded-xl border border-slate-100 object-cover sm:h-24 sm:w-24"
              :alt="`生活照片 ${imageIndex + 1}`"
              loading="lazy"
              @click="toggleExpand(item.id)"
            />
            <button
              v-if="getImagePaths(item).length > 4"
              class="flex h-20 w-20 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-sm font-semibold text-slate-600 sm:h-24 sm:w-24"
              @click="toggleExpand(item.id)"
            >
              +{{ getImagePaths(item).length - 4 }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 分页 -->
    <div v-if="totalPages > 1" class="mt-6 flex flex-col items-center gap-2">
      <p class="text-xs text-gray-400">
        第 {{ (store.page - 1) * store.pageSize + 1 }}–{{ Math.min(store.page * store.pageSize, store.total) }} 条 / 共 {{ store.total }} 条
      </p>
      <div class="flex items-center gap-2">
      <button
        :disabled="store.page <= 1"
        class="px-3 py-1.5 text-sm rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
        @click="load(store.page - 1)"
      >
        上一页
      </button>
      <div class="flex gap-1">
        <button
          v-for="p in totalPages"
          :key="p"
          class="w-8 h-8 text-sm rounded-md transition"
          :class="p === store.page
            ? 'bg-rose-500 text-white'
            : 'border border-gray-200 text-gray-600 hover:bg-gray-50'"
          @click="load(p)"
        >
          {{ p }}
        </button>
      </div>
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

  <!-- 侧边抽屉 -->
  <Teleport to="body">
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-50 bg-black/30 flex justify-end"
    >
      <div class="w-full max-w-md h-full bg-white shadow-xl flex flex-col">
        <div class="flex items-center justify-between px-6 py-4 border-b border-gray-100">
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
            <label class="block text-sm font-medium text-gray-700 mb-1">
              内容 <span class="text-red-500">*</span>
            </label>
            <textarea
              v-model="form.content"
              rows="7"
              placeholder="记录今天的点滴…"
              class="w-full px-3 py-2 rounded-md border border-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-rose-200"
            />
          </div>
          <div>
            <div class="mb-2 flex items-center justify-between gap-3">
              <label class="block text-sm font-medium text-gray-700">图片</label>
              <span class="text-xs text-slate-400">{{ form.imagePaths?.length ?? 0 }} / {{ MAX_IMAGES }} 张 · 单张最大 5MB</span>
            </div>
            <input
              type="file"
              accept="image/*"
              multiple
              class="block text-sm text-gray-500"
              :disabled="imageUploading || (form.imagePaths?.length ?? 0) >= MAX_IMAGES"
              @change="handleImageUpload"
            />
            <p v-if="imageUploading" class="text-xs text-gray-400 mt-1">上传中…</p>
            <div v-if="form.imagePaths?.length" class="mt-3 grid grid-cols-3 gap-2">
              <div v-for="(path, imageIndex) in form.imagePaths" :key="path" class="group relative aspect-square">
                <img
                  :src="staticUrl(path)"
                  class="h-full w-full rounded-xl border border-slate-200 object-cover"
                  :alt="`预览 ${imageIndex + 1}`"
                />
                <button
                  class="absolute right-1.5 top-1.5 flex h-6 w-6 items-center justify-center rounded-full bg-slate-950/70 text-xs text-white opacity-90 transition hover:bg-red-600"
                  type="button"
                  :aria-label="`移除第 ${imageIndex + 1} 张图片`"
                  @click="removeImage(imageIndex)"
                >
                  ×
                </button>
              </div>
            </div>
          </div>
        </div>

        <div class="px-6 py-4 border-t border-gray-100 flex gap-3">
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
