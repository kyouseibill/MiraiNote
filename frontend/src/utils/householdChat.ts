export interface HouseholdChatCandidate {
  id: number
  name: string
  location: string | null
  isPaused: boolean
  nextDueDate: string | null
  consumableId: number | null
  consumableName: string | null
  consumableStock: number | null
}

export interface HouseholdChatDraft {
  kind: string
  message: string
  draftId: number | null
  expiresAt: string | null
  completedOn: string | null
  cost: number | null
  deductConsumable: boolean
  item: HouseholdChatCandidate | null
  candidates: HouseholdChatCandidate[]
  suggestedName: string | null
  confirmed?: boolean
  /** 这份草稿属于哪次对话。只在对应会话里恢复卡片。 */
  sessionId?: number
}

const KINDS = new Set(['confirm', 'choose', 'create', 'query', 'unrecognized', 'rejected'])

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null
}

function parseCandidate(value: unknown): HouseholdChatCandidate | null {
  if (!value || typeof value !== 'object') return null
  const row = value as Record<string, unknown>
  if (typeof row.id !== 'number' || typeof row.name !== 'string') return null
  return {
    id: row.id,
    name: row.name,
    location: text(row.location),
    isPaused: row.isPaused === true,
    nextDueDate: text(row.nextDueDate),
    consumableId: typeof row.consumableId === 'number' ? row.consumableId : null,
    consumableName: text(row.consumableName),
    consumableStock: typeof row.consumableStock === 'number' ? row.consumableStock : null,
  }
}

/** 工具结果只解析成待确认草稿。写完成记录仍走确认接口。 */
export function parseHouseholdChatDraft(result: unknown): HouseholdChatDraft | null {
  let value = result
  if (typeof result === 'string') {
    const trimmed = result.trim()
    if (!trimmed) return null
    try {
      value = JSON.parse(trimmed)
    } catch {
      return null
    }
  }
  if (!value || typeof value !== 'object') return null
  const row = value as Record<string, unknown>
  const kind = typeof row.kind === 'string' ? row.kind : ''
  if (!KINDS.has(kind)) return null
  const candidates = Array.isArray(row.candidates)
    ? row.candidates.map(parseCandidate).filter((item): item is HouseholdChatCandidate => item != null)
    : []
  return {
    kind,
    message: typeof row.message === 'string' ? row.message : '',
    draftId: typeof row.draftId === 'number' ? row.draftId : null,
    expiresAt: text(row.expiresAt),
    completedOn: text(row.completedOn),
    cost: typeof row.cost === 'number' ? row.cost : null,
    deductConsumable: row.deductConsumable === true,
    item: parseCandidate(row.item),
    candidates,
    suggestedName: text(row.suggestedName),
    confirmed: row.confirmed === true,
  }
}

/** 只渲染属于当前会话、且没有被本页工具卡占用的草稿。 */
export function visibleRestoredDrafts(
  drafts: HouseholdChatDraft[],
  sessionId: number | null | undefined,
  liveDraftIds: ReadonlySet<number>,
): HouseholdChatDraft[] {
  if (sessionId == null || sessionId <= 0) return []
  return drafts.filter((draft) => {
    if (draft.sessionId !== sessionId) return false
    if (draft.kind !== 'confirm' && draft.kind !== 'choose' && draft.kind !== 'create') return false
    return draft.draftId == null || !liveDraftIds.has(draft.draftId)
  })
}

export function householdCreateHref(name: string | null | undefined): string {
  const trimmed = name?.trim()
  return trimmed ? `/household?prefill=${encodeURIComponent(trimmed)}` : '/household'
}
