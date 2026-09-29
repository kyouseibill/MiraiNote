import { http, unwrap } from './auth'
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
  HouseholdUpcoming,
  SaveHouseholdConsumablePayload,
  UpdateHouseholdItemPayload,
} from '@/types/household'

function itemParams(query: HouseholdItemQuery = {}) {
  const params: Record<string, string | boolean> = {}
  if (query.category) params.category = query.category
  if (query.includePaused != null) params.includePaused = query.includePaused
  return params
}

export const householdApi = {
  getMine: () => unwrap<Household>(http.get('/household')),

  listMembers: () => unwrap<HouseholdMember[]>(http.get('/household/members')),

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

  completeItem: (id: number, payload: CompleteHouseholdItemPayload, idempotencyKey: string) =>
    unwrap<CompleteHouseholdItemResult>(http.post(`/household/items/${id}/complete`, payload, {
      headers: { 'Idempotency-Key': idempotencyKey },
      skipErrorToastStatuses: [409, 422],
    })),

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

  deleteConsumable: (id: number) => unwrap<null>(http.delete(`/household/consumables/${id}`)),

  restockConsumable: (id: number, quantity: number) =>
    unwrap<HouseholdConsumable>(http.post(`/household/consumables/${id}/restock`, { quantity })),
}
