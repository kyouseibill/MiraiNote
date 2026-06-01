<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useWorkLogStore } from '@/stores/workLog'
import { useToast } from '@/composables/useToast'
import { renderMarkdown } from '@/composables/useMarkdown'
import type { WorkLog, CreateWorkLogPayload } from '@/types/workLog'

const store = useWorkLogStore()
const toast = useToast()

const keyword = ref('')
const dateFrom = ref('')
const dateTo = ref('')
const filterCategory = ref('')

const drawerOpen = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)
// 鎶藉眽鍐?缂栬緫/棰勮 鍒囨崲
const previewMode = ref(false)

const form = reactive<CreateWorkLogPayload>({
  title: '',
  purpose: '',
  content: '',
  tags: '',
  category: '',
  logDate: todayStr(),
})

const isEdit = computed(() => editingId.value !== null)
const totalPages = computed(() => Math.ceil(store.total / store.pageSize))

// 褰撳墠閫変腑鏉＄洰灞曞紑璇︽儏
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

async function load(page = 1) {
  try {
    await store.fetchList({
      page,
      keyword: keyword.value || undefined,
      dateFrom: dateFrom.value || undefined,
      dateTo: dateTo.value || undefined,
      category: filterCategory.value || undefined,
    })
  } catch {
    // 閿欒宸茬敱鎷︽埅鍣ㄥ鐞?
  }
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
  drawerOpen.value = true
}

function toggleExpand(item: WorkLog) {
  expandedId.value = expandedId.value === item.id ? null : item.id
}

