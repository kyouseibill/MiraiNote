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
  /** false 表示还没有家庭。缺省按已有家庭处理，设计预览不带这个字段。 */
  hasHousehold?: boolean
  /** 有待处理邀请，且还没有家庭时先接受或拒绝。 */
  hasPendingInvitations?: boolean
}

export interface HouseholdMember {
  id: number
  userId: number
  username: string
  /** 只对管理员返回；成员侧为 null，即使后端尚未收口也不要展示。 */
  email: string | null
  role: HouseholdRole
}

export type HouseholdInvitationStatus = 'Pending' | 'Accepted' | 'Rejected' | 'Revoked'

export interface HouseholdInvitation {
  id: number
  householdId: number
  householdName: string
  inviteeUserId: number
  inviteeUsername: string
  inviteeEmail: string | null
  inviterUsername: string
  role: HouseholdRole
  status: HouseholdInvitationStatus
  expiresAt: string
  isExpired: boolean
}

export interface HouseholdConsumableLink {
  id: number
  name: string
  isArchived: boolean
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
  isArchived: boolean
  mileageCycleKm: number | null
  aliases: string[]
  createdAt: string
  updatedAt: string
}

export interface HouseholdItemQuery {
  category?: HouseholdCategory
  includePaused?: boolean
  /** 为 true 时只看已归档事项。 */
  archivedOnly?: boolean
}

export interface HouseholdServerToday {
  today: string
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

export interface ConfirmHouseholdChatPayload {
  draftId: number
  itemId: number
  completedOn?: string | null
  cost?: number | null
  /** 缺省表示扣减。false 表示这次不扣。 */
  deductConsumable?: boolean | null
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
  linkedItems?: HouseholdConsumableLink[]
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

export type HouseholdNotificationChannel = 'Email' | 'Bark'

export interface HouseholdNotificationDeliveryFailure {
  failedAt: string
  reason: string
}

export interface HouseholdNotificationSettings {
  barkEnabled: boolean
  barkConfigured: boolean
  barkAddressSuffix: string | null
  barkAddressUnreadable: boolean
  emailEnabled: boolean
  email: string | null
  pushHour: number
  pushMinute: number
  leadChannel: HouseholdNotificationChannel
  dueChannel: HouseholdNotificationChannel
  overdueIntervalDays: number
  notificationsEnabled: boolean
  barkFailure: HouseholdNotificationDeliveryFailure | null
  emailFailure: HouseholdNotificationDeliveryFailure | null
}

export interface UpdateHouseholdNotificationSettingsPayload {
  barkEnabled: boolean
  barkAddress?: string | null
  clearBarkAddress?: boolean
  emailEnabled: boolean
  email?: string | null
  pushHour: number
  pushMinute: number
  leadChannel: HouseholdNotificationChannel
  dueChannel: HouseholdNotificationChannel
  overdueIntervalDays: number
}

export interface UpdateHouseholdConsumablePayload {
  name: string
  specModel?: string | null
  restockThreshold?: number | null
  unit?: string | null
  purchaseLink?: string | null
  note?: string | null
}
