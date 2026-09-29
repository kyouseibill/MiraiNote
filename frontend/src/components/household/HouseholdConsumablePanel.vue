<script setup lang="ts">
import { ref } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import { draftText, purchaseLinkError, safeHttpUrl } from '@/utils/householdFormat'
import type { HouseholdConsumable } from '@/types/household'

const { toast, store, report } = useHouseholdFeedback()
const open = ref(false)
const editingId = ref<number | null>(null)
const name = ref('')
const specModel = ref('')
const stock = ref('0')
const threshold = ref('1')
const unit = ref('')
const purchaseLink = ref('')
const note = ref('')
const quantities = ref<Record<number, string | number>>({})
const busy = ref(false)
const formError = ref('')
const linkError = ref('')
const deletingId = ref<number | null>(null)

function resetForm() {
  name.value = ''
  specModel.value = ''
  stock.value = '0'
  threshold.value = '1'
  unit.value = ''
  purchaseLink.value = ''
  note.value = ''
  formError.value = ''
  linkError.value = ''
  editingId.value = null
}

function openCreate() {
  resetForm()
  open.value = true
}

function openEdit(item: HouseholdConsumable) {
  editingId.value = item.id
  name.value = item.name
  specModel.value = item.specModel ?? ''
  stock.value = String(item.currentStock)
  threshold.value = String(item.restockThreshold)
  unit.value = item.unit ?? ''
  purchaseLink.value = item.purchaseLink ?? ''
  note.value = item.note ?? ''
  formError.value = ''
  linkError.value = ''
  open.value = true
}

async function createConsumable() {
  formError.value = ''
  linkError.value = purchaseLinkError(purchaseLink.value)
  const trimmed = name.value.trim()
  if (!trimmed) {
    formError.value = '请填写耗材名称'
    return
  }
  const editing = editingId.value != null
  if (!editing && !/^\d+$/.test(draftText(stock.value))) {
    formError.value = '库存需为 0 或正整数'
    return
  }
  if (!/^\d+$/.test(draftText(threshold.value))) {
    formError.value = '补货阈值需为 0 或正整数'
    return
  }
  if (linkError.value) return
  busy.value = true
  try {
    const fields = {
      name: trimmed,
      specModel: specModel.value.trim() || null,
      restockThreshold: Number(threshold.value),
      unit: unit.value.trim() || null,
      purchaseLink: purchaseLink.value.trim() || null,
      note: note.value.trim() || null,
    }
    if (!editing) await store.createConsumable({ ...fields, currentStock: Number(stock.value) })
    else await store.updateConsumable(editingId.value!, fields)
    toast.success(editingId.value == null ? '已添加耗材' : '已更新耗材')
    open.value = false
    resetForm()
  } catch (error) {
    const failure = await report(error)
    formError.value = failure.message
  } finally {
    busy.value = false
  }
}