async function submit() {
  if (!form.title.trim()) {
    toast.error('璇峰～鍐欐爣棰?)
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
      toast.success('宸叉洿鏂?)
    } else {
      await store.create(payload)
      toast.success('宸插垱寤?)
    }
    drawerOpen.value = false
    resetForm()
  } catch {
    // 鎷︽埅鍣ㄥ凡 toast
  } finally {
    submitting.value = false
  }
}

async function remove(item: WorkLog) {
  if (!confirm(`纭畾鍒犻櫎銆?{item.title}銆嶏紵`)) return
  try {
    await store.remove(item.id)
    toast.success('宸插垹闄?)
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
  <div class="max-w-6xl mx-auto px-6 py-6">
    <!-- 椤堕儴鎿嶄綔鏍?-->
    <div class="flex flex-wrap items-end gap-3 mb-4">
      <div class="flex-1 min-w-[200px]">
        <label class="block text-xs text-gray-500 mb-1">鍏抽敭瀛?/label>
        <input
          v-model="keyword"
          type="text"
          placeholder="鎼滅储鏍囬 / 鍐呭 / 鏍囩"
          class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-200"
          @keyup.enter="load(1)"
        />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">鍒嗙被</label>
        <select
          v-model="filterCategory"
          class="h-9 px-2 rounded-md border border-gray-200 text-sm bg-white"
          @change="load(1)"
        >
          <option value="">鍏ㄩ儴鍒嗙被</option>
          <option v-for="cat in store.categories" :key="cat" :value="cat">{{ cat }}</option>
        </select>
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">璧峰鏃ユ湡</label>
        <input v-model="dateFrom" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" />
      </div>
      <div>
        <label class="block text-xs text-gray-500 mb-1">缁撴潫鏃ユ湡</label>
        <input v-model="dateTo" type="date" class="h-9 px-2 rounded-md border border-gray-200 text-sm" />
      </div>
      <button
        class="h-9 px-4 rounded-md bg-gray-100 text-gray-700 text-sm hover:bg-gray-200"
        @click="load(1)"
      >
        绛涢€?
      </button>
      <button
        class="h-9 px-4 rounded-md bg-indigo-600 text-white text-sm hover:bg-indigo-700 shadow-sm"
        @click="openCreate"
      >
        + 鏂板缓璁板綍
      </button>
    </div>

    <!-- 鏁伴噺鎻愮ず -->
    <p v-if="!store.loading && store.total > 0" class="text-xs text-gray-400 mb-2">
      鍏?{{ store.total }} 鏉¤褰?
    </p>

    <!-- 鍒楄〃 -->
    <div class="bg-white border border-gray-100 rounded-xl shadow-sm overflow-hidden">
      <div v-if="store.loading" class="p-10 text-center text-gray-400 text-sm">鍔犺浇涓€?/div>
      <div v-else-if="store.items.length === 0" class="p-10 text-center text-gray-400 text-sm">
        鏆傛棤璁板綍锛岀偣鍑汇€屾柊寤鸿褰曘€嶅紑濮?
      </div>
      <ul v-else class="divide-y divide-gray-100">
        <li
          v-for="item in store.items"
          :key="item.id"
          class="hover:bg-gray-50 transition"
        >
          <!-- 鎽樿琛?-->
          <div
            class="p-4 cursor-pointer flex items-start justify-between gap-3"
            @click="toggleExpand(item)"
          >
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2 flex-wrap">
                <span class="text-xs text-gray-400 font-mono shrink-0">{{ fmtDate(item.logDate) }}</span>
                <span v-if="item.category" class="text-xs px-1.5 py-0.5 rounded bg-indigo-50 text-indigo-600 shrink-0">
                  {{ item.category }}
                </span>
                <h3 class="font-medium text-gray-900 truncate">{{ item.title }}</h3>
              </div>
              <p v-if="item.purpose" class="mt-1 text-xs text-gray-500 line-clamp-1">鐩殑锛歿{ item.purpose }}</p>
              <!-- 鎶樺彔鏃舵樉绀虹函鏂囨湰鎽樿 -->
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
                class="text-xs text-indigo-500 hover:text-indigo-700 px-2 py-1"
                @click.stop="openEdit(item)"
              >
                缂栬緫
              </button>
              <button
                class="text-xs text-red-400 hover:text-red-600 px-2 py-1"
                @click.stop="remove(item)"
              >
                鍒犻櫎
              </button>
              <span class="text-xs text-gray-300 ml-1">{{ expandedId === item.id ? '鈻? : '鈻? }}</span>
            </div>
          </div>

          <!-- 灞曞紑锛歁arkdown 娓叉煋鍐呭 -->
          <div v-if="expandedId === item.id && item.content" class="px-4 pb-4">
            <div
              class="prose prose-sm max-w-none text-gray-700 bg-gray-50 rounded-lg p-4 border border-gray-100"
              v-html="renderMarkdown(item.content)"
            />
          </div>
        </li>
      </ul>
    </div>

    <!-- 鍒嗛〉 -->
    <div v-if="totalPages > 1" class="mt-4 flex items-center justify-center gap-2">
      <button
        :disabled="store.page <= 1"
        class="px-3 py-1.5 text-sm rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
        @click="load(store.page - 1)"
      >
        涓婁竴椤?
      </button>
      <div class="flex gap-1">
        <button
          v-for="p in totalPages"
          :key="p"
          class="w-8 h-8 text-sm rounded-md transition"
          :class="p === store.page
            ? 'bg-indigo-600 text-white'
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
        涓嬩竴椤?
      </button>
    </div>
  </div>

  <!-- 鎶藉眽锛氭柊澧?/ 缂栬緫 -->
  <Teleport to="body">
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-50 bg-black/30 flex justify-end"
      @click.self="drawerOpen = false"
    >
      <div class="w-full max-w-xl h-full bg-white shadow-xl flex flex-col">
        <header class="h-14 px-5 border-b border-gray-100 flex items-center justify-between">
          <h3 class="font-semibold text-gray-900">{{ isEdit ? '缂栬緫宸ヤ綔璁板綍' : '鏂板缓宸ヤ綔璁板綍' }}</h3>
          <button class="text-gray-400 hover:text-gray-600" @click="drawerOpen = false">鉁?/button>
        </header>

        <div class="flex-1 overflow-y-auto p-5 space-y-4">
          <div>
            <label class="block text-sm text-gray-700 mb-1">鏍囬 <span class="text-red-500">*</span></label>
            <input v-model="form.title" type="text" maxlength="200" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-200" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-sm text-gray-700 mb-1">鏃ユ湡 <span class="text-red-500">*</span></label>
              <input v-model="form.logDate" type="date" class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
            </div>
            <div>
              <label class="block text-sm text-gray-700 mb-1">鍒嗙被</label>
              <input
                v-model="form.category"
                type="text"
                list="category-suggestions"
                placeholder="濡傦細寮€鍙?/ 浼氳"
                class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm"
              />
              <datalist id="category-suggestions">
                <option v-for="cat in store.categories" :key="cat" :value="cat" />
              </datalist>
            </div>
          </div>
          <div>
            <label class="block text-sm text-gray-700 mb-1">鐩殑</label>
            <input v-model="form.purpose" type="text" maxlength="500" placeholder="鏈」宸ヤ綔鐨勭洰鐨? class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>

          <!-- 鍐呭锛氱紪杈?棰勮 鍒囨崲 -->
          <div>
            <div class="flex items-center justify-between mb-1">
              <label class="text-sm text-gray-700">鍐呭锛堟敮鎸?Markdown锛?/label>
              <div class="flex rounded-md overflow-hidden border border-gray-200 text-xs">
                <button
                  class="px-2 py-0.5 transition"
                  :class="!previewMode ? 'bg-indigo-600 text-white' : 'text-gray-500 hover:bg-gray-50'"
                  @click="previewMode = false"
                >
                  缂栬緫
                </button>
                <button
                  class="px-2 py-0.5 transition"
                  :class="previewMode ? 'bg-indigo-600 text-white' : 'text-gray-500 hover:bg-gray-50'"
                  @click="previewMode = true"
                >
                  棰勮
                </button>
              </div>
            </div>
            <textarea
              v-if="!previewMode"
              v-model="form.content"
              rows="12"
              class="w-full p-3 rounded-md border border-gray-200 text-sm font-mono leading-6 focus:outline-none focus:ring-2 focus:ring-indigo-200"
              placeholder="鏀寔 Markdown 璇硶锛屽 **绮椾綋**銆? 鏍囬銆? 鍒楄〃鈥?
            />
            <div
              v-else
              class="min-h-[12rem] p-3 rounded-md border border-gray-200 bg-gray-50 prose prose-sm max-w-none text-gray-700"
              v-html="renderMarkdown(form.content)"
            />
          </div>

          <div>
            <label class="block text-sm text-gray-700 mb-1">鏍囩锛堥€楀彿鍒嗛殧锛?/label>
            <input v-model="form.tags" type="text" placeholder="渚嬪锛氶」鐩瓵, 绱ф€? class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm" />
          </div>
        </div>

        <footer class="h-14 px-5 border-t border-gray-100 flex items-center justify-end gap-2">
          <button class="h-9 px-4 rounded-md text-gray-700 hover:bg-gray-100" @click="drawerOpen = false">鍙栨秷</button>
          <button
            class="h-9 px-4 rounded-md bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-60"
            :disabled="submitting"
            @click="submit"
          >
            {{ submitting ? '淇濆瓨涓€? : '淇濆瓨' }}
          </button>
        </footer>
      </div>
    </div>
  </Teleport>

</template>
