<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from '@/composables/useToast'
import {
  downloadExportFile,
  fileNameFromUrl,
  isExportDownload,
} from '@/composables/useExportDownload'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const status = ref('正在下载…')

onMounted(async () => {
  const path = String(route.query.path || '')
  if (!path.startsWith('/api/v1/mirai/exports/') || !isExportDownload(path)) {
    toast.error('无效的导出下载链接')
    await router.replace('/chat')
    return
  }

  try {
    await downloadExportFile(path, fileNameFromUrl(path))
    toast.success('下载完成')
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : '文件下载失败，请稍后重试'
    toast.error(message)
  } finally {
    status.value = '即将返回对话…'
    await router.replace('/chat').catch(async () => {
      await router.replace('/')
    })
  }
})
</script>

<template>
  <div class="export-download-view" role="status" aria-live="polite">
    <p>{{ status }}</p>
  </div>
</template>

<style scoped>
.export-download-view {
  display: grid;
  place-items: center;
  min-height: 40vh;
  padding: 32px;
  color: var(--mn-ink, #2c2a26);
  font-size: 14px;
}
</style>
