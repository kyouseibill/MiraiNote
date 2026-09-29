import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import axios from 'axios'
import { lifeLogApi } from '@/api/lifeLog'
import { householdApi } from '@/api/household'
import { buildHouseholdPreview } from '@/household/previewData'
import { shanghaiToday } from '@/utils/householdFormat'
import type {
  CompleteHouseholdItemPayload,
  CompleteHouseholdItemResult,
  CreateFromTemplatePayload,
  CreateHouseholdItemPayload,
  Household,
  HouseholdCategory,
  HouseholdCompletion,
  HouseholdConsumable,
  HouseholdItem,
  HouseholdItemQuery,
  HouseholdItemTemplate,
  HouseholdMember,
  HouseholdNotificationSettings,
  HouseholdUpcoming,
  SaveHouseholdConsumablePayload,
  UpdateHouseholdConsumablePayload,
  UpdateHouseholdItemPayload,
  UpdateHouseholdNotificationSettingsPayload,
} from '@/types/household'

const archivedReadOnlyMessage = '已归档事项请先由管理员恢复'

export class HouseholdRequestError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'HouseholdRequestError'
    this.status = status
  }
}

function cloneUpcoming(source: HouseholdUpcoming, category?: HouseholdCategory): HouseholdUpcoming {
  const match = (item: { category: HouseholdCategory }) => !category || item.category === category
  return {
    today: source.today,
    overdue: source.overdue.filter(match),
    within7Days: source.within7Days.filter(match),
    within30Days: source.within30Days.filter(match),
  }
}

