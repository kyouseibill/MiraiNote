import { useHouseholdStore } from '@/stores/household'
import { useToast } from '@/composables/useToast'
import { apiFailure, isAxiosError } from '@/utils/apiError'

/** 接口 400/403 已由 axios 拦截器提示；这里补一句留在表单里，并在 403 后刷新角色。 */
export function useHouseholdFeedback() {
  const toast = useToast()
  const store = useHouseholdStore()

  async function report(error: unknown) {
    const failure = apiFailure(error)
    if (!isAxiosError(error)) toast.error(failure.message)
    if (failure.status === 403 && !store.previewMode) {
      try {
        await store.fetchHousehold()
      } catch {
        // 角色刷新失败时，保留刚才的错误提示。
      }
    }
    return failure
  }

  return { toast, store, report }
}
