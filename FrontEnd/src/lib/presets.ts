import { ApiError, authFetch } from './api'
import type { SelectedOption, VariantQuantity } from './lists'

// Saved loadouts (wargear + size) for a datasheet, reusable across lists.

export interface LoadoutPreset {
  id: string
  gameSystemKey: string
  datasheetId: string
  datasheetName: string
  name: string
  modelCount: number
  // Base size points; total adds the options.
  points: number
  total: number
  selectedOptions: SelectedOption[]
  updatedAt: string
}

export interface CreatePresetInput {
  gameSystemKey: string
  datasheetId: string
  name: string
  models?: number
  selectedOptionIds: string[]
  variants?: VariantQuantity[]
}

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await authFetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      const data = await res.json()
      if (data?.errors && typeof data.errors === 'object') {
        const messages = Object.values(data.errors as Record<string, string[]>).flat()
        if (messages.length > 0) message = messages.join(' ')
      } else if (typeof data?.error === 'string') {
        message = data.error
      }
    } catch {
      // No JSON body; keep the generic message.
    }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) {
    return undefined as T
  }
  return res.json() as Promise<T>
}

export function getPresets(system?: string, datasheetId?: string): Promise<LoadoutPreset[]> {
  const params = new URLSearchParams()
  if (system) params.set('system', system)
  if (datasheetId) params.set('datasheetId', datasheetId)
  const q = params.toString()
  return send<LoadoutPreset[]>(`/api/presets${q ? `?${q}` : ''}`, 'GET')
}

export function createPreset(input: CreatePresetInput): Promise<LoadoutPreset> {
  return send<LoadoutPreset>('/api/presets', 'POST', input)
}

export function deletePreset(id: string): Promise<void> {
  return send<void>(`/api/presets/${id}`, 'DELETE')
}
