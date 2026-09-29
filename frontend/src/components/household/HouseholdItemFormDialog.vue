<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { HouseholdCategory, HouseholdCycleUnit, HouseholdItem, HouseholdItemTemplate, HouseholdItemType } from '@/types/household'
import {
  CYCLE_UNITS,
  HOUSEHOLD_CATEGORIES,
  categoryLabel,
  cycleLabel,
  itemTypeLabel,
  draftText,
  parseAliases,
  purchaseLinkError,
  shanghaiToday,
  validateItemDraft,
} from '@/utils/householdFormat'

const props = defineProps<{
  open: boolean
  mode: 'create' | 'template' | 'edit'
  item?: HouseholdItem | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const { toast, store, report } = useHouseholdFeedback()
const localMode = ref<'create' | 'template' | 'edit'>('create')
const selectedTemplate = ref<HouseholdItemTemplate | null>(null)
const showMore = ref(false)
const submitting = ref(false)
const serverError = ref('')
const errors = ref<Record<string, string>>({})
const dateInput = ref<HTMLInputElement | null>(null)

const form = reactive({
  name: '',
  category: 'HomeMaintenance' as HouseholdCategory,
  location: '',
  modelSpec: '',
  itemType: 'Recurring' as HouseholdItemType,
  cycleValue: '',
  cycleUnit: 'Month' as HouseholdCycleUnit | '',
  lastDoneDate: '',
  expiryDate: '',
  leadDays: '7',
  assigneeMemberId: '',
  consumableId: '',
  note: '',
  purchaseLink: '',
  mileageCycleKm: '',
  aliases: '',
})

const today = ref(shanghaiToday())

async function refreshToday() {
  if (!store.previewMode) await store.fetchServerToday()
  today.value = store.calendarToday
}

const groupedTemplates = computed(() => HOUSEHOLD_CATEGORIES
  .map((category) => ({
    ...category,
    templates: store.templates.filter((template) => template.category === category.value),
  }))
  .filter((group) => group.templates.length > 0))

const title = computed(() => {
  if (localMode.value === 'edit') return '编辑事项'
  if (localMode.value === 'template' && !selectedTemplate.value) return '从模板新建'
  if (localMode.value === 'template') return '确认模板'
  return '新建事项'
})

const description = computed(() => {
  if (localMode.value === 'template' && !selectedTemplate.value) return '选一个模板就会带出推荐周期。周期事项再确认上次完成日期即可。'
  if (form.itemType === 'OneOffExpiry') return '一次性到期不会自动顺延。到期日保存后由服务器作为下次到期日。'
  return '下次到期日在保存后由服务器计算，这里只填写上次完成日期和周期。'
})

function blank() {
  form.name = ''
  form.category = 'HomeMaintenance'
  form.location = ''
  form.modelSpec = ''
  form.itemType = 'Recurring'
  form.cycleValue = ''
  form.cycleUnit = 'Month'
  form.lastDoneDate = today.value
  form.expiryDate = ''
  form.leadDays = '7'
  form.assigneeMemberId = ''
  form.consumableId = ''
  form.note = ''
  form.purchaseLink = ''
  form.mileageCycleKm = ''
  form.aliases = ''
}

function fillFromItem(item: HouseholdItem) {
  form.name = item.name
  form.category = item.category
  form.location = item.location ?? ''
  form.modelSpec = item.modelSpec ?? ''
  form.itemType = item.itemType
  form.cycleValue = item.cycleValue != null ? String(item.cycleValue) : ''
  form.cycleUnit = item.cycleUnit ?? 'Month'
  form.lastDoneDate = item.lastDoneDate ?? today.value
  form.expiryDate = item.expiryDate ?? ''
  form.leadDays = String(item.leadDays ?? 7)
  form.assigneeMemberId = item.assigneeMemberId != null ? String(item.assigneeMemberId) : ''
  form.consumableId = item.consumableId != null ? String(item.consumableId) : ''
  form.note = item.note ?? ''
  form.purchaseLink = item.purchaseLink ?? ''
  form.mileageCycleKm = item.mileageCycleKm != null ? String(item.mileageCycleKm) : ''
  form.aliases = item.aliases.join('，')
}

async function applyTemplate(template: HouseholdItemTemplate) {
  selectedTemplate.value = template
  form.name = template.name
  form.category = template.category
  form.itemType = template.itemType
  form.cycleValue = template.cycleValue != null ? String(template.cycleValue) : ''
  form.cycleUnit = template.cycleUnit ?? 'Month'
  await refreshToday()
  form.lastDoneDate = template.itemType === 'Recurring' ? today.value : ''
  form.expiryDate = ''
  errors.value = {}
  await nextTick()
  dateInput.value?.focus()
}

function detachTemplate() {
  selectedTemplate.value = null
  localMode.value = 'create'
}

async function prepareDialog() {
  await refreshToday()
  localMode.value = props.mode
  selectedTemplate.value = null
  showMore.value = props.mode === 'edit'
  serverError.value = ''
  errors.value = {}
  blank()
  if (props.mode === 'edit' && props.item) fillFromItem(props.item)
  if (!store.previewMode) {
    try {
      await Promise.all([store.fetchTemplates(), store.fetchMembers(), store.fetchConsumables()])
    } catch (error) {
      const failure = await report(error)
      serverError.value = failure.message
    }
  }
}

watch(() => props.open, (openDialog) => {
  if (openDialog) void prepareDialog()
})

function extraErrors() {
  const next: Record<string, string> = {}
  if (form.location.trim().length > 200) next.location = '位置不能超过 200 个字符'
  if (form.note.trim().length > 2000) next.note = '备注不能超过 2000 个字符'
  const linkError = purchaseLinkError(form.purchaseLink)
  if (linkError) next.purchaseLink = linkError
  const aliases = parseAliases(form.aliases)
  if (aliases.length > 20) next.aliases = '别名最多 20 个'
  if (aliases.some((alias) => alias.length > 50)) next.aliases = '单个别名不能超过 50 个字符'
  return next
}

async function submit() {
  if (submitting.value) return
  if (localMode.value === 'edit' && !store.isAdmin) {
    serverError.value = '只有管理员可以编辑事项'
    await report(new Error('只有管理员可以编辑事项'))
    return
  }
  serverError.value = ''
  await refreshToday()
  const draftErrors = validateItemDraft({
    name: form.name,
    itemType: form.itemType,
    cycleValue: form.cycleValue,
    cycleUnit: form.cycleUnit,
    lastDoneDate: form.lastDoneDate,
    expiryDate: form.expiryDate,
    leadDays: form.leadDays,
    mileageCycleKm: form.category === 'Vehicle' ? form.mileageCycleKm : '',
  }, today.value)
  errors.value = {
    ...draftErrors,
    ...extraErrors(),
  }
  if (Object.keys(errors.value).length) {
    showMore.value = true
    return
  }

  const recurring = form.itemType === 'Recurring'
  const shared = {
    name: form.name.trim(),
    category: form.category,
    location: form.location.trim() || null,
    modelSpec: form.modelSpec.trim() || null,
    cycleValue: recurring ? Number(form.cycleValue) : null,
    cycleUnit: recurring && form.cycleUnit ? form.cycleUnit : null,
    lastDoneDate: recurring ? form.lastDoneDate : props.item?.lastDoneDate ?? null,
    expiryDate: recurring ? null : form.expiryDate,
    leadDays: draftText(form.leadDays) ? Number(form.leadDays) : 7,
    assigneeMemberId: form.assigneeMemberId ? Number(form.assigneeMemberId) : null,
    consumableId: form.consumableId ? Number(form.consumableId) : null,
    note: form.note.trim() || null,
    purchaseLink: form.purchaseLink.trim() || null,
    mileageCycleKm: form.category === 'Vehicle' && draftText(form.mileageCycleKm) ? Number(form.mileageCycleKm) : null,
    aliases: parseAliases(form.aliases),
  }

  submitting.value = true
  try {
    if (localMode.value === 'edit' && props.item) {
      await store.updateItem(props.item.id, { ...shared, itemType: form.itemType })
      toast.success(store.previewMode ? '设计预览已保存，到期日仍以示例数据为准' : '已保存')
    } else if (localMode.value === 'template' && selectedTemplate.value) {
      await store.createFromTemplate({ templateId: selectedTemplate.value.id, ...shared })
      toast.success(store.previewMode ? '设计预览已创建。到期日不会在浏览器里计算' : '已创建')
    } else {
      await store.createItem({ ...shared, itemType: form.itemType, isPaused: false })
      toast.success(store.previewMode ? '设计预览已创建。到期日不会在浏览器里计算' : '已创建')
    }
    emit('saved')
    emit('close')
  } catch (error) {
    const failure = await report(error)
    serverError.value = failure.message
    if (failure.status === 403) emit('close')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppDialog :open="open" :title="title" :description="description" size="lg" :busy="submitting" @close="emit('close')">
    <p v-if="serverError" role="alert" class="mb-4 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-3 py-2 text-[12px] text-[#9d3b34]">{{ serverError }}</p>

    <div v-if="localMode === 'template' && !selectedTemplate" class="space-y-5">
      <p v-if="!groupedTemplates.length" class="text-[13px] text-[var(--mn-muted)]">还没有模板。</p>
      <section v-for="group in groupedTemplates" :key="group.value">
        <h3 class="text-[11px] font-medium tracking-[0.14em] text-[var(--mn-muted)]">{{ group.label }}</h3>
        <div class="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3">
          <button
            v-for="template in group.templates"
            :key="template.id"
            type="button"
            class="rounded-md border border-[var(--mn-line)] bg-white/70 px-3 py-2 text-left hover:border-[#8d9bab]"
            @click="applyTemplate(template)"
          >
            <span class="block text-[13px] text-[var(--mn-ink)]">{{ template.name }}</span>
            <span class="mt-0.5 block text-[11px] text-[var(--mn-muted)]">{{ cycleLabel(template.cycleValue, template.cycleUnit) }}</span>
          </button>
        </div>
      </section>
    </div>

    <form v-else class="space-y-4" @submit.prevent="submit">
      <div v-if="selectedTemplate" class="flex flex-wrap items-center justify-between gap-2 rounded-md bg-[#f4f1eb] px-3 py-2 text-[12px]">
        <span>模板：{{ selectedTemplate.name }} · {{ cycleLabel(selectedTemplate.cycleValue, selectedTemplate.cycleUnit) }}</span>
        <button type="button" class="text-[#4c6178] hover:underline" @click="selectedTemplate = null">更换模板</button>
      </div>
      <div>
        <label class="text-[13px] font-medium" for="item-name">名称</label>
        <input id="item-name" v-model="form.name" class="form-input mt-1.5 h-10" maxlength="100" :disabled="submitting" />
        <p v-if="errors.name" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.name }}</p>
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="text-[13px] font-medium" for="item-category">分类</label>
          <select id="item-category" v-model="form.category" class="form-input mt-1.5 h-10" :disabled="submitting">
            <option v-for="category in HOUSEHOLD_CATEGORIES" :key="category.value" :value="category.value">{{ category.label }}</option>
          </select>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-type">类型</label>
          <select v-if="localMode !== 'template'" id="item-type" v-model="form.itemType" class="form-input mt-1.5 h-10" :disabled="submitting">
            <option value="Recurring">周期</option>
            <option value="OneOffExpiry">一次性到期</option>
          </select>
          <p v-else id="item-type" class="mt-1.5 flex h-10 items-center text-[13px]">{{ itemTypeLabel(form.itemType) }} · {{ categoryLabel(form.category) }}</p>
        </div>
      </div>

      <div v-if="form.itemType === 'Recurring'" class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="text-[13px] font-medium" for="item-cycle">周期</label>
          <div class="mt-1.5 flex gap-2">
            <input id="item-cycle" v-model="form.cycleValue" type="number" min="1" step="1" class="form-input h-10" :disabled="submitting" />
            <select v-model="form.cycleUnit" class="form-input h-10" aria-label="周期单位" :disabled="submitting">
              <option v-for="unit in CYCLE_UNITS" :key="unit.value" :value="unit.value">{{ unit.label }}</option>
            </select>
          </div>
          <p v-if="errors.cycleValue || errors.cycleUnit" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.cycleValue || errors.cycleUnit }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-last-done">上次完成日期</label>
          <input id="item-last-done" ref="dateInput" v-model="form.lastDoneDate" data-dialog-autofocus type="date" class="form-input mt-1.5 h-10" :max="today" :disabled="submitting" />
          <p class="mt-1 text-[11px] text-[var(--mn-muted)]">不能晚于今天（北京时间）。</p>
          <p v-if="errors.lastDoneDate" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.lastDoneDate }}</p>
        </div>
      </div>
      <div v-else>
        <label class="text-[13px] font-medium" for="item-expiry">到期日</label>
        <input id="item-expiry" ref="dateInput" v-model="form.expiryDate" data-dialog-autofocus type="date" class="form-input mt-1.5 h-10" :disabled="submitting" />
        <p class="mt-1 text-[11px] text-[var(--mn-muted)]">可以填写过去的日期，用来补录已经过期的证件或保修。</p>
        <p v-if="errors.expiryDate" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.expiryDate }}</p>
      </div>

      <div>
        <label class="text-[13px] font-medium" for="item-location">位置</label>
        <input id="item-location" v-model="form.location" class="form-input mt-1.5 h-10" maxlength="200" placeholder="可不填" :disabled="submitting" />
        <p v-if="errors.location" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.location }}</p>
      </div>

      <button type="button" class="text-[12px] text-[#4c6178] hover:underline" @click="showMore = !showMore">
        {{ showMore ? '收起更多选项' : '更多选项' }}
      </button>
      <div v-if="showMore" class="space-y-4 border-t border-[var(--mn-line)] pt-4">
        <div>
          <label class="text-[13px] font-medium" for="item-model">型号 / 规格</label>
          <input id="item-model" v-model="form.modelSpec" class="form-input mt-1.5 h-10" maxlength="200" :disabled="submitting" />
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="text-[13px] font-medium" for="item-lead">提前提醒天数</label>
            <input id="item-lead" v-model="form.leadDays" type="number" min="0" step="1" class="form-input mt-1.5 h-10" :disabled="submitting" />
            <p v-if="errors.leadDays" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.leadDays }}</p>
          </div>
          <div>
            <label class="text-[13px] font-medium" for="item-assignee">负责人</label>
            <select id="item-assignee" v-model="form.assigneeMemberId" class="form-input mt-1.5 h-10" :disabled="submitting">
              <option value="">不指定</option>
              <option v-for="member in store.members" :key="member.id" :value="String(member.id)">{{ member.username }}</option>
            </select>
          </div>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-consumable">关联耗材</label>
          <select id="item-consumable" v-model="form.consumableId" class="form-input mt-1.5 h-10" :disabled="submitting">
            <option value="">不关联</option>
            <option v-for="consumable in store.consumables" :key="consumable.id" :value="String(consumable.id)">
              {{ consumable.name }}（库存 {{ consumable.currentStock }}）
            </option>
          </select>
        </div>
        <div v-if="form.category === 'Vehicle'">
          <label class="text-[13px] font-medium" for="item-mileage">里程周期（公里）</label>
          <input id="item-mileage" v-model="form.mileageCycleKm" type="number" min="1" step="1" class="form-input mt-1.5 h-10" placeholder="只记录，不按里程提醒" :disabled="submitting" />
          <p v-if="errors.mileageCycleKm" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.mileageCycleKm }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-link">购买链接</label>
          <input id="item-link" v-model="form.purchaseLink" class="form-input mt-1.5 h-10" maxlength="500" :disabled="submitting" />
          <p v-if="errors.purchaseLink" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.purchaseLink }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-note">备注</label>
          <textarea id="item-note" v-model="form.note" rows="3" class="form-input mt-1.5 resize-none py-2" maxlength="2000" :disabled="submitting" />
          <p v-if="errors.note" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.note }}</p>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="item-aliases">别名</label>
          <input id="item-aliases" v-model="form.aliases" class="form-input mt-1.5 h-10" placeholder="用逗号分隔，方便以后匹配" :disabled="submitting" />
          <p v-if="errors.aliases" class="mt-1 text-[12px] text-[#9d3b34]">{{ errors.aliases }}</p>
        </div>
      </div>
      <button v-if="localMode === 'template'" type="button" class="text-[12px] text-[var(--mn-muted)] hover:text-[var(--mn-ink)]" @click="detachTemplate">
        不使用模板，自己填写
      </button>
    </form>

    <template v-if="localMode !== 'template' || selectedTemplate" #footer>
      <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="submitting" @click="emit('close')">取消</button>
      <button id="household-item-submit" type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white disabled:opacity-50" :disabled="submitting" @click="submit">
        {{ submitting ? '保存中…' : localMode === 'edit' ? '保存' : '创建' }}
      </button>
    </template>
  </AppDialog>
</template>
