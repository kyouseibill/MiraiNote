<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { lifeLogApi, type LifeLog, type LifeLogPayload } from '@/api/data'
import ImageViewer from '@/components/ImageViewer.vue'
import { useUiStore } from '@/stores/ui'
import { staticUrl } from '@/utils/url'
import { fmtDate } from '@/utils/format'

// 生活流（M1 精简版）：时间线 + 心情 + 图片查看器（复用 frontend 逻辑）+ 编辑 + 侧边对话挂载
const route = useRoute()
const router = useRouter()
const ui = useUiStore()

const items = ref<LifeLog[]>([])
const loading = ref(true)
const error = ref('')
const expandedId = ref<number | null>(null)

const MOODS = ['开心', '平静', '疲惫', '难过', '兴奋', '焦虑', '感恩']
const MOOD_EMOJI: Record<string, string> = {
  开心: '😊', 平静: '😌', 疲惫: '😪', 难过: '😢', 兴奋: '🤩', 焦虑: '😰', 感恩: '🙏',
}

// 心情统计（当前已加载数据）
const moodStats = computed(() => {
  const counts: Record<string, number> = {}
  for (const it of items.value) if (it.mood) counts[it.mood] = (counts[it.mood] ?? 0) + 1
  const total = Object.values(counts).reduce((s, v) => s + v, 0)
  return Object.entries(counts)
    .sort((a, b) => b[1] - a[1])
    .map(([mood, count]) => ({ mood, count, pct: total ? Math.round((count / total) * 100) : 0 }))
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await lifeLogApi.list()
    const want = route.query.id ? Number(route.query.id) : null
    if (want && items.value.some((l) => l.id === want)) expandedId.value = want
    else expandedId.value = items.value[0]?.id ?? null
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

function toggle(it: LifeLog) {
  expandedId.value = expandedId.value === it.id ? null : it.id
  if (expandedId.value === it.id) router.replace({ query: { ...route.query, id: String(it.id) } }).catch(() => {})
}

function imagesOf(it: LifeLog): string[] {
  return it.imagePaths?.length ? it.imagePaths : it.imagePath ? [it.imagePath] : []
}

function openDiscuss(it: LifeLog) {
  ui.openContext({ type: 'lifelog', id: it.id, title: it.content.slice(0, 18) })
}

// 视觉稿 ⑤：「→」展开当前展开条目的侧边讨论（编辑/查看器打开时忽略；收起逻辑在 App.vue）
function onKeydown(e: KeyboardEvent) {
  if (e.key !== 'ArrowRight' || ui.contextTarget || editOpen.value || viewerOpen.value) return
  const t = e.target as HTMLElement | null
  if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return
  const it = items.value.find((x) => x.id === expandedId.value)
  if (it) openDiscuss(it)
}

// ---- 图片查看器 ----
const viewerOpen = ref(false)
const viewerImages = ref<string[]>([])
const viewerIndex = ref(0)
function openViewer(it: LifeLog, index = 0) {
  const imgs = imagesOf(it)
  if (!imgs.length) return
  viewerImages.value = imgs
  viewerIndex.value = index
  viewerOpen.value = true
}

// ---- 编辑抽屉 ----
const editOpen = ref(false)
const saving = ref(false)
const form = ref<LifeLogPayload & { id?: number }>(emptyForm())

function emptyForm(): LifeLogPayload {
  return { content: '', mood: null, imagePaths: [], logDate: new Date().toISOString().slice(0, 10) }
}

function openCreate() {
  form.value = emptyForm()
  editOpen.value = true
}

function openEdit(it: LifeLog) {
  form.value = { id: it.id, content: it.content, mood: it.mood, imagePaths: imagesOf(it), logDate: it.logDate.slice(0, 10) }
  editOpen.value = true
}

async function save() {
  if (!form.value.content.trim() || saving.value) return
  saving.value = true
  try {
    const saved = await lifeLogApi.save({ ...form.value, imagePath: form.value.imagePaths?.[0] ?? null })
    editOpen.value = false
    await load()
    expandedId.value = saved.id
  } catch (e) {
    error.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  load()
  window.addEventListener('keydown', onKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-3">
    <div class="flex items-center gap-3">
      <h1 class="text-base font-bold">生活流</h1>
      <span class="text-[11px] text-ink-faint">{{ items.length }} 条</span>
      <!-- 心情统计 -->
      <div v-if="moodStats.length" class="ml-2 flex flex-wrap gap-1.5">
        <span v-for="m in moodStats" :key="m.mood" class="rounded-full border border-ink-faint/20 bg-paper-card px-2 py-0.5 text-[10px] text-ink-sub" :title="`${m.mood} ${m.count} 条（${m.pct}%）`">
          {{ MOOD_EMOJI[m.mood] }} {{ m.mood }} {{ m.pct }}%
        </span>
      </div>
      <button class="ml-auto rounded-lg bg-brand px-2.5 py-1 text-xs text-white hover:bg-brand-dark" @click="openCreate">＋ 记一笔</button>
    </div>

    <div v-if="error" class="rounded-xl border border-red-200 bg-red-50 p-3 text-xs text-red-700">
      {{ error }}
      <button class="ml-2 underline" @click="load">重试</button>
    </div>

    <div v-if="loading" class="space-y-3">
      <div v-for="i in 3" :key="i" class="h-24 animate-pulse rounded-xl bg-paper-card" />
    </div>

    <div v-else-if="!items.length" class="rounded-xl border border-dashed border-ink-faint/30 p-16 text-center text-xs text-ink-faint">
      还没有生活记录，记下今天的心情吧
    </div>

    <!-- 时间线 -->
    <div v-else class="ml-2 border-l-2 border-ink-faint/15 pl-5">
      <div v-for="it in items" :key="it.id" class="relative mb-4">
        <span class="absolute -left-[26px] top-2 h-2.5 w-2.5 rounded-full border-2 border-paper bg-brand-line" />
        <div class="rounded-xl border bg-paper-card p-4" :class="expandedId === it.id ? 'border-brand/60' : 'border-ink-faint/20'">
          <button class="block w-full text-left" @click="toggle(it)">
            <div class="flex items-center gap-2 text-[11px] text-ink-faint">
              <span>{{ fmtDate(it.logDate) }}</span>
              <span v-if="it.mood" class="rounded-full border border-pink-200 bg-pink-50 px-2 py-0.5 text-[10px] text-pink-600">
                {{ MOOD_EMOJI[it.mood] ?? '' }} {{ it.mood }}
              </span>
              <span v-if="imagesOf(it).length" class="text-[10px]">🖼 {{ imagesOf(it).length }}</span>
              <span class="ml-auto">{{ expandedId === it.id ? '收起 ▴' : '展开 ▾' }}</span>
            </div>
            <p class="mt-1.5 text-[13px] leading-7" :class="expandedId === it.id ? '' : 'line-clamp-2'">{{ it.content }}</p>
          </button>

          <div v-if="expandedId === it.id">
            <!-- 图片栅格（点击进查看器） -->
            <div v-if="imagesOf(it).length" class="mt-3 grid grid-cols-5 gap-2">
              <button
                v-for="(img, i) in imagesOf(it)"
                :key="img"
                class="aspect-square cursor-zoom-in overflow-hidden rounded-lg border border-ink-faint/15 hover:border-brand/50"
                @click="openViewer(it, i)"
              >
                <img :src="staticUrl(img)" :alt="`照片 ${i + 1}`" class="h-full w-full object-cover" loading="lazy" />
              </button>
            </div>
            <div class="mt-3 flex gap-2 border-t border-dashed border-ink-faint/15 pt-2.5">
              <button class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs hover:border-brand hover:text-brand" @click="openEdit(it)">✎ 编辑</button>
              <button class="rounded-lg border border-ink-faint/30 px-3 py-1 text-xs hover:border-brand hover:text-brand" @click="openDiscuss(it)">💬 讨论</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 编辑抽屉 -->
    <Teleport to="body">
      <div v-if="editOpen" class="fixed inset-0 z-50 flex justify-end bg-black/30" @click.self="editOpen = false">
        <div class="flex h-full w-[440px] flex-col bg-white shadow-xl">
          <div class="flex items-center justify-between border-b border-ink-faint/15 px-5 py-3.5">
            <b class="text-sm">{{ form.id ? '编辑生活记录' : '记一笔生活' }}</b>
            <button class="text-ink-faint hover:text-ink" @click="editOpen = false">✕</button>
          </div>
          <div class="min-h-0 flex-1 space-y-3 overflow-y-auto px-5 py-4">
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">日期</span>
              <input v-model="form.logDate" type="date" class="w-full rounded-lg border border-ink-faint/30 px-3 py-1.5 text-sm outline-none focus:border-brand" />
            </label>
            <label class="block">
              <span class="mb-1 block text-xs font-medium text-ink-sub">内容</span>
              <textarea v-model="form.content" rows="6" class="w-full rounded-lg border border-ink-faint/30 px-3 py-2 text-sm leading-6 outline-none focus:border-brand" />
            </label>
            <div>
              <span class="mb-1 block text-xs font-medium text-ink-sub">心情</span>
              <div class="flex flex-wrap gap-1.5">
                <button
                  v-for="m in MOODS"
                  :key="m"
                  class="rounded-full border px-2.5 py-1 text-xs"
                  :class="form.mood === m ? 'border-pink-400 bg-pink-50 text-pink-600' : 'border-ink-faint/25 text-ink-sub hover:border-pink-300'"
                  @click="form.mood = form.mood === m ? null : m"
                >
                  {{ MOOD_EMOJI[m] }} {{ m }}
                </button>
              </div>
            </div>
            <div v-if="form.imagePaths?.length" class="rounded-lg border border-dashed border-ink-faint/30 p-2 text-[11px] text-ink-faint">
              {{ form.imagePaths.length }} 张图片（图片管理在 Web 端上传，M1 桌面端保留展示）
            </div>
          </div>
          <div class="flex justify-end gap-2 border-t border-ink-faint/15 px-5 py-3">
            <button class="rounded-lg border border-ink-faint/30 px-4 py-1.5 text-xs" @click="editOpen = false">取消</button>
            <button class="rounded-lg bg-brand px-4 py-1.5 text-xs font-semibold text-white hover:bg-brand-dark disabled:opacity-50" :disabled="saving || !form.content.trim()" @click="save">
              {{ saving ? '保存中…' : '保存' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 图片查看器 -->
    <ImageViewer v-model:open="viewerOpen" v-model:index="viewerIndex" :images="viewerImages" />
  </div>
</template>
