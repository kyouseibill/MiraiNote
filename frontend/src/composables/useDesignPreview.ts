import { computed } from 'vue'
import { useRoute } from 'vue-router'

/** 仅开发环境：无后端时用示例数据看界面。 */
export function useDesignPreview() {
  const route = useRoute()
  const active = computed(() => import.meta.env.DEV && route.query.designPreview === '1')

  function withPreview(path: string) {
    if (!active.value) return path
    return {
      path,
      query: { designPreview: '1' },
    }
  }

  return { active, withPreview }
}
