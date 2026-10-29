import { apiGet } from './api'

// Read-only catalogue API. Ids returned here are the stable BSData ids that
// saved lists reference.

export interface CatalogueSystem {
  key: string
  name: string
  commitSha: string
  refreshedAt: string
  factions: number
  datasheets: number
  // True for point-less systems (e.g. Kill Team 2024): no points limits.
  pointless: boolean
}

export interface CatalogueFaction {
  id: string
  name: string
  datasheets: number
}

export interface CatalogueDatasheet {
  id: string
  name: string
  points: number | null
  keywords: string[]
}

// A named rule with its text: a datasheet ability, a detachment rule, etc.
export interface AbilityText {
  name: string
  text: string
}

export interface CatalogueEnhancement {
  id: string
  name: string
  points: number
  text: string
}

export interface CatalogueDetachment {
  id: string
  name: string
  dp: number
  enhancements: CatalogueEnhancement[]
  rules: AbilityText[]
}

export interface FactionDatasheets {
  faction: { id: string; name: string; equipment?: string[]; detachments?: CatalogueDetachment[] }
  datasheets: CatalogueDatasheet[]
}

export interface DatasheetCharacteristic {
  name: string
  value: string
}

export interface DatasheetProfile {
  name: string
  characteristics: DatasheetCharacteristic[]
}

export interface DatasheetWeapon {
  name: string
  ranged: boolean
  characteristics: DatasheetCharacteristic[]
}

export interface DatasheetOption {
  id: string
  name: string
  points: number
  group: string
  // Weapon stat line(s) this choice grants (empty for plain upgrades).
  weapons: DatasheetWeapon[]
}

export interface UnitSize {
  models: number
  points: number
}

export interface ModelGroup {
  label: string
  weapons: string[]
}

export interface DatasheetDetail {
  id: string
  name: string
  points: number | null
  keywords: string[]
  profiles: DatasheetProfile[]
  weapons: DatasheetWeapon[]
  abilities: AbilityText[]
  options: DatasheetOption[]
  equipment?: string[]
  sizes: UnitSize[]
  modelGroups: ModelGroup[]
}

export function getCatalogueSystems(): Promise<CatalogueSystem[]> {
  return apiGet<CatalogueSystem[]>('/api/catalogue/systems')
}

export function getFactions(systemKey: string): Promise<CatalogueFaction[]> {
  return apiGet<CatalogueFaction[]>(`/api/catalogue/${encodeURIComponent(systemKey)}/factions`)
}

export function getDatasheets(factionId: string): Promise<FactionDatasheets> {
  return apiGet<FactionDatasheets>(`/api/catalogue/factions/${encodeURIComponent(factionId)}/datasheets`)
}

export function getDatasheet(datasheetId: string): Promise<DatasheetDetail> {
  return apiGet<DatasheetDetail>(`/api/catalogue/datasheets/${encodeURIComponent(datasheetId)}`)
}
