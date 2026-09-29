<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { IconLoader2, IconPlus } from '@tabler/icons-vue'
import AppDialog from '@/components/AppDialog.vue'
import HouseholdCompleteDialog from '@/components/household/HouseholdCompleteDialog.vue'
import HouseholdConsumablePanel from '@/components/household/HouseholdConsumablePanel.vue'
import HouseholdItemFormDialog from '@/components/household/HouseholdItemFormDialog.vue'
import HouseholdMemberPanel from '@/components/household/HouseholdMemberPanel.vue'
import HouseholdRestoreDialog from '@/components/household/HouseholdRestoreDialog.vue'
import HouseholdStatusPill from '@/components/household/HouseholdStatusPill.vue'
import { useDesignPreview } from '@/composables/useDesignPreview'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { HouseholdCategory, HouseholdItem } from '@/types/household'
import {
  HOUSEHOLD_CATEGORIES,
  categoryLabel,
  cycleLabel,
  formatCalendarDate,
  itemStatus,
  roleLabel,
} from '@/utils/householdFormat'

const { active, asMember, withPreview } = useDesignPreview()
const { toast, store, report } = useHouseholdFeedback()
const section = ref<'items' | 'stock' | 'members'>('items')
const category = ref<HouseholdCategory | ''>('')
const includePaused = ref(true)
const listScope = ref<'active' | 'archived'>('active')
const menuId = ref<number | null>(null)
const restoring = ref<HouseholdItem | null>(null)
const pageError = ref('')
const formOpen = ref(false)
const formMode = ref<'create' | 'template' | 'edit'>('template')
const editing = ref<HouseholdItem | null>(null)
const completeId = ref<number | null>(null)
const deleting = ref<HouseholdItem | null>(null)
const busy = ref(false)
const ready = ref(false)

function query() {
  return {
    category: category.value || undefined,
    includePaused: includePaused.value,
    archivedOnly: listScope.value === 'archived',
  }
}

function closeMenu() {
  menuId.value = null
}

function toggleMenu(id: number) {
  menuId.value = menuId.value === id ? null : id
}

function onDocumentClick() {
  menuId.value = null
}

watch(menuId, (id) => {
  document.removeEventListener('click', onDocumentClick)
  if (id != null) document.addEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))

async function load() {
  pageError.value = ''
  try {
    await store.loadWorkspace(active.value, asMember.value)
    await store.fetchItems(query())
  } catch (error) {
    pageError.value = (await report(error)).message
  } finally {
    ready.value = true
  }
}

async function reloadItems() {
  if (!ready.value) return
  try {
    await store.fetchItems(query())
    pageError.value = ''
  } catch (error) {
    pageError.value = (await report(error)).message
  }
}

onMounted(load)
watch([category, includePaused, listScope], () => {
  closeMenu()
  void reloadItems()
})

function openCreate(mode: 'create' | 'template') {
  editing.value = null
  formMode.value = mode
  formOpen.value = true
}

function openEdit(item: HouseholdItem) {
  if (!store.isAdmin) return
  editing.value = item
  formMode.value = 'edit'
  formOpen.value = true
}

