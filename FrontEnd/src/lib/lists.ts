import { ApiError, authFetch } from './api'

// Account-scoped army lists. Every mutating call returns the full list so the
// client can replace its state from the response.

export interface SelectedOption {
  id: string
  name: string
  points: number
}

export interface ListEntry {
  id: string
  datasheetId: string
  datasheetName: string
  points: number
  optionsPoints: number
  quantity: number
  lineTotal: number
  keywords: string[]
  isWarlord: boolean
  enhancementId: string | null
  enhancementName: string | null
  enhancementPoints: number
  selectedOptions: SelectedOption[]
}

export interface ArmyListSummary {
  id: string
  name: string
  gameSystemKey: string
  factionId: string
  factionName: string
  pointsLimit: number | null
  total: number
  entryCount: number
  featured: boolean
  battleSize: string | null
  updatedAt: string
}

export interface ListDetachment {
  id: string
  name: string
  dp: number
}

export interface ArmyList {
  id: string
  name: string
  gameSystemKey: string
  factionId: string
  factionName: string
  pointsLimit: number | null
  total: number
  isOverLimit: boolean
  featured: boolean
  battleSize: string | null
  detachments: ListDetachment[]
  updatedAt: string
  entries: ListEntry[]
}

export interface CreateListInput {
  name: string
  gameSystemKey: string
  factionId: string
  pointsLimit: number | null
}

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await authFetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    throw await toError(res)
  }
  if (res.status === 204) {
    return undefined as T
  }
  return res.json() as Promise<T>
}

export function getLists(): Promise<ArmyListSummary[]> {
  return send<ArmyListSummary[]>('/api/lists', 'GET')
}

export function getList(id: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}`, 'GET')
}

export function createList(input: CreateListInput): Promise<ArmyList> {
  return send<ArmyList>('/api/lists', 'POST', input)
}

export function updateList(id: string, name: string, pointsLimit: number | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}`, 'PUT', { name, pointsLimit })
}

export function deleteList(id: string): Promise<void> {
  return send<void>(`/api/lists/${id}`, 'DELETE')
}

export function setListFeatured(id: string, featured: boolean): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/featured`, 'POST', { featured })
}

export const BATTLE_SIZES = ['Incursion', 'Strike Force', 'Onslaught'] as const

export function setBattleSize(id: string, battleSize: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/battle-size`, 'POST', { battleSize })
}

export function setDetachments(id: string, detachmentIds: string[]): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/detachments`, 'POST', { detachmentIds })
}

export function setWarlord(listId: string, entryId: string, isWarlord: boolean): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/warlord`, 'POST', { isWarlord })
}

export function setEnhancement(listId: string, entryId: string, enhancementId: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/enhancement`, 'POST', { enhancementId })
}

export function addEntry(
  listId: string,
  datasheetId: string,
  quantity = 1,
  selectedOptionIds: string[] = [],
): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries`, 'POST', {
    datasheetId,
    quantity,
    selectedOptionIds,
  })
}

export function updateEntry(
  listId: string,
  entryId: string,
  quantity: number,
  selectedOptionIds?: string[],
): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}`, 'PUT', {
    quantity,
    selectedOptionIds,
  })
}

export function removeEntry(listId: string, entryId: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}`, 'DELETE')
}

async function toError(res: Response): Promise<ApiError> {
  let message = `Request failed (${res.status})`
  try {
    const data = await res.json()
    if (data?.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors as Record<string, string[]>).flat()
      if (messages.length > 0) message = messages.join(' ')
    } else if (typeof data?.error === 'string') {
      message = data.error
    } else if (typeof data?.title === 'string') {
      message = data.title
    }
  } catch {
    // No or invalid JSON body; keep the generic message.
  }
  return new ApiError(res.status, message)
}
