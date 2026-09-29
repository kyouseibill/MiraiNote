<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import HouseholdPhotoField from '@/components/household/HouseholdPhotoField.vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { CompleteHouseholdItemResult, HouseholdItem } from '@/types/household'
import { apiFailure } from '@/utils/apiError'
import {
  backfillRenewalMessage,
  formatCalendarDate,
  shanghaiToday,
  shiftCalendarDay,
  validateCompletionDraft,
} from '@/utils/householdFormat'

const props = defineProps<{
  open: boolean
  itemId: number | null
}>()

const emit = defineEmits<{
  close: []
  completed: [CompleteHouseholdItemResult]
  refresh: []
}>()

const { toast, store, report } = useHouseholdFeedback()
const item = ref<HouseholdItem | null>(null)
const loading = ref(false)
const submitting = ref(false)
const uploading = ref(false)
const loadError = ref('')
const serverError = ref('')
const errors = ref<Record<string, string>>({})
const completedOn = ref('')
const memberId = ref('')
const photos = ref<string[]>([])
const cost = ref('')
const purchaseLink = ref('')
const note = ref('')
const renew = ref(false)
const newExpiryDate = ref('')
const skipDeduction = ref(false)
const quantity = ref('1')
const idempotencyKey = ref('')

const today = ref(shanghaiToday())
const tomorrow = computed(() => shiftCalendarDay(today.value, 1))
const consumable = computed(() => store.consumables.find((entry) => entry.id === item.value?.consumableId) ?? null)
const backfill = computed(() => Boolean(item.value?.lastDoneDate && completedOn.value && completedOn.value < item.value.lastDoneDate))

function reset() {
  item.value = null
  loadError.value = ''
  serverError.value = ''
  errors.value = {}
  today.value = shanghaiToday()
  completedOn.value = today.value
  memberId.value = store.household?.myMemberId ? String(store.household.myMemberId) : ''
  photos.value = []
  cost.value = ''
  purchaseLink.value = ''
  note.value = ''
  renew.value = false
  newExpiryDate.value = ''
  skipDeduction.value = false
  quantity.value = '1'
  uploading.value = false
  idempotencyKey.value = crypto.randomUUID()
}

async function load() {
  if (props.itemId == null) return
  loading.value = true
  loadError.value = ''
  try {
    if (!store.previewMode) {
      await Promise.all([
        store.members.length ? Promise.resolve() : store.fetchMembers(),
        store.consumables.length ? Promise.resolve() : store.fetchConsumables(),
      ])
    }
    item.value = store.items.find((entry) => entry.id === props.itemId) ?? await store.fetchItem(props.itemId)
    if (!memberId.value && store.household?.myMemberId) memberId.value = String(store.household.myMemberId)
  } catch (error) {
    const failure = await report(error)
    loadError.value = failure.message
  } finally {
    loading.value = false
  }
}

watch(() => props.open, (open) => {
  if (!open) return
  reset()
  void load()
})

watch(backfill, (isBackfill) => {
  if (!isBackfill) return
  renew.value = false
  newExpiryDate.value = ''
})

