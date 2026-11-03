import { ApiError, authFetch } from './api'

// Account-scoped army lists. Every mutating call returns the full list so the
// client can replace its state from the response.

export interface SelectedOption {
  id: string
  name: string
  points: number
  // How many of this choice: 1 for a plain upgrade, the chosen count for a
  // per-model loadout variant.
  quantity: number
}

// A per-model loadout variant and the count chosen for it.
export interface VariantQuantity {
  id: string
  quantity: number
}

export interface ListForce {
  id: string
  factionId: string
  factionName: string
  sortOrder: number
}

export interface ListEntry {
  id: string
  // The force (faction slice) this entry belongs to.
  forceId: string
  datasheetId: string
  datasheetName: string
  // Player-given name for the unit; null shows the datasheet name.
  customName: string | null
  // Free-text note the owner keeps against the unit.
  notes: string | null
  points: number
  optionsPoints: number
  modelCount: number
  sortOrder: number
  lineTotal: number
  keywords: string[]
  isWarlord: boolean
  forcedWarlord: boolean
  // Leader attachment: the bodyguard entry this Leader is attached to, or null.
  attachedToEntryId: string | null
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
  usesLegends: boolean
  usesCrucible: boolean
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
  forceDisposition: string | null
  usesLegends: boolean
  usesCrucible: boolean
  detachments: ListDetachment[]
  forces: ListForce[]
  updatedAt: string
  entries: ListEntry[]
}

export interface CreateListInput {
  name: string
  gameSystemKey: string
  factionId: string
  pointsLimit: number | null
  // Null (or "Custom") keeps the manual points limit; a battle size sets its cap.
  battleSize?: string | null
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

export function duplicateList(id: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/duplicate`, 'POST')
}

// Downloads the list as a BattleScribe .rosz (opens in BattleScribe / NewRecruit).
export async function exportRoster(id: string, fileName: string): Promise<void> {
  const res = await authFetch(`/api/lists/${id}/export`)
  if (!res.ok) {
    throw await toError(res)
  }
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${sanitizeFileName(fileName)}.rosz`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export interface ImportRosterResult {
  list: ArmyList
  // Names of selections in the file that didn't match a cached datasheet/option.
  unmatched: string[]
}

// Imports a BattleScribe roster (.ros or .rosz) as a new list.
export async function importRoster(file: File): Promise<ImportRosterResult> {
  const form = new FormData()
  form.append('file', file)
  const res = await authFetch('/api/lists/import', { method: 'POST', body: form })
  if (!res.ok) {
    throw await toError(res)
  }
  return res.json() as Promise<ImportRosterResult>
}

function sanitizeFileName(name: string): string {
  const cleaned = name.replace(/[\\/:*?"<>|]/g, '_').trim()
  return cleaned.length > 0 ? cleaned : 'roster'
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

export function setForceDisposition(id: string, forceDisposition: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${id}/disposition`, 'POST', { forceDisposition })
}

export function setWarlord(listId: string, entryId: string, isWarlord: boolean): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/warlord`, 'POST', { isWarlord })
}

// Attach a Leader (entryId) to a bodyguard unit, or null to detach.
export function attachEntry(listId: string, entryId: string, attachToEntryId: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/attach`, 'POST', { attachToEntryId })
}

export function setEnhancement(listId: string, entryId: string, enhancementId: string | null): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/enhancement`, 'POST', { enhancementId })
}

export function addEntry(
  listId: string,
  datasheetId: string,
  selectedOptionIds: string[] = [],
  models?: number,
  forceId?: string,
  variants?: VariantQuantity[],
): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries`, 'POST', {
    datasheetId,
    selectedOptionIds,
    models,
    forceId,
    variants,
  })
}

// Add an allied force (a faction) to the list.
export function addForce(listId: string, factionId: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/forces`, 'POST', { factionId })
}

// Remove a force and all of its entries. A list must keep at least one force.
export function removeForce(listId: string, forceId: string): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/forces/${forceId}`, 'DELETE')
}

export function setModelCount(
  listId: string,
  entryId: string,
  models: number,
  variants?: VariantQuantity[],
): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}/size`, 'POST', { models, variants })
}

// Persist the owner's custom ordering of entries (full id sequence).
export function reorderEntries(listId: string, entryIds: string[]): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/order`, 'POST', { entryIds })
}

// A partial entry edit. An omitted field is left unchanged; for customName/notes
// an empty string clears the value.
export interface EntryPatch {
  selectedOptionIds?: string[]
  customName?: string | null
  notes?: string | null
  variants?: VariantQuantity[]
}

export function updateEntry(listId: string, entryId: string, patch: EntryPatch): Promise<ArmyList> {
  return send<ArmyList>(`/api/lists/${listId}/entries/${entryId}`, 'PUT', patch)
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
