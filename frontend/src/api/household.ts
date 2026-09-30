import { http, unwrap } from './auth'
import type {
  CompleteHouseholdItemPayload,
  ConfirmHouseholdChatPayload,
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
  HouseholdInvitation,
  HouseholdMember,
  HouseholdRole,
  HouseholdServerToday,
  HouseholdNotificationSettings,
  HouseholdUpcoming,
  SaveHouseholdConsumablePayload,
  UpdateHouseholdConsumablePayload,
  UpdateHouseholdItemPayload,
  UpdateHouseholdNotificationSettingsPayload,
} from '@/types/household'

function itemParams(query: HouseholdItemQuery = {}) {
  const params: Record<string, string | boolean> = {}
  if (query.category) params.category = query.category
  if (query.includePaused != null) params.includePaused = query.includePaused
  if (query.archivedOnly) params.archivedOnly = true
  return params
}

export const householdApi = {
  getMine: () => unwrap<Household>(http.get('/household')),

  listMembers: () => unwrap<HouseholdMember[]>(http.get('/household/members')),

  removeMember: (memberId: number) => unwrap<null>(http.delete(`/household/members/${memberId}`)),

  changeMemberRole: (memberId: number, role: HouseholdRole) =>
    unwrap<HouseholdMember>(http.put(`/household/members/${memberId}/role`, { role })),

  leaveHousehold: () => unwrap<null>(http.post('/household/leave')),

  listOutgoingInvitations: () => unwrap<HouseholdInvitation[]>(http.get('/household/invitations')),

  listIncomingInvitations: () => unwrap<HouseholdInvitation[]>(http.get('/household/invitations/incoming')),

  createInvitation: (userIdentifier: string) =>
    unwrap<HouseholdInvitation>(http.post('/household/invitations', { userIdentifier })),

  revokeInvitation: (id: number) => unwrap<HouseholdInvitation>(http.delete(`/household/invitations/${id}`)),

  acceptInvitation: (id: number, idempotencyKey: string) =>
    unwrap<HouseholdMember>(http.post(`/household/invitations/${id}/accept`, null, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })),

  rejectInvitation: (id: number) => unwrap<HouseholdInvitation>(http.post(`/household/invitations/${id}/reject`)),

  listItems: (query: HouseholdItemQuery = {}) =>
    unwrap<HouseholdItem[]>(http.get('/household/items', { params: itemParams(query) })),

  getItem: (id: number) => unwrap<HouseholdItem>(http.get(`/household/items/${id}`)),

  createItem: (payload: CreateHouseholdItemPayload) =>
    unwrap<HouseholdItem>(http.post('/household/items', payload)),

  updateItem: (id: number, payload: UpdateHouseholdItemPayload) =>
    unwrap<HouseholdItem>(http.put(`/household/items/${id}`, payload)),

  pauseItem: (id: number) => unwrap<HouseholdItem>(http.post(`/household/items/${id}/pause`)),

  resumeItem: (id: number) => unwrap<HouseholdItem>(http.post(`/household/items/${id}/resume`)),

  deleteItem: (id: number) => unwrap<null>(http.delete(`/household/items/${id}`)),

  restoreItem: (id: number, expiryDate: string) =>
    unwrap<HouseholdItem>(http.post(`/household/items/${id}/restore`, { expiryDate })),

  serverToday: () => unwrap<HouseholdServerToday>(http.get('/household/server-today', {
    skipErrorToastStatuses: [404],
  })),

  completeItem: (id: number, payload: CompleteHouseholdItemPayload, idempotencyKey: string) =>
    unwrap<CompleteHouseholdItemResult>(http.post(`/household/items/${id}/complete`, payload, {
      headers: { 'Idempotency-Key': idempotencyKey },
      skipErrorToastStatuses: [409, 422],
    })),

  confirmChatDraft: (payload: ConfirmHouseholdChatPayload, idempotencyKey: string) =>
    unwrap<CompleteHouseholdItemResult>(http.post('/household/chat/confirm', payload, {
      headers: { 'Idempotency-Key': idempotencyKey },
      skipErrorToastStatuses: [400, 404, 409],
    })),

  listChatDrafts: (sessionId: number) =>
    unwrap<unknown[]>(http.get('/household/chat/drafts', { params: { sessionId } })),

  history: (id: number) => unwrap<HouseholdCompletion[]>(http.get(`/household/items/${id}/history`)),

  upcoming: (category?: HouseholdCategory) =>
    unwrap<HouseholdUpcoming>(http.get('/household/upcoming', {
      params: category ? { category } : {},
    })),

  templates: () => unwrap<HouseholdItemTemplate[]>(http.get('/household/templates')),

  createFromTemplate: (payload: CreateFromTemplatePayload) =>
    unwrap<HouseholdItem>(http.post('/household/items/from-template', payload)),

  listConsumables: () => unwrap<HouseholdConsumable[]>(http.get('/household/consumables')),

  createConsumable: (payload: SaveHouseholdConsumablePayload) =>
    unwrap<HouseholdConsumable>(http.post('/household/consumables', payload)),

  updateConsumable: (id: number, payload: UpdateHouseholdConsumablePayload) =>
    unwrap<HouseholdConsumable>(http.put(`/household/consumables/${id}`, payload)),

  deleteConsumable: (id: number) => unwrap<null>(http.delete(`/household/consumables/${id}`)),

  restockConsumable: (id: number, quantity: number) =>
    unwrap<HouseholdConsumable>(http.post(`/household/consumables/${id}/restock`, { quantity })),

  getNotificationSettings: () =>
    unwrap<HouseholdNotificationSettings>(http.get('/household/notification-settings')),

  updateNotificationSettings: (payload: UpdateHouseholdNotificationSettingsPayload) =>
    unwrap<HouseholdNotificationSettings>(http.put('/household/notification-settings', payload)),

  testBark: (barkAddress?: string | null) =>
    unwrap<null>(http.post('/household/notification-settings/test-bark', { barkAddress: barkAddress || null })),

  testEmail: (email?: string | null) =>
    unwrap<null>(http.post('/household/notification-settings/test-email', { email: email || null })),
}
