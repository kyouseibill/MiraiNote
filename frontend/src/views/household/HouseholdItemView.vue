<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { IconArrowLeft, IconLoader2 } from '@tabler/icons-vue'
import AppDialog from '@/components/AppDialog.vue'
import HouseholdCompleteDialog from '@/components/household/HouseholdCompleteDialog.vue'
import HouseholdItemFormDialog from '@/components/household/HouseholdItemFormDialog.vue'
import HouseholdRestoreDialog from '@/components/household/HouseholdRestoreDialog.vue'
import HouseholdStatusPill from '@/components/household/HouseholdStatusPill.vue'
import { useDesignPreview } from '@/composables/useDesignPreview'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import { staticUrl } from '@/composables/useStaticUrl'
import { formatAccountDateTime } from '@/utils/accountTime'
import {
  categoryLabel,
  cycleLabel,
  formatCalendarDate,
  formatCost,
  itemStatus,
  itemTypeLabel,
  safeHttpUrl,
} from '@/utils/householdFormat'

const route = useRoute()
const router = useRouter()
const { active, asMember, withPreview } = useDesignPreview()
const { toast, store, report } = useHouseholdFeedback()
const loading = ref(true)
const error = ref('')
const formOpen = ref(false)
const completeOpen = ref(false)
const confirmDelete = ref(false)
const restoreOpen = ref(false)
const busy = ref(false)

const itemId = computed(() => Number(route.params.id))
const item = computed(() => store.currentItem)
const status = computed(() => item.value ? itemStatus(item.value, store.calendarToday) : null)

async function load() {
  loading.value = true
  error.value = ''
  if (!Number.isFinite(itemId.value)) {
    error.value = '事项不存在'
    loading.value = false
    return
  }
  try {
    if (active.value) await store.loadWorkspace(true, asMember.value)
    else await store.loadWorkspace(false)
    await store.fetchItem(itemId.value)
    await Promise.all([
      store.fetchHistory(itemId.value),
      store.members.length ? Promise.resolve() : store.fetchMembers(),
      store.consumables.length ? Promise.resolve() : store.fetchConsumables(),
    ])
  } catch (cause) {
    error.value = (await report(cause)).message
  } finally {
    loading.value = false
  }
}

watch(() => route.params.id, () => { void load() }, { immediate: true })

