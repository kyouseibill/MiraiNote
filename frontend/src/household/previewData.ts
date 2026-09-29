import { shiftCalendarDay, shanghaiToday } from '@/utils/householdFormat'
import type {
  Household,
  HouseholdCompletion,
  HouseholdConsumable,
  HouseholdItem,
  HouseholdItemTemplate,
  HouseholdMember,
  HouseholdUpcoming,
  HouseholdUpcomingItem,
} from '@/types/household'

export interface HouseholdPreviewBundle {
  household: Household
  members: HouseholdMember[]
  items: HouseholdItem[]
  templates: HouseholdItemTemplate[]
  consumables: HouseholdConsumable[]
  upcoming: HouseholdUpcoming
  history: Record<number, HouseholdCompletion[]>
}

function upcomingItem(
  item: HouseholdItem,
  today: string,
  daysOverdue: number,
  daysRemaining: number,
): HouseholdUpcomingItem {
  return {
    id: item.id,
    name: item.name,
    category: item.category,
    itemType: item.itemType,
    location: item.location,
    dueDate: item.nextDueDate ?? today,
    daysOverdue,
    daysRemaining,
    assigneeMemberId: item.assigneeMemberId,
    assigneeName: item.assigneeName,
  }
}

/** 设计预览用的静态数据。天数是随日期写好的，不是按周期推算到期日。 */
export function buildHouseholdPreview(asMember: boolean): HouseholdPreviewBundle {
  const today = shanghaiToday()
  const stamp = `${today}T01:00:00Z`
  const household: Household = {
    id: 1,
    name: '我的家庭',
    myMemberId: asMember ? 2 : 1,
    myRole: asMember ? 'Member' : 'Admin',
  }
  const members: HouseholdMember[] = [
    { id: 1, userId: 1, username: 'Bill', email: asMember ? null : 'bill@example.com', role: 'Admin' },
    { id: 2, userId: 2, username: '林夏', email: null, role: 'Member' },
  ]
  const consumables: HouseholdConsumable[] = [
    {
      id: 1, householdId: 1, name: '空调滤网', specModel: '标准', currentStock: 0, restockThreshold: 1,
      isLowStock: true, lowStockReminderSent: false, unit: '片', purchaseLink: null, note: null,
      createdAt: stamp, updatedAt: stamp,
    },
    {
      id: 2, householdId: 1, name: 'PP 棉', specModel: '10 寸', currentStock: 3, restockThreshold: 1,
      isLowStock: false, lowStockReminderSent: false, unit: '支', purchaseLink: null, note: null,
      createdAt: stamp, updatedAt: stamp,
    },
  ]
  const items: HouseholdItem[] = [
    {
      id: 1, householdId: 1, name: '空调滤网', category: 'HomeMaintenance', location: '客厅',
      modelSpec: '标准', itemType: 'Recurring', cycleValue: 3, cycleUnit: 'Month',
      lastDoneDate: '2026-06-01', nextDueDate: shiftCalendarDay(today, -2), expiryDate: null,
      leadDays: 7, assigneeMemberId: 2, assigneeName: '林夏', consumableId: 1, note: '南北两台一起换',
      purchaseLink: null, isPaused: false, mileageCycleKm: null, aliases: ['滤网'],
      createdAt: stamp, updatedAt: stamp,
    },
    {
      id: 2, householdId: 1, name: '净水器 PP 棉', category: 'HomeMaintenance', location: '厨房',
      modelSpec: null, itemType: 'Recurring', cycleValue: 6, cycleUnit: 'Month',
      lastDoneDate: '2026-04-01', nextDueDate: shiftCalendarDay(today, 3), expiryDate: null,
      leadDays: 7, assigneeMemberId: null, assigneeName: null, consumableId: 2, note: null,
      purchaseLink: null, isPaused: false, mileageCycleKm: null, aliases: [],
      createdAt: stamp, updatedAt: stamp,
    },
    {
      id: 3, householdId: 1, name: '年检', category: 'Vehicle', location: null,
      modelSpec: null, itemType: 'Recurring', cycleValue: 12, cycleUnit: 'Month',
      lastDoneDate: '2025-10-01', nextDueDate: shiftCalendarDay(today, 18), expiryDate: null,
      leadDays: 7, assigneeMemberId: 1, assigneeName: 'Bill', consumableId: null, note: null,
      purchaseLink: null, isPaused: false, mileageCycleKm: 5000, aliases: [],
      createdAt: stamp, updatedAt: stamp,
    },
    {
      id: 4, householdId: 1, name: '护照', category: 'Document', location: null,
      modelSpec: null, itemType: 'OneOffExpiry', cycleValue: null, cycleUnit: null,
      lastDoneDate: null, nextDueDate: shiftCalendarDay(today, 12), expiryDate: shiftCalendarDay(today, 12),
      leadDays: 30, assigneeMemberId: 1, assigneeName: 'Bill', consumableId: null, note: null,
      purchaseLink: null, isPaused: false, mileageCycleKm: null, aliases: [],
      createdAt: stamp, updatedAt: stamp,
    },
    {
      id: 5, householdId: 1, name: '洗衣机槽清洁', category: 'HomeMaintenance', location: '卫生间',
      modelSpec: null, itemType: 'Recurring', cycleValue: 1, cycleUnit: 'Month',
      lastDoneDate: '2026-08-01', nextDueDate: shiftCalendarDay(today, -10), expiryDate: null,
      leadDays: 7, assigneeMemberId: null, assigneeName: null, consumableId: null, note: '暂停到搬家后',
      purchaseLink: null, isPaused: true, mileageCycleKm: null, aliases: [],
      createdAt: stamp, updatedAt: stamp,
    },
  ]
  const active = items.filter((item) => !item.isPaused && item.nextDueDate)
  const upcoming: HouseholdUpcoming = {
    today,
    overdue: active.filter((item) => (item.nextDueDate ?? '') < today).map((item) => upcomingItem(item, today, 2, 0)),
    within7Days: active.filter((item) => {
      const due = item.nextDueDate ?? ''
      return due >= today && due <= shiftCalendarDay(today, 7)
    }).map((item) => upcomingItem(item, today, 0, 3)),
    within30Days: active.filter((item) => {
      const due = item.nextDueDate ?? ''
      return due > shiftCalendarDay(today, 7) && due <= shiftCalendarDay(today, 30)
    }).map((item) => upcomingItem(item, today, 0, item.id === 3 ? 18 : 12)),
  }
  const templates: HouseholdItemTemplate[] = [
    { id: 1, name: '空调滤网', category: 'HomeMaintenance', itemType: 'Recurring', cycleValue: 3, cycleUnit: 'Month', sortOrder: 1 },
    { id: 2, name: '净水器 PP 棉', category: 'HomeMaintenance', itemType: 'Recurring', cycleValue: 6, cycleUnit: 'Month', sortOrder: 2 },
    { id: 9, name: '洗衣机槽清洁', category: 'HomeMaintenance', itemType: 'Recurring', cycleValue: 1, cycleUnit: 'Month', sortOrder: 9 },
    { id: 10, name: '常规保养', category: 'Vehicle', itemType: 'Recurring', cycleValue: 6, cycleUnit: 'Month', sortOrder: 10 },
    { id: 14, name: '护照', category: 'Document', itemType: 'OneOffExpiry', cycleValue: null, cycleUnit: null, sortOrder: 14 },
    { id: 17, name: '家电保修', category: 'Warranty', itemType: 'OneOffExpiry', cycleValue: null, cycleUnit: null, sortOrder: 17 },
  ]
  const history: Record<number, HouseholdCompletion[]> = {
    1: [
      {
        id: 11, itemId: 1, completedOn: '2026-06-01', completedByMemberId: 2, completedByUserId: 2,
        completedByUsername: '林夏', photoRefs: [], cost: 68, purchaseLink: null, note: '换了客厅滤网',
        consumableId: 1, consumableQuantityDeducted: 1, needsRestock: false, newExpiryDate: null, createdAt: stamp,
      },
    ],
  }
  return { household, members, items, templates, consumables, upcoming, history }
}
