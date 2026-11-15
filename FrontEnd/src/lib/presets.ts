import { send } from './lists'

// Saved unit templates, reusable across the owner's lists for one game system.

export interface UnitPreset {
  id: string
  gameSystemKey: string
  name: string
  unitName: string
  points: number | null
  modelCount: number | null
  wargear: string | null
  enhancement: string | null
  notes: string | null
  updatedAt: string
}

export interface PresetFields {
  gameSystemKey: string
  name: string
  unitName: string
  points?: number | null
  modelCount?: number | null
  wargear?: string | null
  enhancement?: string | null
  notes?: string | null
}

export function getPresets(system?: string): Promise<UnitPreset[]> {
  return send<UnitPreset[]>(`/api/presets${system ? `?system=${encodeURIComponent(system)}` : ''}`, 'GET')
}

export function createPreset(fields: PresetFields): Promise<UnitPreset> {
  return send<UnitPreset>('/api/presets', 'POST', fields)
}

export function deletePreset(id: string): Promise<void> {
  return send<void>(`/api/presets/${id}`, 'DELETE')
}
