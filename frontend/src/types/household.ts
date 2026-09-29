/** 与后端 HouseholdDtos 对齐。枚举按名称传输。日期是 yyyy-MM-dd，不在客户端推算到期日。 */

export type HouseholdCategory = 'HomeMaintenance' | 'Vehicle' | 'Document' | 'Warranty'
export type HouseholdItemType = 'Recurring' | 'OneOffExpiry'
export type HouseholdCycleUnit = 'Day' | 'Month' | 'Year'
export type HouseholdRole = 'Admin' | 'Member'

export interface Household {
  id: number
  name: string
  myMemberId: number
  myRole: HouseholdRole
}

export interface HouseholdMember {
  id: number
  userId: number
  username: string
  /** 只对管理员返回；成员侧为 null，即使后端尚未收口也不要展示。 */
  email: string | null
  role: HouseholdRole
}

export interface HouseholdItem {
  id: number
  householdId: number
  name: string
  category: HouseholdCategory
  location: string | null
  modelSpec: string | null
  itemType: HouseholdItemType
  cycleValue: number | null
  cycleUnit: HouseholdCycleUnit | null
  lastDoneDate: string | null
  nextDueDate: string | null
  expiryDate: string | null
  leadDays: number
  assigneeMemberId: number | null
  assigneeName: string | null
  consumableId: number | null
  note: string | null
  purchaseLink: string | null
  isPaused: boolean
  mileageCycleKm: number | null
  aliases: string[]
  createdAt: string
  updatedAt: string
}

export interface HouseholdItemQuery {
  category?: HouseholdCategory
  includePaused?: boolean
}

export interface CreateHouseholdItemPayload {
  name: string
  category?: HouseholdCategory
  location?: string | null
  modelSpec?: string | null
  itemType: HouseholdItemType
  cycleValue?: number | null
  cycleUnit?: HouseholdCycleUnit | null
  lastDoneDate?: string | null
  expiryDate?: string | null
  leadDays?: number | null
  assigneeMemberId?: number | null
  consumableId?: number | null
  note?: string | null
  purchaseLink?: string | null
  isPaused?: boolean
  mileageCycleKm?: number | null
  aliases?: string[] | null
}

export interface UpdateHouseholdItemPayload {
  name: string
  category?: HouseholdCategory
  location?: string | null
  modelSpec?: string | null
  itemType: HouseholdItemType
  cycleValue?: number | null
  cycleUnit?: HouseholdCycleUnit | null
  lastDoneDate?: string | null
  expiryDate?: string | null
  leadDays?: number | null
  assigneeMemberId?: number | null
  consumableId?: number | null
  note?: string | null
  purchaseLink?: string | null
  mileageCycleKm?: number | null
  aliases?: string[] | null
}

export interface CreateFromTemplatePayload {
  templateId: number
  name?: string
  category?: HouseholdCategory
  cycleValue?: number | null
  cycleUnit?: HouseholdCycleUnit | null
  lastDoneDate?: string | null
  expiryDate?: string | null
  location?: string | null
  modelSpec?: string | null
  leadDays?: number | null
  assigneeMemberId?: number | null
  consumableId?: number | null
  note?: string | null
  purchaseLink?: string | null
  mileageCycleKm?: number | null
  aliases?: string[] | null
}

export interface HouseholdItemTemplate {
  id: number
  name: string
  category: HouseholdCategory
  itemType: HouseholdItemType
  cycleValue: number | null
  cycleUnit: HouseholdCycleUnit | null
  sortOrder: number
}

export interface CompleteHouseholdItemPayload {
  completedOn?: string | null
  completedByMemberId?: number | null
  photoRefs?: string[] | null
  cost?: number | null
  purchaseLink?: string | null
  note?: string | null
  skipConsumableDeduction?: boolean
  consumableQuantity?: number | null
  newExpiryDate?: string | null
}

export interface HouseholdCompletion {
  id: number
  itemId: number
  completedOn: string
  completedByMemberId: number
  completedByUserId: number
  completedByUsername: string
  photoRefs: string[]
  cost: number | null
  purchaseLink: string | null
  note: string | null
  consumableId: number | null
  consumableQuantityDeducted: number
  needsRestock: boolean
  newExpiryDate: string | null
  createdAt: string
}

export interface CompleteHouseholdItemResult {
  item: HouseholdItem
  record: HouseholdCompletion
  consumableQuantityDeducted: number
  consumableStockAfter: number | null
  needsRestock: boolean
}

export interface HouseholdUpcomingItem {
  id: number
  name: string
  category: HouseholdCategory
  itemType: HouseholdItemType
  location: string | null
  dueDate: string
  daysOverdue: number
  daysRemaining: number
  assigneeMemberId: number | null
  assigneeName: string | null
}

export interface HouseholdUpcoming {
  today: string
  overdue: HouseholdUpcomingItem[]
  within7Days: HouseholdUpcomingItem[]
  within30Days: HouseholdUpcomingItem[]
}

export interface HouseholdConsumable {
  id: number
  householdId: number
  name: string
  specModel: string | null
  currentStock: number
  restockThreshold: number
  isLowStock: boolean
  lowStockReminderSent: boolean
  unit: string | null
  purchaseLink: string | null
  note: string | null
  createdAt: string
  updatedAt: string
}

export interface SaveHouseholdConsumablePayload {
  name: string
  specModel?: string | null
  currentStock: number
  restockThreshold?: number | null
  unit?: string | null
  purchaseLink?: string | null
  note?: string | null
}