export const useHouseholdStore = defineStore('household', () => {
  const household = ref<Household | null>(null)
  const members = ref<HouseholdMember[]>([])
  const items = ref<HouseholdItem[]>([])
  const templates = ref<HouseholdItemTemplate[]>([])
  const consumables = ref<HouseholdConsumable[]>([])
  const upcoming = ref<HouseholdUpcoming | null>(null)
  const currentItem = ref<HouseholdItem | null>(null)
  const history = ref<HouseholdCompletion[]>([])
  const loading = ref(false)
  const previewMode = ref(false)
  const previewAsMember = ref(false)

  const itemSource = ref<HouseholdItem[]>([])
  const upcomingSource = ref<HouseholdUpcoming | null>(null)
  const historySource = ref<Record<number, HouseholdCompletion[]>>({})
  const lastQuery = ref<HouseholdItemQuery>({ includePaused: true })
  const serverToday = ref<string | null>(null)
  const serverTodayUnavailable = ref(false)
  const notificationSettings = ref<HouseholdNotificationSettings | null>(null)

  const isAdmin = computed(() => household.value?.myRole === 'Admin')
  const calendarToday = computed(() => serverToday.value ?? shanghaiToday())

  function assertAdmin(message: string) {
    if (!isAdmin.value) throw new HouseholdRequestError(403, message)
  }

  function assertNotArchived(id: number) {
    const known = itemSource.value.find((item) => item.id === id)
      ?? (currentItem.value?.id === id ? currentItem.value : null)
    if (known?.isArchived) throw new HouseholdRequestError(400, archivedReadOnlyMessage)
  }

  function applyItemQuery() {
    const query = lastQuery.value
    items.value = itemSource.value.filter((item) => {
      if (query.archivedOnly) {
        if (!item.isArchived) return false
      } else if (item.isArchived) {
        return false
      }
      if (!query.includePaused && item.isPaused) return false
      if (query.category && item.category !== query.category) return false
      return true
    })
  }

  function replaceItem(next: HouseholdItem) {
    if (currentItem.value?.id === next.id) currentItem.value = next
    if (previewMode.value) {
      const index = itemSource.value.findIndex((item) => item.id === next.id)
      if (index >= 0) itemSource.value[index] = next
      applyItemQuery()
      return
    }
    const index = items.value.findIndex((item) => item.id === next.id)
    if (index >= 0) items.value[index] = next
  }

  function ensurePreview(asMember: boolean) {
    if (previewMode.value && previewAsMember.value === asMember && household.value) return
    const bundle = buildHouseholdPreview(asMember)
    previewMode.value = true
    previewAsMember.value = asMember
    serverToday.value = null
    household.value = bundle.household
    members.value = bundle.members
    itemSource.value = bundle.items
    templates.value = bundle.templates
    consumables.value = bundle.consumables
    upcomingSource.value = bundle.upcoming
    upcoming.value = cloneUpcoming(bundle.upcoming)
    historySource.value = bundle.history
    currentItem.value = null
    history.value = []
    applyItemQuery()
  }

  async function fetchServerToday() {
    if (previewMode.value) {
      serverToday.value = null
      return null
    }
    if (serverTodayUnavailable.value) return null
    try {
      const result = await householdApi.serverToday()
      serverToday.value = result.today.slice(0, 10)
    } catch (error) {
      serverToday.value = null
      if (axios.isAxiosError(error) && error.response?.status === 404) {
        serverTodayUnavailable.value = true
      }
    }
    return serverToday.value
  }

  async function fetchHousehold() {
    if (previewMode.value) return household.value
    household.value = await householdApi.getMine()
    return household.value
  }

  async function fetchMembers() {
    if (previewMode.value) return members.value
    members.value = await householdApi.listMembers()
    return members.value
  }

  async function fetchItems(query: HouseholdItemQuery = {}) {
    lastQuery.value = {
      includePaused: query.includePaused ?? true,
      category: query.category,
      archivedOnly: query.archivedOnly ?? false,
    }
    if (previewMode.value) {
      applyItemQuery()
      return items.value
    }
    items.value = await householdApi.listItems(lastQuery.value)
    return items.value
  }

  async function fetchTemplates() {
    if (previewMode.value) return templates.value
    if (templates.value.length > 0) return templates.value
    templates.value = await householdApi.templates()
    return templates.value
  }

  async function fetchConsumables() {
    if (previewMode.value) return consumables.value
    consumables.value = await householdApi.listConsumables()
    return consumables.value
  }

  async function fetchUpcoming(category?: HouseholdCategory, real = false) {
    if (!real && previewMode.value && upcomingSource.value) {
      upcoming.value = cloneUpcoming(upcomingSource.value, category)
      return upcoming.value
    }
    upcoming.value = await householdApi.upcoming(category)
    return upcoming.value
  }

  async function fetchItem(id: number) {
    if (previewMode.value) {
      const found = itemSource.value.find((item) => item.id === id) ?? null
      currentItem.value = found
      if (!found) throw new HouseholdRequestError(404, '事项不存在')
      return found
    }
    currentItem.value = await householdApi.getItem(id)
    return currentItem.value
  }

  async function fetchHistory(id: number) {
    if (previewMode.value) {
      history.value = historySource.value[id] ?? []
      return history.value
    }
    history.value = await householdApi.history(id)
    return history.value
  }

  async function loadWorkspace(preview: boolean, asMember = false) {
    loading.value = true
    try {
      if (preview) {
        ensurePreview(asMember)
        return
      }
      previewMode.value = false
      await fetchServerToday()
      await fetchHousehold()
      await Promise.all([
        fetchMembers(),
        fetchItems(lastQuery.value),
        fetchTemplates(),
        fetchConsumables(),
      ])
    } finally {
      loading.value = false
    }
  }

  async function createItem(payload: CreateHouseholdItemPayload) {
    if (previewMode.value) {
      const created: HouseholdItem = {
        id: Date.now(),
        householdId: household.value?.id ?? 0,
        name: payload.name,
        category: payload.category ?? 'HomeMaintenance',
        location: payload.location ?? null,
        modelSpec: payload.modelSpec ?? null,
        itemType: payload.itemType,
        cycleValue: payload.cycleValue ?? null,
        cycleUnit: payload.cycleUnit ?? null,
        lastDoneDate: payload.lastDoneDate ?? null,
        nextDueDate: null,
        expiryDate: payload.expiryDate ?? null,
        leadDays: payload.leadDays ?? 7,
        assigneeMemberId: payload.assigneeMemberId ?? null,
        assigneeName: members.value.find((member) => member.id === payload.assigneeMemberId)?.username ?? null,
        consumableId: payload.consumableId ?? null,
        note: payload.note ?? null,
        purchaseLink: payload.purchaseLink ?? null,
        isPaused: false,
        isArchived: false,
        mileageCycleKm: payload.mileageCycleKm ?? null,
        aliases: payload.aliases ?? [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      itemSource.value = [created, ...itemSource.value]
      applyItemQuery()
      return created
    }
    const created = await householdApi.createItem(payload)
    await fetchItems(lastQuery.value)
    return created
  }

  async function createFromTemplate(payload: CreateFromTemplatePayload) {
    if (previewMode.value) {
      const template = templates.value.find((item) => item.id === payload.templateId)
      return createItem({
        name: payload.name || template?.name || '未命名',
        category: payload.category ?? template?.category,
        itemType: template?.itemType ?? 'Recurring',
        cycleValue: payload.cycleValue ?? template?.cycleValue,
        cycleUnit: payload.cycleUnit ?? template?.cycleUnit,
        lastDoneDate: payload.lastDoneDate,
        expiryDate: payload.expiryDate,
        location: payload.location,
        modelSpec: payload.modelSpec,
        leadDays: payload.leadDays,
        assigneeMemberId: payload.assigneeMemberId,
        consumableId: payload.consumableId,
        note: payload.note,
        purchaseLink: payload.purchaseLink,
        mileageCycleKm: payload.mileageCycleKm,
        aliases: payload.aliases,
      })
    }
    const created = await householdApi.createFromTemplate(payload)
    await fetchItems(lastQuery.value)
    return created
  }

  async function updateItem(id: number, payload: UpdateHouseholdItemPayload) {
    assertNotArchived(id)
    assertAdmin('只有管理员可以编辑事项')
    if (previewMode.value) {
      const current = itemSource.value.find((item) => item.id === id)
      if (!current) throw new HouseholdRequestError(404, '事项不存在')
      const next: HouseholdItem = {
        ...current,
        ...payload,
        category: payload.category ?? current.category,
        location: payload.location ?? null,
        modelSpec: payload.modelSpec ?? null,
        cycleValue: payload.cycleValue ?? null,
        cycleUnit: payload.cycleUnit ?? null,
        lastDoneDate: payload.lastDoneDate ?? null,
        expiryDate: payload.expiryDate ?? null,
        leadDays: payload.leadDays ?? current.leadDays,
        assigneeMemberId: payload.assigneeMemberId ?? null,
        assigneeName: members.value.find((member) => member.id === payload.assigneeMemberId)?.username ?? null,
        consumableId: payload.consumableId ?? null,
        note: payload.note ?? null,
        purchaseLink: payload.purchaseLink ?? null,
        mileageCycleKm: payload.mileageCycleKm ?? null,
        aliases: payload.aliases ?? [],
        nextDueDate: current.nextDueDate,
        updatedAt: new Date().toISOString(),
      }
      replaceItem(next)
      return next
    }
    const updated = await householdApi.updateItem(id, payload)
    replaceItem(updated)
    return updated
  }

  async function setPaused(id: number, paused: boolean) {
    assertNotArchived(id)
    assertAdmin(paused ? '只有管理员可以暂停事项' : '只有管理员可以恢复事项')
    if (previewMode.value) {
      const current = itemSource.value.find((item) => item.id === id)
      if (!current) throw new HouseholdRequestError(404, '事项不存在')
      const next = { ...current, isPaused: paused }
      replaceItem(next)
      return next
    }
    const updated = paused ? await householdApi.pauseItem(id) : await householdApi.resumeItem(id)
    replaceItem(updated)
    return updated
  }

  async function removeItem(id: number) {
    assertAdmin('只有管理员可以删除事项')
    if (previewMode.value) {
      itemSource.value = itemSource.value.filter((item) => item.id !== id)
      if (currentItem.value?.id === id) currentItem.value = null
      applyItemQuery()
      return
    }
    await householdApi.deleteItem(id)
    items.value = items.value.filter((item) => item.id !== id)
    if (currentItem.value?.id === id) currentItem.value = null
  }

  async function completeItem(id: number, payload: CompleteHouseholdItemPayload, idempotencyKey?: string) {
    assertNotArchived(id)
    if (previewMode.value) {
      const current = itemSource.value.find((item) => item.id === id)
      if (!current) throw new HouseholdRequestError(404, '事项不存在')
      const completedOn = payload.completedOn ?? calendarToday.value
      const backfill = Boolean(current.lastDoneDate && completedOn < current.lastDoneDate)
      const sameDay = Boolean(current.lastDoneDate && completedOn === current.lastDoneDate)
      const renewing = Boolean(payload.newExpiryDate) && !backfill
      let lastDoneDate = current.lastDoneDate
      let expiryDate = current.expiryDate
      let nextDueDate = current.nextDueDate
      let isArchived = current.isArchived
      if (!backfill && current.itemType === 'OneOffExpiry') {
        if (renewing) {
          expiryDate = payload.newExpiryDate ?? expiryDate
          nextDueDate = payload.newExpiryDate ?? nextDueDate
          isArchived = false
          lastDoneDate = completedOn
        } else {
          isArchived = true
          if (!sameDay) lastDoneDate = completedOn
        }
      } else if (!backfill && !sameDay) {
        lastDoneDate = completedOn
      }
      const next: HouseholdItem = {
        ...current,
        lastDoneDate,
        expiryDate,
        nextDueDate,
        isArchived,
      }
      replaceItem(next)
      const record: HouseholdCompletion = {
        id: Date.now(),
        itemId: id,
        completedOn,
        completedByMemberId: payload.completedByMemberId ?? household.value?.myMemberId ?? 0,
        completedByUserId: 0,
        completedByUsername: members.value.find((member) => member.id === (payload.completedByMemberId ?? household.value?.myMemberId))?.username ?? '我',
        photoRefs: payload.photoRefs ?? [],
        cost: payload.cost ?? null,
        purchaseLink: payload.purchaseLink ?? null,
        note: payload.note ?? null,
        consumableId: current.consumableId,
        consumableQuantityDeducted: 0,
        needsRestock: false,
        newExpiryDate: payload.newExpiryDate ?? null,
        createdAt: new Date().toISOString(),
      }
      historySource.value = { ...historySource.value, [id]: [record, ...(historySource.value[id] ?? [])] }
      if (currentItem.value?.id === id) history.value = historySource.value[id] ?? []
      return { item: next, record, consumableQuantityDeducted: 0, consumableStockAfter: null, needsRestock: false }
    }
    const result = await householdApi.completeItem(id, payload, idempotencyKey || crypto.randomUUID())
    replaceItem(result.item)
    if (currentItem.value?.id === id) {
      history.value = [result.record, ...history.value.filter((record) => record.id !== result.record.id)]
    }
    patchConsumable(result)
    return result
  }

  function patchConsumable(result: CompleteHouseholdItemResult) {
    if (result.consumableStockAfter == null || result.record.consumableId == null) return
    const found = consumables.value.find((item) => item.id === result.record.consumableId)
    if (!found) return
    found.currentStock = result.consumableStockAfter
    found.isLowStock = found.currentStock <= found.restockThreshold
  }

  async function createConsumable(payload: SaveHouseholdConsumablePayload) {
    if (previewMode.value) {
      const created: HouseholdConsumable = {
        id: Date.now(),
        householdId: household.value?.id ?? 0,
        name: payload.name,
        specModel: payload.specModel ?? null,
        currentStock: payload.currentStock,
        restockThreshold: payload.restockThreshold ?? 1,
        isLowStock: payload.currentStock <= (payload.restockThreshold ?? 1),
        lowStockReminderSent: false,
        unit: payload.unit ?? null,
        purchaseLink: payload.purchaseLink ?? null,
        note: payload.note ?? null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      consumables.value = [...consumables.value, created]
      return created
    }
    const created = await householdApi.createConsumable(payload)
    consumables.value = [...consumables.value, created].sort((a, b) => a.name.localeCompare(b.name, 'zh'))
    return created
  }

  async function updateConsumable(id: number, payload: UpdateHouseholdConsumablePayload) {
    if (previewMode.value) {
      const found = consumables.value.find((item) => item.id === id)
      if (!found) throw new HouseholdRequestError(404, '耗材不存在')
      found.name = payload.name
      found.specModel = payload.specModel ?? null
      found.restockThreshold = payload.restockThreshold ?? found.restockThreshold
      found.unit = payload.unit ?? null
      found.purchaseLink = payload.purchaseLink ?? null
      found.note = payload.note ?? null
      found.isLowStock = found.currentStock <= found.restockThreshold
      found.updatedAt = new Date().toISOString()
      return found
    }
    const updated = await householdApi.updateConsumable(id, payload)
    const index = consumables.value.findIndex((item) => item.id === id)
    if (index >= 0) consumables.value[index] = updated
    else consumables.value = [...consumables.value, updated].sort((a, b) => a.name.localeCompare(b.name, 'zh'))
    return updated
  }

  async function restoreItem(id: number, expiryDate: string) {
    assertAdmin('只有管理员可以恢复已归档事项')
    if (previewMode.value) {
      const current = itemSource.value.find((item) => item.id === id)
      if (!current || !current.isArchived) throw new HouseholdRequestError(400, '只有已归档的一次性事项可以恢复')
      const next = { ...current, isArchived: false, expiryDate, nextDueDate: expiryDate }
      replaceItem(next)
      applyItemQuery()
      return next
    }
    const updated = await householdApi.restoreItem(id, expiryDate)
    replaceItem(updated)
    await fetchItems(lastQuery.value)
    if (currentItem.value?.id === id) currentItem.value = updated
    return updated
  }

  async function restockConsumable(id: number, quantity: number) {
    if (previewMode.value) {
      const found = consumables.value.find((item) => item.id === id)
      if (!found) throw new HouseholdRequestError(404, '耗材不存在')
      found.currentStock += quantity
      found.isLowStock = found.currentStock <= found.restockThreshold
      return found
    }
    const updated = await householdApi.restockConsumable(id, quantity)
    const index = consumables.value.findIndex((item) => item.id === id)
    if (index >= 0) consumables.value[index] = updated
    return updated
  }

  async function removeConsumable(id: number) {
    assertAdmin('只有管理员可以删除耗材')
    if (previewMode.value) {
      const used = itemSource.value.some((item) => item.consumableId === id)
      if (used) throw new HouseholdRequestError(400, '仍有事项关联该耗材，无法删除')
      consumables.value = consumables.value.filter((item) => item.id !== id)
      return
    }
    await householdApi.deleteConsumable(id)
    consumables.value = consumables.value.filter((item) => item.id !== id)
  }

  async function uploadPhoto(file: File) {
    if (previewMode.value) return URL.createObjectURL(file)
    return lifeLogApi.uploadImage(file)
  }

  function defaultNotificationSettings(): HouseholdNotificationSettings {
    return {
      barkEnabled: true,
      barkConfigured: true,
      barkAddressSuffix: '9f3a',
      barkAddressUnreadable: false,
      emailEnabled: true,
      email: 'preview@mirainote.local',
      pushHour: 9,
      pushMinute: 0,
      leadChannel: 'Email',
      dueChannel: 'Bark',
      overdueIntervalDays: 3,
      notificationsEnabled: false,
      barkFailure: { failedAt: '2026-10-08T01:05:00.000Z', reason: '发送失败' },
      emailFailure: { failedAt: '2026-10-08T01:07:00.000Z', reason: '连接失败' },
    }
  }

  async function fetchNotificationSettings() {
    if (previewMode.value) {
      notificationSettings.value ??= defaultNotificationSettings()
      return notificationSettings.value
    }
    notificationSettings.value = await householdApi.getNotificationSettings()
    return notificationSettings.value
  }

  async function saveNotificationSettings(payload: UpdateHouseholdNotificationSettingsPayload) {
    if (previewMode.value) {
      const current = notificationSettings.value ?? defaultNotificationSettings()
      const next: HouseholdNotificationSettings = {
        ...current,
        barkEnabled: payload.barkEnabled,
        emailEnabled: payload.emailEnabled,
        email: payload.email?.trim() || null,
        pushHour: payload.pushHour,
        pushMinute: payload.pushMinute,
        leadChannel: payload.leadChannel,
        dueChannel: payload.dueChannel,
        overdueIntervalDays: payload.overdueIntervalDays,
      }
      if (payload.clearBarkAddress) {
        next.barkConfigured = false
        next.barkAddressSuffix = null
        next.barkAddressUnreadable = false
      } else if (payload.barkAddress?.trim()) {
        next.barkConfigured = true
        next.barkAddressSuffix = payload.barkAddress.trim().slice(-4)
        next.barkAddressUnreadable = false
      }
      notificationSettings.value = next
      return next
    }
    notificationSettings.value = await householdApi.updateNotificationSettings(payload)
    return notificationSettings.value
  }

  async function testNotificationChannel(kind: 'bark' | 'email', value?: string | null) {
    if (previewMode.value) return
    if (kind === 'bark') await householdApi.testBark(value)
    else await householdApi.testEmail(value)
  }

  function consumableName(id: number | null | undefined) {
    if (id == null) return ''
    return consumables.value.find((item) => item.id === id)?.name ?? ''
  }

  return {
    household,
    members,
    items,
    templates,
    consumables,
    upcoming,
    currentItem,
    history,
    loading,
    previewMode,
    isAdmin,
    calendarToday,
    fetchServerToday,
    fetchHousehold,
    fetchMembers,
    fetchItems,
    fetchTemplates,
    fetchConsumables,
    fetchUpcoming,
    fetchItem,
    fetchHistory,
    ensurePreview,
    loadWorkspace,
    createItem,
    createFromTemplate,
    updateItem,
    setPaused,
    removeItem,
    completeItem,
    restoreItem,
    createConsumable,
    updateConsumable,
    restockConsumable,
    removeConsumable,
    uploadPhoto,
    consumableName,
    notificationSettings,
    fetchNotificationSettings,
    saveNotificationSettings,
    testNotificationChannel,
  }
})
