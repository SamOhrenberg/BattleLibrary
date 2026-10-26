import type { ArmyList, ListEntry } from '../lib/lists'

// 40k 11e list-legality checks for the builder. Point-less systems (Kill Team)
// have none of these, so only emptiness is checked there.
//
// Per battle size: points cap, Detachment Points budget, enhancement cap, and
// the "rule of N" datasheet cap. Onslaught's DP/enhancement figures are not yet
// confirmed by GW (11e launched mid-2026) — the values below are placeholders;
// adjust them here when official numbers land.

interface SizeRules {
  cap: number
  dp: number
  enhancements: number
  ruleOfN: number
}

export const BATTLE_SIZE_RULES: Record<string, SizeRules> = {
  Incursion: { cap: 1000, dp: 2, enhancements: 2, ruleOfN: 2 },
  'Strike Force': { cap: 2000, dp: 3, enhancements: 4, ruleOfN: 3 },
  Onslaught: { cap: 3000, dp: 5, enhancements: 4, ruleOfN: 3 }, // DP/enh = placeholder (TBC)
}

// Fallback for a Custom (no battle size) list: Strike Force-ish limits.
const DEFAULT_RULES: SizeRules = { cap: Infinity, dp: Infinity, enhancements: 4, ruleOfN: 3 }

export function rulesFor(battleSize: string | null): SizeRules {
  return (battleSize && BATTLE_SIZE_RULES[battleSize]) || DEFAULT_RULES
}

export const hasKeyword = (e: ListEntry, kw: string) =>
  e.keywords.some((k) => k.toLowerCase() === kw.toLowerCase())

export const isCharacter = (e: ListEntry) => hasKeyword(e, 'Character')
export const isEpicHero = (e: ListEntry) => hasKeyword(e, 'Epic Hero')

export function validateList(list: ArmyList, hasDetachments: boolean, pointless: boolean): string[] {
  const issues: string[] = []
  if (list.entries.length === 0) {
    issues.push('Add at least one unit.')
    return issues
  }
  if (pointless) {
    return issues
  }

  const rules = rulesFor(list.battleSize)

  if (list.pointsLimit != null && list.total > list.pointsLimit) {
    issues.push(`Over the ${list.pointsLimit} pt limit by ${list.total - list.pointsLimit}.`)
  }

  if (hasDetachments) {
    const dpUsed = list.detachments.reduce((n, d) => n + d.dp, 0)
    if (list.detachments.length === 0) {
      issues.push('Select a detachment.')
    } else if (list.detachments.length > 1 && dpUsed > rules.dp) {
      // A single detachment is always legal even if it exceeds the budget.
      issues.push(`Detachments cost ${dpUsed} DP, over the ${rules.dp} DP budget.`)
    }
  }

  const warlords = list.entries.filter((e) => e.isWarlord).length
  if (warlords === 0) {
    issues.push('Assign a Warlord.')
  } else if (warlords > 1) {
    issues.push('Only one Warlord is allowed.')
  }

  const enhancements = list.entries.filter((e) => e.enhancementId).length
  if (enhancements > rules.enhancements) {
    issues.push(`No more than ${rules.enhancements} Enhancements at this battle size.`)
  }

  // Rule of N: ≤N of a datasheet, Battleline ≤2N, Dedicated Transport unlimited,
  // Epic Hero ≤1.
  const groups = new Map<string, { n: number; name: string; max: number }>()
  for (const e of list.entries) {
    const max = hasKeyword(e, 'Dedicated Transport')
      ? Infinity
      : isEpicHero(e)
        ? 1
        : hasKeyword(e, 'Battleline')
          ? rules.ruleOfN * 2
          : rules.ruleOfN
    const g = groups.get(e.datasheetId) ?? { n: 0, name: e.datasheetName, max }
    g.n += e.quantity
    groups.set(e.datasheetId, g)
  }
  for (const g of groups.values()) {
    if (g.n > g.max) {
      issues.push(`Too many ${g.name}: ${g.n} of max ${g.max}.`)
    }
  }

  return issues
}