async function submit() {
  if (submitting.value || uploading.value || !item.value) return
  serverError.value = ''
  const renewing = item.value.itemType === 'OneOffExpiry' && renew.value && !backfill.value
  errors.value = validateCompletionDraft({
    completedOn: completedOn.value,
    today: today.value,
    backfill: backfill.value,
    renew: item.value.itemType === 'OneOffExpiry' && renew.value,
    newExpiryDate: newExpiryDate.value,
    cost: cost.value,
    hasConsumable: item.value.consumableId != null,
    skipDeduction: skipDeduction.value,
    quantity: quantity.value,
  })
  if (Object.keys(errors.value).length) return

  const before = item.value
  submitting.value = true
  try {
    const result = await store.completeItem(item.value.id, {
      completedOn: completedOn.value,
      completedByMemberId: memberId.value ? Number(memberId.value) : null,
      photoRefs: photos.value,
      cost: cost.value.trim() ? Number(cost.value) : null,
      purchaseLink: purchaseLink.value.trim() || null,
      note: note.value.trim() || null,
      skipConsumableDeduction: before.consumableId != null && skipDeduction.value,
      consumableQuantity: before.consumableId != null && !skipDeduction.value ? Number(quantity.value) : null,
      newExpiryDate: renewing ? newExpiryDate.value : null,
    }, idempotencyKey.value)
    const keptDue = Boolean(before.lastDoneDate && completedOn.value < before.lastDoneDate && result.item.nextDueDate === before.nextDueDate)
    const parts = [keptDue
      ? '已补记到历史，下次到期日保持不变'
      : result.record.newExpiryDate
        ? `已完成，到期日已更新为 ${formatCalendarDate(result.item.nextDueDate)}`
        : '已完成']
    if (store.previewMode) parts.push('设计预览不会在浏览器里重算到期日')
    if (result.needsRestock) parts.push('耗材库存为 0，需要补货')
    const message = parts.join('。')
    if (result.needsRestock) toast.warning(message)
    else toast.success(message)
    emit('completed', result)
    emit('close')
  } catch (error) {
    const failure = apiFailure(error)
    if (failure.status === 409) {
      toast.info('刚刚已提交')
      emit('refresh')
      emit('close')
      return
    }
    if (failure.status === 422) {
      idempotencyKey.value = crypto.randomUUID()
      serverError.value = failure.message ? `${failure.message}。请刷新后重试` : '请刷新后重试'
      toast.error('请刷新后重试')
      emit('refresh')
      if (props.itemId != null && !store.previewMode) {
        try {
          item.value = await store.fetchItem(props.itemId)
        } catch {
          // 列表刷新失败时，对话框里仍保留上面的提示。
        }
      }
      return
    }
    const reported = await report(error)
    serverError.value = reported.message
    if (reported.status === 403) emit('close')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppDialog
    :open="open"
    title="标记已完成"
    :description="item ? item.name : '记录这次完成'"
    size="lg"
    :busy="submitting || uploading"
    @close="emit('close')"
  >
    <p v-if="loading" class="text-[13px] text-[var(--mn-muted)]">正在读取事项…</p>
    <p v-else-if="loadError" role="alert" class="text-[13px] text-[#9d3b34]">{{ loadError }}</p>
    <form v-else-if="item" class="space-y-4" @submit.prevent="submit">
      <p v-if="serverError" role="alert" class="rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-3 py-2 text-[12px] text-[#9d3b34]">{{ serverError }}</p>
      <div>
        <label class="text-[13px] font-medium" for="complete-date">完成日期</label>
        <input id="complete-date" v-model="completedOn" data-dialog-autofocus type="date" class="form-input mt-1.5 h-10" :max="today" :disabled="submitting" />
        <p class="mt-1 text-[11px] text-[var(--mn-muted)]">默认今天，可以改成过去的日期。今天按北京时间，不能晚于今天。</p>
        <p v-if="errors.completedOn" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.completedOn }}</p>
        <p v-if="backfill" class="mt-1 text-[12px] text-[#4c6178]">这个日期早于上次完成日期，只会补进历史，不会改下次到期日。{{ backfillRenewalMessage }}</p>
      </div>
      <div>
        <label class="text-[13px] font-medium" for="complete-member">执行人</label>
        <select id="complete-member" v-model="memberId" class="form-input mt-1.5 h-10" :disabled="submitting">
          <option v-for="member in store.members" :key="member.id" :value="String(member.id)">{{ member.username }}</option>
        </select>
      </div>
      <HouseholdPhotoField v-model="photos" :disabled="submitting" @uploading="uploading = $event" />
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="text-[13px] font-medium" for="complete-cost">费用（元）</label>
          <input id="complete-cost" v-model="cost" inputmode="decimal" class="form-input mt-1.5 h-10" placeholder="可不填" :disabled="submitting" />
          <p v-if="errors.cost" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.cost }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="complete-link">购买链接</label>
          <input id="complete-link" v-model="purchaseLink" class="form-input mt-1.5 h-10" placeholder="可不填" :disabled="submitting" />
        </div>
      </div>
      <div>
        <label class="text-[13px] font-medium" for="complete-note">备注</label>
        <textarea id="complete-note" v-model="note" rows="3" class="form-input mt-1.5 resize-none py-2" :disabled="submitting" />
      </div>
      <div v-if="consumable" class="rounded-md border border-[var(--mn-line)] px-3 py-3">
        <p class="text-[13px] font-medium">关联耗材：{{ consumable.name }}</p>
        <p class="mt-1 text-[11px] text-[var(--mn-muted)]">当前库存 {{ consumable.currentStock }}{{ consumable.unit || '' }}。默认扣 1，可以改数量或这次不扣。</p>
        <label class="mt-3 flex items-center gap-2 text-[13px]">
          <input v-model="skipDeduction" type="checkbox" :disabled="submitting" />
          这次不扣减
        </label>
        <div v-if="!skipDeduction" class="mt-3">
          <label class="text-[13px]" for="complete-qty">扣减数量</label>
          <input id="complete-qty" v-model="quantity" type="number" min="0" step="1" class="form-input mt-1.5 h-10" :disabled="submitting" />
          <p v-if="errors.quantity" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.quantity }}</p>
        </div>
      </div>
      <div v-if="item.itemType === 'OneOffExpiry'" class="rounded-md border border-[var(--mn-line)] px-3 py-3">
        <label class="flex items-center gap-2 text-[13px] font-medium">
          <input v-model="renew" type="checkbox" :disabled="submitting || backfill" />
          续期，并设置新的到期日
        </label>
        <p class="mt-1 text-[11px] text-[var(--mn-muted)]">
          {{ backfill ? backfillRenewalMessage : '不续期则保持当前到期日。新的到期日必须晚于今天，也必须晚于这次的完成日期。' }}
        </p>
        <div v-if="renew && !backfill" class="mt-3">
          <label class="text-[13px]" for="complete-expiry">新的到期日</label>
          <input id="complete-expiry" v-model="newExpiryDate" type="date" class="form-input mt-1.5 h-10" :min="tomorrow" :disabled="submitting" />
          <p v-if="errors.newExpiryDate" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.newExpiryDate }}</p>
        </div>
        <p v-else-if="errors.newExpiryDate" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.newExpiryDate }}</p>
      </div>
    </form>
    <template #footer>
      <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="submitting" @click="emit('close')">取消</button>
      <button id="household-complete-submit" type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white disabled:opacity-50" :disabled="submitting || uploading || loading || !item" @click="submit">
        {{ submitting ? '提交中…' : '确认完成' }}
      </button>
    </template>
  </AppDialog>
</template>
