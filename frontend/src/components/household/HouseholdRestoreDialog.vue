<script setup lang="ts">
import { ref, watch } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { HouseholdItem } from '@/types/household'
import { shiftCalendarDay } from '@/utils/householdFormat'

const props = defineProps<{
  open: boolean
  item: HouseholdItem | null
}>()

const emit = defineEmits<{
  close: []
  restored: []
}>()

const { toast, store, report } = useHouseholdFeedback()
const expiryDate = ref('')
const error = ref('')
const busy = ref(false)
const today = ref(store.calendarToday)
const tomorrow = ref(shiftCalendarDay(store.calendarToday, 1))

async function refreshClock() {
  if (!store.previewMode) await store.fetchServerToday()
  today.value = store.calendarToday
  tomorrow.value = shiftCalendarDay(today.value, 1)
}

async function reset() {
  await refreshClock()
  expiryDate.value = ''
  error.value = ''
}

watch(() => props.open, (open) => {
  if (open) void reset()
})

async function submit() {
  if (!props.item || busy.value) return
  await refreshClock()
  if (!expiryDate.value) {
    error.value = '请填写新的到期日'
    return
  }
  if (expiryDate.value <= today.value) {
    error.value = '新的到期日必须晚于今天'
    return
  }
  busy.value = true
  try {
    await store.restoreItem(props.item.id, expiryDate.value)
    toast.success('已恢复')
    emit('restored')
    emit('close')
  } catch (cause) {
    error.value = (await report(cause)).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <AppDialog
    :open="open"
    title="恢复事项"
    :description="item ? `为「${item.name}」填写新的到期日。必须晚于今天（北京时间）。` : ''"
    :busy="busy"
    @close="emit('close')"
  >
    <form class="space-y-2" @submit.prevent="submit">
      <label class="text-[13px] font-medium" for="restore-expiry">新的到期日</label>
      <input id="restore-expiry" v-model="expiryDate" data-dialog-autofocus type="date" class="form-input mt-1.5 h-10" :min="tomorrow" :disabled="busy" />
      <p v-if="error" role="alert" class="text-[12px] text-[#9d3b34]">{{ error }}</p>
    </form>
    <template #footer>
      <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="emit('close')">取消</button>
      <button type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="submit">恢复</button>
    </template>
  </AppDialog>
</template>
