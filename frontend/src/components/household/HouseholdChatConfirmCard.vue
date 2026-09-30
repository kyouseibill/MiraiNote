<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { householdApi } from '@/api/household'
import { apiFailure } from '@/utils/apiError'
import { formatAccountDateTime } from '@/utils/accountTime'
import { householdCreateHref, type HouseholdChatDraft } from '@/utils/householdChat'
import { createIdempotencyKey } from '@/utils/idempotencyKey'
import { formatCalendarDate, formatCost } from '@/utils/householdFormat'

const props = defineProps<{
  draft: HouseholdChatDraft
}>()

const selectedId = ref<number | null>(props.draft.item?.id ?? props.draft.candidates[0]?.id ?? null)
const deduct = ref(props.draft.deductConsumable)
const submitting = ref(false)
const done = ref(false)
const error = ref('')
const idempotencyKey = createIdempotencyKey()

const selected = computed(() =>
  props.draft.candidates.find((item) => item.id === selectedId.value) ?? props.draft.item)
const showDeduct = computed(() => selected.value?.consumableId != null)
const expired = computed(() => {
  if (!props.draft.expiresAt) return false
  const at = Date.parse(props.draft.expiresAt)
  return Number.isFinite(at) && at <= Date.now()
})
const expiryLabel = computed(() =>
  props.draft.expiresAt ? formatAccountDateTime(props.draft.expiresAt) : '')
const costLabel = computed(() => formatCost(props.draft.cost))
const createHref = computed(() => householdCreateHref(props.draft.suggestedName))

function choose(id: number) {
  if (done.value || submitting.value) return
  selectedId.value = id
}

async function confirm() {
  if (submitting.value || done.value || expired.value) return
  if (props.draft.draftId == null || selectedId.value == null) return
  submitting.value = true
  error.value = ''
  try {
    await householdApi.confirmChatDraft({
      draftId: props.draft.draftId,
      itemId: selectedId.value,
      completedOn: props.draft.completedOn,
      cost: props.draft.cost,
      deductConsumable: showDeduct.value ? deduct.value : null,
    }, idempotencyKey)
    done.value = true
  } catch (err) {
    error.value = apiFailure(err).message
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <section class="household-chat-card" data-testid="household-chat-card">
    <p class="household-chat-card-kicker">家务记录</p>
    <p class="household-chat-card-message">{{ draft.message }}</p>

    <RouterLink
      v-if="draft.kind === 'create'"
      class="household-chat-card-create"
      data-testid="household-chat-create"
      :to="createHref"
    >新建事项</RouterLink>

    <template v-else>
      <div v-if="draft.candidates.length > 1" class="household-chat-card-choices" role="listbox" aria-label="选择事项">
        <button
          v-for="item in draft.candidates"
          :key="item.id"
          type="button"
          role="option"
          class="household-chat-card-choice"
          :data-candidate-id="item.id"
          :aria-selected="selectedId === item.id"
          :disabled="done || submitting"
          @click="choose(item.id)"
        >
          {{ item.name }}<span v-if="item.location"> · {{ item.location }}</span>
        </button>
      </div>

      <dl v-if="selected" class="household-chat-card-facts">
        <div>
          <dt>事项</dt>
          <dd data-testid="household-chat-item">{{ selected.name }}<span v-if="selected.isPaused">（已暂停）</span></dd>
        </div>
        <div>
          <dt>日期</dt>
          <dd data-testid="household-chat-date">{{ formatCalendarDate(draft.completedOn) }}</dd>
        </div>
        <div v-if="costLabel">
          <dt>费用</dt>
          <dd data-testid="household-chat-cost">{{ costLabel }}</dd>
        </div>
      </dl>

      <p v-if="expiryLabel" class="household-chat-card-expiry" data-testid="household-chat-expiry">
        {{ expired ? '确认已过期，请重新说一次' : `请在 ${expiryLabel} 前确认` }}
      </p>

      <label v-if="showDeduct" class="household-chat-card-deduct">
        <input v-model="deduct" type="checkbox" data-testid="household-chat-deduct" :disabled="done || submitting || expired" />
        扣减耗材<span v-if="selected?.consumableName">（{{ selected.consumableName }}）</span>
      </label>

      <p v-if="error" class="household-chat-card-error" role="alert">{{ error }}</p>
      <p v-if="done" class="household-chat-card-done" role="status">已记下</p>
      <button
        v-else
        type="button"
        class="household-chat-card-submit"
        data-testid="household-chat-confirm"
        :disabled="submitting || expired || selectedId == null"
        @click="confirm"
      >{{ submitting ? '正在记下…' : '确认记下' }}</button>
    </template>
  </section>
</template>

<style scoped>
.household-chat-card {
  margin: 12px 0;
  max-width: 28rem;
  border: 1px solid var(--mn-line);
  border-radius: 8px;
  background: #fff;
  padding: 14px 16px;
}
.household-chat-card-kicker {
  margin: 0 0 4px;
  font-size: 11px;
  letter-spacing: 0.12em;
  color: var(--mn-muted);
}
.household-chat-card-message {
  margin: 0 0 10px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--mn-ink);
}
.household-chat-card-facts {
  display: grid;
  gap: 6px;
  margin: 0 0 10px;
  font-size: 13px;
}
.household-chat-card-facts div {
  display: flex;
  gap: 12px;
}
.household-chat-card-facts dt {
  width: 2.5rem;
  color: var(--mn-muted);
}
.household-chat-card-facts dd {
  margin: 0;
}
.household-chat-card-choices {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 10px;
}
.household-chat-card-choice {
  height: 32px;
  border: 1px solid var(--mn-line);
  border-radius: 6px;
  background: #fff;
  padding: 0 10px;
  font-size: 13px;
}
.household-chat-card-choice[aria-selected='true'] {
  border-color: var(--mn-indigo);
  color: var(--mn-indigo-dark);
}
.household-chat-card-expiry,
.household-chat-card-deduct,
.household-chat-card-error,
.household-chat-card-done {
  margin: 0 0 10px;
  font-size: 13px;
}
.household-chat-card-expiry,
.household-chat-card-error {
  color: #9d3b34;
}
.household-chat-card-deduct {
  display: flex;
  align-items: center;
  gap: 8px;
}
.household-chat-card-submit,
.household-chat-card-create {
  display: inline-flex;
  align-items: center;
  height: 34px;
  border-radius: 6px;
  background: var(--mn-indigo);
  padding: 0 12px;
  color: #fff;
  font-size: 13px;
  text-decoration: none;
}
.household-chat-card-submit:disabled {
  opacity: 0.5;
}
</style>