async function togglePause(item: HouseholdItem) {
  if (!store.isAdmin || busy.value) return
  busy.value = true
  try {
    await store.setPaused(item.id, !item.isPaused)
    toast.success(item.isPaused ? '已恢复' : '已暂停')
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function confirmDelete() {
  if (!deleting.value || !store.isAdmin || busy.value) return
  busy.value = true
  try {
    await store.removeItem(deleting.value.id)
    toast.success('已删除')
    deleting.value = null
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

function statusOf(item: HouseholdItem) {
  return itemStatus(item, store.calendarToday)
}
</script>

<template>
  <div class="mx-auto w-full max-w-[1160px] px-4 py-8 sm:px-8 lg:py-10">
    <div class="mb-6 flex flex-wrap items-end justify-between gap-4 border-b border-[var(--mn-line)] pb-6">
      <div>
        <p class="mb-2 text-[11px] font-medium tracking-[0.17em] text-[var(--mn-muted)]">MIRAI / HOUSEHOLD</p>
        <h1 class="font-serif text-2xl text-[var(--mn-ink)] sm:text-[28px]">家务周期</h1>
        <p class="mt-2 max-w-2xl text-[13px] leading-6 text-[#68665f]">{{ store.household?.name || '我的家庭' }}<span v-if="store.household"> · {{ roleLabel(store.household.myRole) }}</span>。下次到期日由服务器计算，日期按北京时间展示。</p>
      </div>
      <div class="flex flex-wrap gap-2">
        <button type="button" class="inline-flex h-10 items-center gap-2 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] font-medium text-white hover:bg-[var(--mn-indigo-dark)]" @click="openCreate('template')">
          <IconPlus :size="16" />从模板新建
        </button>
        <button type="button" class="h-10 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" @click="openCreate('create')">自己填写</button>
      </div>
    </div>

    <div class="mb-6 flex gap-2 text-[13px]">
      <button type="button" class="h-9 rounded-md px-3" :class="section === 'items' ? 'bg-[#edf0f2] text-[#384b60]' : 'text-[var(--mn-muted)]'" :aria-pressed="section === 'items'" @click="section = 'items'">事项</button>
      <button type="button" class="h-9 rounded-md px-3" :class="section === 'stock' ? 'bg-[#edf0f2] text-[#384b60]' : 'text-[var(--mn-muted)]'" :aria-pressed="section === 'stock'" @click="section = 'stock'">耗材</button>
      <button type="button" class="h-9 rounded-md px-3" :class="section === 'members' ? 'bg-[#edf0f2] text-[#384b60]' : 'text-[var(--mn-muted)]'" :aria-pressed="section === 'members'" @click="section = 'members'">成员</button>
    </div>

    <p v-if="pageError" role="alert" class="mb-4 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-4 py-3 text-[13px] text-[#9d3b34]">
      {{ pageError }}
      <button type="button" class="ml-3 text-[#4c6178] hover:underline" @click="load">重试</button>
    </p>

    <div v-if="!ready" class="flex h-40 items-center justify-center text-[13px] text-[var(--mn-muted)]">
      <IconLoader2 :size="18" class="mr-2 animate-spin" />正在读取家务周期
    </div>

    <section v-else-if="section === 'items'">
      <div class="mb-4 flex flex-wrap items-center gap-3 text-[13px]">
        <label>
          <span class="sr-only">分类</span>
          <select v-model="category" class="form-input h-10 w-auto">
            <option value="">全部分类</option>
            <option v-for="item in HOUSEHOLD_CATEGORIES" :key="item.value" :value="item.value">{{ item.label }}</option>
          </select>
        </label>
        <label>
          <span class="sr-only">归档</span>
          <select v-model="listScope" class="form-input h-10 w-auto">
            <option value="active">未归档</option>
            <option value="archived">已归档</option>
          </select>
        </label>
        <label class="inline-flex items-center gap-2 text-[var(--mn-muted)]">
          <input v-model="includePaused" type="checkbox" />
          显示已暂停
        </label>
      </div>

      <p v-if="!store.items.length" class="rounded-md border border-dashed border-[var(--mn-line)] px-4 py-14 text-center text-[13px] text-[var(--mn-muted)]">
        {{ listScope === 'archived' ? '没有已归档的事项。一次性事项完成且不续期后会出现在这里。' : '还没有事项。从模板新建，填上上次完成日期，通常半分钟内就能建好。' }}
      </p>
      <ul v-else class="divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
        <li v-for="item in store.items" :key="item.id" class="flex flex-wrap items-start gap-4 py-4" :class="item.isPaused && !item.isArchived ? 'opacity-70' : ''">
          <div class="min-w-0 flex-1">
            <RouterLink :to="withPreview(`/household/items/${item.id}`)" class="text-[15px] text-[var(--mn-ink)] hover:text-[#384b60]">{{ item.name }}</RouterLink>
            <p class="mt-1 text-[12px] text-[var(--mn-muted)]">
              {{ categoryLabel(item.category) }}
              · {{ item.itemType === 'OneOffExpiry' ? '一次性到期' : cycleLabel(item.cycleValue, item.cycleUnit) }}
              <template v-if="item.location"> · {{ item.location }}</template>
              <template v-if="item.assigneeName"> · {{ item.assigneeName }}</template>
            </p>
            <p class="mt-1 flex flex-wrap items-center gap-3 text-[12px]">
              <span class="tabular-nums text-[var(--mn-ink)]">下次到期 {{ formatCalendarDate(item.nextDueDate) }}</span>
              <HouseholdStatusPill :label="statusOf(item).label" :tone="statusOf(item).tone" />
            </p>
          </div>
          <div class="flex shrink-0 items-center gap-2">
            <button type="button" class="h-8 rounded-md bg-[var(--mn-indigo)] px-3 text-[12px] text-white" :disabled="busy" @click="completeId = item.id">已完成</button>
            <div v-if="store.isAdmin" class="hidden items-center gap-2 sm:flex">
              <button v-if="item.isArchived" type="button" class="h-8 rounded-md border border-[var(--mn-line)] px-3 text-[12px]" :disabled="busy" @click="restoring = item">恢复</button>
              <button type="button" class="h-8 rounded-md border border-[var(--mn-line)] px-3 text-[12px]" :disabled="busy" @click="openEdit(item)">编辑</button>
              <button v-if="!item.isArchived" type="button" class="h-8 rounded-md border border-[var(--mn-line)] px-3 text-[12px]" :disabled="busy" @click="togglePause(item)">{{ item.isPaused ? '恢复' : '暂停' }}</button>
              <button type="button" class="h-8 px-2 text-[12px] text-[#b4493f]" :disabled="busy" @click="deleting = item">删除</button>
            </div>
            <div v-if="store.isAdmin" class="relative sm:hidden">
              <button
                type="button"
                class="flex h-8 w-8 items-center justify-center rounded-md border border-[var(--mn-line)] text-[16px] leading-none"
                :aria-label="`更多操作：${item.name}`"
                aria-haspopup="menu"
                :aria-expanded="menuId === item.id"
                :disabled="busy"
                @click.stop="toggleMenu(item.id)"
              >…</button>
              <div v-if="menuId === item.id" role="menu" class="absolute right-0 top-9 z-20 min-w-[8.5rem] rounded-md border border-[var(--mn-line)] bg-white py-1 shadow-md" @click.stop>
                <button v-if="item.isArchived" type="button" role="menuitem" class="block w-full px-3 py-2 text-left text-[13px] hover:bg-[#f4f1eb]" @click="restoring = item; closeMenu()">恢复</button>
                <button type="button" role="menuitem" class="block w-full px-3 py-2 text-left text-[13px] hover:bg-[#f4f1eb]" @click="openEdit(item); closeMenu()">编辑</button>
                <button v-if="!item.isArchived" type="button" role="menuitem" class="block w-full px-3 py-2 text-left text-[13px] hover:bg-[#f4f1eb]" @click="togglePause(item); closeMenu()">{{ item.isPaused ? '恢复' : '暂停' }}</button>
                <button type="button" role="menuitem" class="block w-full px-3 py-2 text-left text-[13px] text-[#b4493f] hover:bg-[#fff5f3]" @click="deleting = item; closeMenu()">删除</button>
              </div>
            </div>
          </div>
        </li>
      </ul>
    </section>

    <HouseholdConsumablePanel v-else-if="section === 'stock'" />
    <HouseholdMemberPanel v-else />

    <HouseholdItemFormDialog :open="formOpen" :mode="formMode" :item="editing" @close="formOpen = false" @saved="reloadItems" />
    <HouseholdCompleteDialog :open="completeId != null" :item-id="completeId" @close="completeId = null" @completed="reloadItems" @refresh="reloadItems" />
    <HouseholdRestoreDialog :open="restoring != null" :item="restoring" @close="restoring = null" @restored="reloadItems" />
    <AppDialog :open="deleting != null" title="删除事项" :description="deleting ? `确定删除「${deleting.name}」？此操作需要管理员权限。` : ''" :busy="busy" @close="deleting = null">
      <p class="text-[13px] leading-6">删除后事项不再出现在列表和近期到期里。</p>
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="deleting = null">取消</button>
        <button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="confirmDelete">删除</button>
      </template>
    </AppDialog>
  </div>
</template>