async function restock(id: number) {
  const raw = draftText(quantities.value[id] ?? '1')
  if (!/^[1-9]\d*$/.test(raw)) {
    toast.error('补货数量必须大于 0')
    return
  }
  busy.value = true
  try {
    await store.restockConsumable(id, Number(raw))
    quantities.value[id] = '1'
    toast.success('已补货')
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function remove() {
  if (deletingId.value == null || !store.isAdmin) return
  busy.value = true
  try {
    await store.removeConsumable(deletingId.value)
    toast.success('已删除耗材')
    deletingId.value = null
  } catch (error) {
    await report(error)
    deletingId.value = null
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <div class="mb-4 flex items-center justify-between gap-3">
      <p class="text-[12px] text-[var(--mn-muted)]">库存小于或等于补货阈值时标为需补货。补货数量会加到当前库存上。</p>
      <button type="button" class="h-9 shrink-0 rounded-md bg-[var(--mn-indigo)] px-3 text-[13px] text-white" @click="openCreate">添加耗材</button>
    </div>
    <p v-if="!store.consumables.length" class="rounded-md border border-dashed border-[var(--mn-line)] px-4 py-10 text-center text-[13px] text-[var(--mn-muted)]">还没有耗材。</p>
    <ul v-else class="divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
      <li v-for="item in store.consumables" :key="item.id" class="flex flex-wrap items-center gap-3 py-4">
        <div class="min-w-[180px] flex-1">
          <p class="text-[14px] text-[var(--mn-ink)]">{{ item.name }}</p>
          <p class="mt-1 text-[12px] text-[var(--mn-muted)]">
            <span v-if="item.specModel">{{ item.specModel }} · </span>
            库存 {{ item.currentStock }}{{ item.unit || '' }} · 阈值 {{ item.restockThreshold }}
            <span v-if="item.isLowStock" class="ml-2 text-[#b4493f]">需补货</span>
          </p>
          <a v-if="safeHttpUrl(item.purchaseLink)" :href="safeHttpUrl(item.purchaseLink) || undefined" class="mt-1 inline-block break-all text-[12px] text-[#4c6178] hover:underline" target="_blank" rel="noopener noreferrer">{{ safeHttpUrl(item.purchaseLink) }}</a>
          <span v-else-if="item.purchaseLink" class="mt-1 inline-block break-all text-[12px] text-[var(--mn-muted)]">{{ item.purchaseLink }}</span>
        </div>
        <form class="flex items-center gap-2" @submit.prevent="restock(item.id)">
          <label class="sr-only" :for="`restock-${item.id}`">补货数量</label>
          <input :id="`restock-${item.id}`" v-model="quantities[item.id]" type="number" min="1" step="1" class="form-input h-9 w-20" placeholder="1" :disabled="busy" />
          <button type="submit" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[13px]" :disabled="busy">补货</button>
        </form>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[12px]" :disabled="busy" @click="openEdit(item)">编辑</button>
        <button v-if="store.isAdmin" type="button" class="h-9 px-2 text-[12px] text-[#b4493f] hover:underline" :disabled="busy" @click="deletingId = item.id">删除</button>
      </li>
    </ul>

    <AppDialog :open="open" :title="editingId == null ? '添加耗材' : '编辑耗材'" :description="editingId == null ? '名称和当前库存就够用，阈值默认 1。' : '可以改名称、规格、阈值、单位、购买链接和备注。库存不能在这里改。'" :busy="busy" @close="open = false">
      <p v-if="formError" role="alert" class="mb-3 text-[12px] text-[#9d3b34]">{{ formError }}</p>
      <form class="space-y-3" @submit.prevent="createConsumable">
        <div>
          <label class="text-[13px] font-medium" for="consumable-name">名称</label>
          <input id="consumable-name" v-model="name" data-dialog-autofocus class="form-input mt-1.5 h-10" :disabled="busy" />
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div v-if="editingId == null">
            <label class="text-[13px] font-medium" for="consumable-stock">当前库存</label>
            <input id="consumable-stock" v-model="stock" type="number" min="0" step="1" class="form-input mt-1.5 h-10" :disabled="busy" />
          </div>
          <div v-else>
            <p class="text-[13px] font-medium">当前库存</p>
            <p id="consumable-stock" class="mt-1.5 flex h-10 items-center text-[14px] tabular-nums text-[var(--mn-ink)]">{{ stock }}<span class="ml-2 text-[12px] font-normal text-[var(--mn-muted)]">只读</span></p>
          </div>
          <div>
            <label class="text-[13px] font-medium" for="consumable-threshold">补货阈值</label>
            <input id="consumable-threshold" v-model="threshold" type="number" min="0" step="1" class="form-input mt-1.5 h-10" :disabled="busy" />
          </div>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="text-[13px] font-medium" for="consumable-unit">单位</label>
            <input id="consumable-unit" v-model="unit" class="form-input mt-1.5 h-10" placeholder="片、支" :disabled="busy" />
          </div>
          <div>
            <label class="text-[13px] font-medium" for="consumable-spec">规格</label>
            <input id="consumable-spec" v-model="specModel" class="form-input mt-1.5 h-10" :disabled="busy" />
          </div>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="consumable-link">购买链接</label>
          <input id="consumable-link" v-model="purchaseLink" class="form-input mt-1.5 h-10" maxlength="500" :disabled="busy" />
          <p v-if="linkError" class="mt-1 text-[12px] text-[#9d3b34]">{{ linkError }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="consumable-note">备注</label>
          <textarea id="consumable-note" v-model="note" rows="2" class="form-input mt-1.5 resize-none py-2" :disabled="busy" />
        </div>
      </form>
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="open = false">取消</button>
        <button type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="createConsumable">{{ editingId == null ? '保存' : '更新' }}</button>
      </template>
    </AppDialog>

    <AppDialog :open="deletingId != null" title="删除耗材" description="仍被事项引用时无法删除。" :busy="busy" @close="deletingId = null">
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="deletingId = null">取消</button>
        <button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="remove">删除</button>
      </template>
    </AppDialog>
  </div>
</template>
