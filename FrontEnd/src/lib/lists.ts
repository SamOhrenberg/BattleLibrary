import { ApiError, authFetch } from './api'

// Account-scoped, freeform army lists. Every field is typed by the owner and
// nothing is validated against game rules. Mutating calls return the full list.

export interface ListForce {
  id: string
  name: string
  sortOrder: number
}

export interface ListEntry {
  id: string
  forceId: string
  name: string
  points: number | null
  modelCount: number | null
  wargear: string | null
  enhancement: string | null
  notes: string | null
  sortOrder: number
  isWarlord: boolean
  // The entry this one is attached to (e.g. a leader joining a unit), or null.
  attachedToEntryId: string | null
}

export interface ArmyListSummary {
  id: string
  name: string
  gameSystemKey: string
  factionName: string
  pointsLimit: number | null
  total: number
  entryCount: number
  featured: boolean
  updatedAt: string
}

export interface ArmyList {
  id: string
  name: string
  gameSystemKey: string
  factionName: string
  detachment: string | null
  pointsLimit: number | null
  notes: string | null
  total: number
  featured: boolean
  forces: ListForce[]
  updatedAt: string
  entries: ListEntry[]
}

export interface ListFields {
  name: string
  gameSystemKey: string
  factionName?: string | null
  detachment?: string | null
  pointsLimit?: number | null
  notes?: string | null
}

export interface EntryFields {
  name: string
  points?: number | null
  modelCount?: number | null
  wargear?: string | null
  enhancement?: string | null
  notes?: string | null
  forceId?: string | null
}

export async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
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

export function createList(fields: ListFields): Promise<ArmyList> {
  return send<ArmyList>('/api/lists', 'POST', fields)
}

export function updateList(id: string, fields: ListFields): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}`, 'PUT', fields)
}

export function deleteList(id: string): Promise<void> {
  return send<void>(`/api/lists/${id}`, 'DELETE')
}

export function duplicateList(id: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/duplicate`, 'POST')
}

export function setListFeatured(id: string, featured: boolean): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/featured`, 'POST', { featured })
}

export function addForce(listId: string, name: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/forces`, 'POST', { name })
}

export function renameForce(listId: string, forceId: string, name: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/forces/${forceId}`, 'PUT', { name })
}

// Removes a section and every unit in it. A list keeps at least one section.
export function removeForce(listId: string, forceId: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/forces/${forceId}`, 'DELETE')
}

export function addEntry(listId: string, fields: EntryFields): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries`, 'POST', fields)
}

export function updateEntry(listId: string, entryId: string, fields: EntryFields): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}`, 'PUT', fields)
}

export function removeEntry(listId: string, entryId: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}`, 'DELETE')
}

// Persist the owner's custom ordering of entries (full id sequence).
export function reorderEntries(listId: string, entryIds: string[]): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/order`, 'POST', { entryIds })
}

export function setWarlord(listId: string, entryId: string, isWarlord: boolean): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/warlord`, 'POST', { isWarlord })
}

// Attach an entry to another, or null to detach.
export function attachEntry(listId: string, entryId: string, attachToEntryId: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/attach`, 'POST', { attachToEntryId })
}

// The fields of an existing entry, ready to resend (e.g. to duplicate it).
export function entryFields(e: ListEntry): EntryFields {
  return {
    name: e.name,
    points: e.points,
    modelCount: e.modelCount,
    wargear: e.wargear,
    enhancement: e.enhancement,
    notes: e.notes,
    forceId: e.forceId,
  }
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