async function togglePause() {
  if (!item.value || !store.isAdmin || busy.value) return
  const pausing = !item.value.isPaused
  busy.value = true
  try {
    await store.setPaused(item.value.id, pausing)
    toast.success(pausing ? '已暂停' : '已恢复')
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}

async function remove() {
  if (!item.value || !store.isAdmin || busy.value) return
  busy.value = true
  try {
    await store.removeItem(item.value.id)
    toast.success('已删除')
    await router.push(withPreview('/household'))
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
    confirmDelete.value = false
  }
}
</script>

<template>
  <div class="mx-auto w-full max-w-[860px] px-4 py-8 sm:px-8 lg:py-10">
    <RouterLink :to="withPreview('/household')" class="inline-flex items-center gap-1 text-[13px] text-[#4c6178] hover:underline">
      <IconArrowLeft :size="16" />返回家务周期
    </RouterLink>

    <p v-if="loading" class="mt-10 flex items-center text-[13px] text-[var(--mn-muted)]">
      <IconLoader2 :size="18" class="mr-2 animate-spin" />正在读取事项
    </p>
    <p v-else-if="error" role="alert" class="mt-8 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-4 py-3 text-[13px] text-[#9d3b34]">
      {{ error }}
      <button type="button" class="ml-3 text-[#4c6178] hover:underline" @click="load">重试</button>
    </p>

    <template v-else-if="item && status">
      <header class="mt-6 border-b border-[var(--mn-line)] pb-6">
        <p class="text-[11px] tracking-[0.14em] text-[var(--mn-muted)]">{{ categoryLabel(item.category) }} · {{ itemTypeLabel(item.itemType) }}</p>
        <div class="mt-2 flex flex-wrap items-center gap-3">
          <h1 class="font-serif text-2xl text-[var(--mn-ink)]">{{ item.name }}</h1>
          <HouseholdStatusPill :label="status.label" :tone="status.tone" />
        </div>
        <p class="mt-3 text-[14px] text-[var(--mn-ink)]">下次到期日 {{ formatCalendarDate(item.nextDueDate) }}</p>
        <p class="mt-1 text-[12px] text-[var(--mn-muted)]">由服务器计算，页面不推算周期。</p>
        <div class="mt-5 flex flex-wrap gap-2">
          <button type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white" @click="completeOpen = true">已完成</button>
          <button v-if="store.isAdmin && item.isArchived" type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="restoreOpen = true">恢复</button>
          <button v-if="store.isAdmin" type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="formOpen = true">编辑</button>
          <button v-if="store.isAdmin && !item.isArchived" type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="togglePause">{{ item.isPaused ? '恢复' : '暂停' }}</button>
          <button v-if="store.isAdmin" type="button" class="h-9 px-3 text-[13px] text-[#b4493f]" :disabled="busy" @click="confirmDelete = true">删除</button>
        </div>
      </header>

      <dl class="mt-6 grid gap-4 text-[13px] sm:grid-cols-2">
        <div><dt class="text-[var(--mn-muted)]">位置</dt><dd class="mt-1">{{ item.location || '—' }}</dd></div>
        <div><dt class="text-[var(--mn-muted)]">型号 / 规格</dt><dd class="mt-1">{{ item.modelSpec || '—' }}</dd></div>
        <div><dt class="text-[var(--mn-muted)]">周期</dt><dd class="mt-1">{{ item.itemType === 'Recurring' ? cycleLabel(item.cycleValue, item.cycleUnit) : '不自动顺延' }}</dd></div>
        <div><dt class="text-[var(--mn-muted)]">上次完成</dt><dd class="mt-1 tabular-nums">{{ formatCalendarDate(item.lastDoneDate) }}</dd></div>
        <div v-if="item.expiryDate"><dt class="text-[var(--mn-muted)]">到期日</dt><dd class="mt-1 tabular-nums">{{ formatCalendarDate(item.expiryDate) }}</dd></div>
        <div><dt class="text-[var(--mn-muted)]">提前提醒</dt><dd class="mt-1">{{ item.leadDays }} 天</dd></div>
        <div><dt class="text-[var(--mn-muted)]">负责人</dt><dd class="mt-1">{{ item.assigneeName || '未指定' }}</dd></div>
        <div><dt class="text-[var(--mn-muted)]">关联耗材</dt><dd class="mt-1">{{ store.consumableName(item.consumableId) || '无' }}</dd></div>
        <div v-if="item.mileageCycleKm"><dt class="text-[var(--mn-muted)]">里程周期</dt><dd class="mt-1">{{ item.mileageCycleKm }} km（只记录）</dd></div>
        <div v-if="item.aliases.length" class="sm:col-span-2"><dt class="text-[var(--mn-muted)]">别名</dt><dd class="mt-1">{{ item.aliases.join('、') }}</dd></div>
        <div v-if="item.note" class="sm:col-span-2"><dt class="text-[var(--mn-muted)]">备注</dt><dd class="mt-1 whitespace-pre-wrap">{{ item.note }}</dd></div>
        <div v-if="item.purchaseLink" class="sm:col-span-2">
          <dt class="text-[var(--mn-muted)]">购买链接</dt>
          <dd class="mt-1">
            <a v-if="safeHttpUrl(item.purchaseLink)" :href="safeHttpUrl(item.purchaseLink) || undefined" class="break-all text-[#4c6178] hover:underline" target="_blank" rel="noopener noreferrer">{{ item.purchaseLink }}</a>
            <span v-else class="break-all">{{ item.purchaseLink }}</span>
          </dd>
        </div>
      </dl>

      <section class="mt-10">
        <h2 class="font-serif text-[18px] text-[var(--mn-ink)]">完成记录</h2>
        <p v-if="!store.history.length" class="mt-4 text-[13px] text-[var(--mn-muted)]">还没有完成记录。</p>
        <ol v-else class="mt-4 divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
          <li v-for="record in store.history" :key="record.id" class="py-4">
            <p class="text-[14px] text-[var(--mn-ink)]">
              <span class="tabular-nums">{{ formatCalendarDate(record.completedOn) }}</span>
              <span class="text-[13px] text-[var(--mn-muted)]"> · {{ record.completedByUsername }} 完成</span>
            </p>
            <p class="mt-1 text-[12px] text-[var(--mn-muted)]">
              <template v-if="record.cost != null">费用 {{ formatCost(record.cost) }} · </template>
              <template v-if="record.consumableQuantityDeducted">扣减库存 {{ record.consumableQuantityDeducted }} · </template>
              <template v-if="record.newExpiryDate">续期至 {{ formatCalendarDate(record.newExpiryDate) }} · </template>
              记录于 {{ formatAccountDateTime(record.createdAt) }}
            </p>
            <p v-if="record.note" class="mt-2 whitespace-pre-wrap text-[13px]">{{ record.note }}</p>
            <a v-if="safeHttpUrl(record.purchaseLink)" :href="safeHttpUrl(record.purchaseLink) || undefined" class="mt-1 inline-block text-[12px] text-[#4c6178] hover:underline" target="_blank" rel="noopener noreferrer">购买链接</a>
            <span v-else-if="record.purchaseLink" class="mt-1 inline-block break-all text-[12px] text-[var(--mn-muted)]">{{ record.purchaseLink }}</span>
            <div v-if="record.photoRefs.length" class="mt-3 grid grid-cols-4 gap-2 sm:grid-cols-6">
              <a v-for="(path, index) in record.photoRefs" :key="`${record.id}-${index}`" :href="staticUrl(path)" target="_blank" rel="noopener noreferrer">
                <img :src="staticUrl(path)" class="aspect-square w-full rounded-md border border-[var(--mn-line)] object-cover" :alt="`${item.name} 的完成照片 ${index + 1}`" />
              </a>
            </div>
          </li>
        </ol>
      </section>
    </template>

    <HouseholdItemFormDialog :open="formOpen" mode="edit" :item="item" @close="formOpen = false" @saved="load" />
    <HouseholdCompleteDialog :open="completeOpen" :item-id="item?.id ?? null" @close="completeOpen = false" @completed="load" @refresh="load" />
    <HouseholdRestoreDialog :open="restoreOpen" :item="item" @close="restoreOpen = false" @restored="load" />
    <AppDialog :open="confirmDelete" title="删除事项" :description="item ? `确定删除「${item.name}」？` : ''" :busy="busy" @close="confirmDelete = false">
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="confirmDelete = false">取消</button>
        <button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="remove">删除</button>
      </template>
    </AppDialog>
  </div>
</template>
