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

// When a unit is selectable. requiredToggles: "Show X" opt-in content groups
// (allied daemons, Chaos Knights, Titans, ...) that must be enabled for it to
// appear. requiredDetachmentIds: detachment ids it is locked to (shown only when
// one is selected, e.g. Scintillating Legions needs Changehost of Deceit).
export interface DatasheetAvailability {
  requiredDetachmentIds: string[]
  requiredToggles: string[]
}

export interface CatalogueDatasheet {
  id: string
  name: string
  points: number | null
  keywords: string[]
  availability?: DatasheetAvailability
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
  // The model this choice is for (e.g. "Aspiring Sorcerer"); empty when it
  // applies unit-wide or the unit is a single model.
  model?: string
  // The choice set's stable id and selection bounds. groupMax 1 is a "pick one"
  // swap; groupMax 0 means no declared cap. Drive single-select in the builder.
  groupId?: string
  groupMin?: number
  groupMax?: number
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

// One weapon loadout within a variant group; max is the per-variant cap, points
// the additive cost per model taking it (usually 0 in 11e).
export interface ModelVariant {
  id: string
  name: string
  max: number
  points: number
  weapons: DatasheetWeapon[]
}

// A distributable model pool (e.g. Rubric Marines' "4-9 Rubric Marines") split
// across its weapon variants; max is the pool cap.
export interface ModelVariantGroup {
  id: string
  name: string
  max: number
  variants: ModelVariant[]
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
  // Distributable per-model loadout pools (empty for most units). fixedModelCount
  // is the always-present single models, so poolTarget = chosen size - it.
  variantGroups?: ModelVariantGroup[]
  fixedModelCount?: number
  availability?: DatasheetAvailability
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

// A Rule Search hit over already-cached content. `kind` is one of unit / ability
// / weapon / detachment-rule / enhancement; `datasheetId` is set for unit/ability
// /weapon so the UI can open the datasheet.
export interface RuleSearchResult {
  kind: 'unit' | 'ability' | 'weapon' | 'detachment-rule' | 'enhancement'
  title: string
  subtitle: string | null
  text: string | null
  datasheetId: string | null
  factionId: string | null
  factionName: string | null
}

export function searchRules(systemKey: string, q: string): Promise<RuleSearchResult[]> {
  return apiGet<RuleSearchResult[]>(
    `/api/catalogue/${encodeURIComponent(systemKey)}/search?q=${encodeURIComponent(q)}`,
  )
}
