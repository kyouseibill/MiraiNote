<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { workspaceApi, type WorkspaceEntry } from '@/api/workspace'
import { useToast } from '@/composables/useToast'

const emit = defineEmits<{
  attach: [result: { fileName: string; fileType: string; textContent: string }]
  close: []
}>()

const toast = useToast()

// 当前视图
const scope = ref<'private' | 'public'>('private')
const currentPath = ref('')
const entries = ref<WorkspaceEntry[]>([])
const loading = ref(false)
const attaching = ref<string | null>(null)

// 路径面包屑
const breadcrumbs = computed(() => {
  const parts = currentPath.value ? currentPath.value.split('/').filter(Boolean) : []
  const crumbs = [{ label: scope.value === 'private' ? '我的文件' : '公共文件', path: '' }]
  let acc = ''
  for (const p of parts) {
    acc = acc ? `${acc}/${p}` : p
    crumbs.push({ label: p, path: acc })
  }
  return crumbs
})

async function load() {
  loading.value = true
  try {
    const result = await workspaceApi.browse(scope.value, currentPath.value || undefined)
    entries.value = result.entries
    currentPath.value = result.currentPath
  } catch (e: any) {
    toast.error(e?.response?.data?.message ?? '加载失败')
  } finally {
    loading.value = false
  }
}

function openDir(entry: WorkspaceEntry) {
  currentPath.value = entry.relativePath
  load()
}

function navigateTo(path: string) {
  currentPath.value = path
  load()
}

async function attachFile(entry: WorkspaceEntry) {
  attaching.value = entry.relativePath
  try {
    const result = await workspaceApi.attach(entry.relativePath, scope.value)
    emit('attach', {
      fileName: result.fileName,
      fileType: result.fileType,
      textContent: result.textContent,
    })
    toast.success(`已附加：${result.fileName}`)
  } catch (e: any) {
    toast.error(e?.response?.data?.message ?? '附加失败')
  } finally {
    attaching.value = null
  }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`
  return `${(bytes / 1024 / 1024).toFixed(1)}MB`
}

function getIcon(entry: WorkspaceEntry): string {
  if (entry.type === 'dir') return '📁'
  const ext = entry.extension
  if (['.pdf'].includes(ext)) return '📄'
  if (['.docx', '.doc'].includes(ext)) return '📝'
  if (['.xlsx', '.xls', '.csv'].includes(ext)) return '📊'
  if (['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg'].includes(ext)) return '🖼️'
  if (['.mp4', '.mov', '.avi'].includes(ext)) return '🎬'
  if (['.mp3', '.wav', '.flac'].includes(ext)) return '🎵'
  if (['.zip', '.rar', '.tar', '.gz'].includes(ext)) return '🗜️'
  return '📃'
}

// 文件是否可附加（不支持视频/音频/压缩包）
const unsupportedExts = new Set(['.mp4', '.mov', '.avi', '.mkv', '.mp3', '.wav', '.flac', '.zip', '.rar', '.tar', '.gz', '.7z', '.exe', '.dll', '.so'])
function canAttach(entry: WorkspaceEntry): boolean {
  return entry.type === 'file' && !unsupportedExts.has(entry.extension)
}

// 切换 scope 时重置路径
watch(scope, () => {
  currentPath.value = ''
  load()
})

// 初始加载
load()
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- 头部 -->
    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-200 shrink-0">
      <h3 class="text-sm font-medium text-gray-800">工作区文件</h3>
      <button class="text-gray-400 hover:text-gray-600 text-lg leading-none" @click="$emit('close')">✕</button>
    </div>

    <!-- scope 切换 -->
    <div class="flex gap-1 px-4 py-2 border-b border-gray-100 shrink-0">
      <button
        v-for="s in (['private', 'public'] as const)"
        :key="s"
        class="px-3 py-1 rounded-full text-xs transition"
        :class="scope === s
          ? 'bg-teal-600 text-white'
          : 'bg-gray-100 text-gray-500 hover:bg-gray-200'"
        @click="scope = s"
      >
        {{ s === 'private' ? '🔒 我的文件' : '🌐 公共文件' }}
      </button>
    </div>

    <!-- 面包屑 -->
    <div class="flex items-center gap-1 px-4 py-1.5 text-xs text-gray-500 border-b border-gray-100 shrink-0 overflow-x-auto">
      <button
        v-for="(crumb, i) in breadcrumbs"
        :key="i"
        class="shrink-0 hover:text-teal-600"
        :class="{ 'text-gray-800 font-medium': i === breadcrumbs.length - 1 }"
        @click="navigateTo(crumb.path)"
      >{{ crumb.label }}</button>
      <span v-if="breadcrumbs.length > 1" class="text-gray-300 mx-0.5">/</span>
    </div>

    <!-- 文件列表 -->
    <div class="flex-1 overflow-y-auto">
      <div v-if="loading" class="px-4 py-8 text-center text-xs text-gray-400">加载中…</div>
      <div v-else-if="entries.length === 0" class="px-4 py-8 text-center text-xs text-gray-400">
        {{ scope === 'private' ? '私有区域为空，Agent 可在此创建文件' : '公共区域为空' }}
      </div>
      <ul v-else class="py-1">
        <li
          v-for="entry in entries"
          :key="entry.relativePath"
          class="flex items-center gap-2 px-4 py-2 hover:bg-gray-50 group"
        >
          <span class="text-base shrink-0">{{ getIcon(entry) }}</span>
          <div class="flex-1 min-w-0">
            <div
              class="text-sm text-gray-700 truncate"
              :class="entry.type === 'dir' ? 'cursor-pointer hover:text-teal-600' : ''"
              @click="entry.type === 'dir' ? openDir(entry) : undefined"
            >{{ entry.name }}</div>
            <div v-if="entry.type === 'file'" class="text-xs text-gray-400">{{ formatSize(entry.sizeBytes) }}</div>
          </div>
          <!-- 附加到消息按钮 -->
          <button
            v-if="canAttach(entry)"
            class="hidden group-hover:flex items-center gap-1 shrink-0 px-2 py-1 rounded text-xs bg-teal-50 text-teal-600 hover:bg-teal-100 transition"
            :disabled="attaching === entry.relativePath"
            @click.stop="attachFile(entry)"
          >
            <span v-if="attaching === entry.relativePath" class="animate-spin">⏳</span>
            <span v-else>📎 附加</span>
          </button>
          <!-- 目录箭头 -->
          <span v-if="entry.type === 'dir'" class="text-gray-300 text-xs shrink-0">▶</span>
        </li>
      </ul>
    </div>
  </div>
</template>
