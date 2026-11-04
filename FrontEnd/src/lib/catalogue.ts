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

// A keyword a unit gains (add: true) or loses (add: false) only when a gate is
// active, from the datasheet's conditional "category" modifiers (e.g. gaining
// Battleline under a specific detachment). Unconditional changes are already
// baked into `keywords`; these apply when one of requiredDetachmentIds is the
// selected detachment, or one of requiredToggles ("Show X") is enabled.
export interface ConditionalKeyword {
  keyword: string
  add: boolean
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

// A source citation: the book (its short name where available, e.g. "Orks") and
// the page within it. page may be empty when only the publication is known.
export interface Citation {
  publication: string
  page: string
}

// A named rule with its text: a datasheet ability, a detachment rule, etc.
// source, when present, cites the book and page it is printed in.
export interface AbilityText {
  name: string
  text: string
  source?: Citation | null
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
  // True for the choice named by its group's default: pre-selected when the unit
  // is added, so starting wargear and points match the datasheet.
  isDefault?: boolean
  // Weapon stat line(s) this choice grants (empty for plain upgrades).
  weapons: DatasheetWeapon[]
}

export interface UnitSize {
  models: number
  points: number
}

// A selection-dependent points adjustment (Crucible warband weapon pricing).
// countedOptionIds are the option ids the rule counts. amount is signed.
// perSelection: add amount once per `step` selected. Otherwise (threshold) add
// amount once when the selected count satisfies `operator` against `value`.
export interface CostRule {
  countedOptionIds: string[]
  amount: number
  perSelection: boolean
  step: number
  operator: string
  value: number
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
  // True for the loadout the pool fills with first (the group's default).
  isDefault?: boolean
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

// Leader attachment (11e): which units this datasheet (a Leader character) can
// attach to. label is the association label ("Leader", "Supported by"); targets
// are the unit/category names it may lead. Empty for non-leaders.
export interface LeaderAttachment {
  label: string
  targets: string[]
}

export interface DatasheetDetail {
  id: string
  name: string
  points: number | null
  keywords: string[]
  // The primary category (battlefield role / faction keyword); also in keywords.
  // Empty when the source flags no primary category.
  primaryKeyword?: string
  profiles: DatasheetProfile[]
  weapons: DatasheetWeapon[]
  // The datasheet's own abilities. Referenced core/keyword rules (Deep Strike,
  // Devastating Wounds, ...) are kept apart in `rules`; absent on older cached
  // data written before the split.
  abilities: AbilityText[]
  rules?: AbilityText[]
  options: DatasheetOption[]
  equipment?: string[]
  sizes: UnitSize[]
  modelGroups: ModelGroup[]
  // Distributable per-model loadout pools (empty for most units). fixedModelCount
  // is the always-present single models, so poolTarget = chosen size - it.
  variantGroups?: ModelVariantGroup[]
  fixedModelCount?: number
  availability?: DatasheetAvailability
  // Selection-dependent pricing rules (Crucible warband weapons); usually empty.
  costRules?: CostRule[]
  // Keyword changes that only apply under a selected detachment / config toggle
  // (empty for most units); feeds the Battleline and rule-of-N legality checks.
  conditionalKeywords?: ConditionalKeyword[]
  // The units this Leader can attach to (empty for non-leaders / older data).
  leaderAttachments?: LeaderAttachment[]
  // The book and page this datasheet is printed in; absent when uncited.
  source?: Citation | null
}

// Points adjustment from a datasheet's cost rules for a given set of selected
// option ids (with quantities). Mirrors the server's DynamicPoints so the
// builder preview matches the saved total.
export function dynamicPoints(
  rules: CostRule[] | undefined,
  selected: ReadonlyArray<{ id: string; quantity?: number }>,
): number {
  if (!rules || rules.length === 0) return 0
  let total = 0
  for (const rule of rules) {
    const counted = new Set(rule.countedOptionIds)
    const count = selected
      .filter((o) => counted.has(o.id))
      .reduce((n, o) => n + Math.max(1, o.quantity ?? 1), 0)
    if (rule.perSelection) {
      total += rule.amount * Math.floor(count / Math.max(1, rule.step))
    } else if (qualifies(count, rule.operator, rule.value)) {
      total += rule.amount
    }
  }
  return total
}

function qualifies(count: number, op: string, value: number): boolean {
  switch (op) {
    case 'atLeast':
      return count >= value
    case 'greaterThan':
      return count > value
    case 'atMost':
      return count <= value
    case 'lessThan':
      return count < value
    case 'equalTo':
      return count === value
    default:
      return false
  }
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
