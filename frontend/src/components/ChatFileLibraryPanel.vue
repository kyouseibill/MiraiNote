<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { IconDownload, IconLoader2, IconPaperclip } from '@tabler/icons-vue'
import { workspaceApi, type ChatLibraryFile } from '@/api/workspace'
import { useToast } from '@/composables/useToast'
import { downloadExportFile } from '@/composables/useExportDownload'
import { ACCOUNT_TIMEZONE, formatAccountDateTime } from '@/utils/accountTime'

const emit = defineEmits<{
  attach: [file: { fileName: string; fileType: string; textContent: string; storedPath?: string }]
}>()

const toast = useToast()
const loading = ref(true)
const error = ref('')
const organizedCount = ref(0)
const uploads = ref<ChatLibraryFile[]>([])
const generated = ref<ChatLibraryFile[]>([])
const exports = ref<ChatLibraryFile[]>([])
const uploadsTruncated = ref(false)
const generatedTruncated = ref(false)
const exportsTruncated = ref(false)
const attaching = ref('')

const unsupported = new Set([
  '.mp4', '.mov', '.avi', '.mkv', '.mp3', '.wav', '.flac',
  '.zip', '.rar', '.tar', '.gz', '.7z', '.exe', '.dll', '.so',
])

onMounted(load)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const library = await workspaceApi.library()
    organizedCount.value = library.organizedCount
    uploads.value = library.uploads
    generated.value = library.generated
    exports.value = library.exports
    uploadsTruncated.value = library.uploadsTruncated
    generatedTruncated.value = library.generatedTruncated
    exportsTruncated.value = library.exportsTruncated
  } catch (err: unknown) {
    const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
    error.value = message || '文件列表加载失败'
  } finally {
    loading.value = false
  }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatTime(iso: string): string {
  return formatAccountDateTime(iso, ACCOUNT_TIMEZONE)
}

function canAttach(file: ChatLibraryFile): boolean {
  return file.kind !== 'export' && !unsupported.has(file.extension)
}

async function attach(file: ChatLibraryFile) {
  attaching.value = file.relativePath
  try {
    const result = await workspaceApi.attach(file.relativePath, 'private')
    emit('attach', {
      fileName: result.fileName,
      fileType: result.fileType,
      textContent: result.textContent,
      storedPath: file.relativePath,
    })
    toast.success(`已附加：${result.fileName}`)
  } catch (err: unknown) {
    const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
    toast.error(message || '附加失败')
  } finally {
    attaching.value = ''
  }
}

async function download(file: ChatLibraryFile) {
  try {
    if (file.downloadUrl) {
      await downloadExportFile(file.downloadUrl, file.name)
      return
    }
    const response = await workspaceApi.download(file.relativePath)
    const blob = response.data
    const contentType = String(response.headers['content-type'] || '')
    if (contentType.includes('application/json')) {
      throw new Error('下载失败')
    }
    const objectUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = file.name
    link.style.display = 'none'
    document.body.appendChild(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
  } catch (err: unknown) {
    toast.error(err instanceof Error ? err.message : '下载失败')
  }
}
</script>

<template>
  <div class="chat-file-library">
    <p v-if="loading" class="chat-file-library-state" role="status">
      <IconLoader2 :size="16" class="chat-spin" />正在整理文件…
    </p>
    <p v-else-if="error" class="chat-file-library-state" role="alert">
      {{ error }}
      <button type="button" class="chat-link" @click="load">重试</button>
    </p>
    <template v-else>
      <p v-if="organizedCount" class="chat-file-library-note">
        已把 {{ organizedCount }} 个散落在工作区根目录的文件归到「工作生成 / archive」。
      </p>
      <section>
        <h3>上传的文件</h3>
        <p class="chat-file-library-hint">从对话里上传或粘贴的文件，保存在 uploads。</p>
        <p v-if="uploadsTruncated" class="chat-file-library-hint">只显示最近的一部分。</p>
        <p v-if="!uploads.length" class="chat-file-library-empty">还没有上传文件。</p>
        <article v-for="file in uploads" :key="file.relativePath" class="chat-artifact">
          <div>
            <span class="chat-file-type">{{ file.extension.replace('.', '').toUpperCase() || 'FILE' }}</span>
            <p :title="file.relativePath">
              {{ file.name }}
              <small>{{ formatTime(file.modifiedAt) }} · {{ formatSize(file.sizeBytes) }}</small>
            </p>
            <button
              v-if="canAttach(file)"
              type="button"
              class="chat-btn chat-artifact-copy"
              :disabled="attaching === file.relativePath"
              @click="attach(file)"
            >
              <IconPaperclip :size="14" />
              <span>{{ attaching === file.relativePath ? '读取中' : '附加' }}</span>
            </button>
            <button type="button" class="chat-icon" :aria-label="`下载 ${file.name}`" @click="download(file)">
              <IconDownload :size="18" />
            </button>
          </div>
          <code class="chat-artifact-path">{{ file.relativePath }}</code>
        </article>
      </section>
      <section>
        <h3>工作生成</h3>
        <p class="chat-file-library-hint">工作模式写出的文件在 generated。较早散落在根目录的文件在 archive。</p>
        <p v-if="generatedTruncated" class="chat-file-library-hint">只显示最近的一部分。</p>
        <p v-if="!generated.length" class="chat-file-library-empty">还没有工作生成的文件。</p>
        <article v-for="file in generated" :key="file.relativePath" class="chat-artifact">
          <div>
            <span class="chat-file-type">{{ file.extension.replace('.', '').toUpperCase() || 'FILE' }}</span>
            <p :title="file.relativePath">
              {{ file.name }}
              <small>{{ formatTime(file.modifiedAt) }} · {{ formatSize(file.sizeBytes) }}</small>
            </p>
            <button
              v-if="canAttach(file)"
              type="button"
              class="chat-btn chat-artifact-copy"
              :disabled="attaching === file.relativePath"
              @click="attach(file)"
            >
              <IconPaperclip :size="14" />
              <span>{{ attaching === file.relativePath ? '读取中' : '附加' }}</span>
            </button>
            <button type="button" class="chat-icon" :aria-label="`下载 ${file.name}`" @click="download(file)">
              <IconDownload :size="18" />
            </button>
          </div>
          <code class="chat-artifact-path">{{ file.relativePath }}</code>
        </article>
      </section>
      <section>
        <h3>导出成品</h3>
        <p class="chat-file-library-hint">PDF、Word、Excel 等交付文件。</p>
        <p v-if="exportsTruncated" class="chat-file-library-hint">只显示最近的一部分。</p>
        <p v-if="!exports.length" class="chat-file-library-empty">还没有导出文件。</p>
        <article v-for="file in exports" :key="file.relativePath" class="chat-artifact">
          <div>
            <span class="chat-file-type">{{ file.extension.replace('.', '').toUpperCase() || 'FILE' }}</span>
            <p :title="file.relativePath">
              {{ file.name }}
              <small>{{ formatTime(file.modifiedAt) }} · {{ formatSize(file.sizeBytes) }}</small>
            </p>
            <button type="button" class="chat-icon" :aria-label="`下载 ${file.name}`" @click="download(file)">
              <IconDownload :size="18" />
            </button>
          </div>
          <code class="chat-artifact-path">{{ file.downloadUrl || file.relativePath }}</code>
        </article>
      </section>
    </template>
  </div>
</template>

<style scoped src="../views/chat/chat.css"></style>
