import { computed } from 'vue'
import { useRoute } from 'vue-router'

/** 仅开发环境：无后端时用示例数据看家务周期界面。 */
export function useDesignPreview() {
  const route = useRoute()
  const active = computed(() => import.meta.env.DEV && route.query.designPreview === '1')
  const asMember = computed(() => active.value && route.query.as === 'member')

  function withPreview(path: string) {
    if (!active.value) return path
    return {
      path,
      query: {
        designPreview: '1',
        ...(asMember.value ? { as: 'member' } : {}),
      },
    }
  }

  return { active, asMember, withPreview }
}
