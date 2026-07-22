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
}

export interface FactionDatasheets {
  faction: { id: string; name: string; equipment?: string[] }
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
}

export interface DatasheetDetail {
  id: string
  name: string
  points: number | null
  keywords: string[]
  profiles: DatasheetProfile[]
  weapons: DatasheetWeapon[]
  abilities: string[]
  options: DatasheetOption[]
  equipment?: string[]
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
