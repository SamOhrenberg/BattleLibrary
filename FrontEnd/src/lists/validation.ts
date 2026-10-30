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

// What kind of problem an issue is, so the assistant knows how to resolve it.
export type IssueCode =
  | 'empty'
  | 'over-points'
  | 'no-detachment'
  | 'dp-over'
  | 'no-warlord'
  | 'too-many-warlords'
  | 'too-many-forced-warlords'
  | 'too-many-enhancements'
  | 'rule-of-n'

export interface ListIssue {
  code: IssueCode
  // One-line statement shown in the legality summary.
  message: string
  // How to resolve it, shown in the assistant.
  fix: string
  // Entries the fix acts on (warlord candidates, over-cap offenders, …).
  entryIds?: string[]
  // Amount over a budget, for points / DP messaging.
  amount?: number
}

export function validateList(list: ArmyList, hasDetachments: boolean, pointless: boolean): ListIssue[] {
  const issues: ListIssue[] = []
  if (list.entries.length === 0) {
    issues.push({
      code: 'empty',
      message: 'Add at least one unit.',
      fix: 'Battleline units are a solid start — add one below, or browse the full catalogue.',
    })
  }
  // Point-less systems (Kill Team) have no further legality rules.
  if (pointless) {
    return issues
  }

  // The remaining checks still run on an empty list so the assistant shows the
  // whole checklist up front (needs a detachment, needs a Warlord, …).
  const rules = rulesFor(list.battleSize)

  if (list.pointsLimit != null && list.total > list.pointsLimit) {
    const over = list.total - list.pointsLimit
    // Offer the priciest entries first as trim candidates.
    const priciest = [...list.entries].sort((a, b) => b.lineTotal - a.lineTotal).map((e) => e.id)
    issues.push({
      code: 'over-points',
      message: `Over the ${list.pointsLimit} pt limit by ${over}.`,
      fix: `Remove or shrink units to free up ${over} pts. The priciest entries are listed first.`,
      entryIds: priciest,
      amount: over,
    })
  }

  if (hasDetachments) {
    const dpUsed = list.detachments.reduce((n, d) => n + d.dp, 0)
    if (list.detachments.length === 0) {
      issues.push({
        code: 'no-detachment',
        message: 'Select a detachment.',
        fix: 'Pick a detachment below — it sets your detachment rule and the enhancements you can take.',
      })
    } else if (list.detachments.length > 1 && dpUsed > rules.dp) {
      // A single detachment is always legal even if it exceeds the budget.
      issues.push({
        code: 'dp-over',
        message: `Detachments cost ${dpUsed} DP, over the ${rules.dp} DP budget.`,
        fix: `Drop a detachment to get back to ${rules.dp} DP.`,
        amount: dpUsed - rules.dp,
      })
    }
  }

  const warlords = list.entries.filter((e) => e.isWarlord)
  const forced = list.entries.filter((e) => e.forcedWarlord)
  if (forced.length > 1) {
    // Two units that each must be the Warlord can't coexist; one has to go.
    issues.push({
      code: 'too-many-forced-warlords',
      message: 'Two units each require being your Warlord.',
      fix: 'Only one is allowed — remove one of these units.',
      entryIds: forced.map((e) => e.id),
    })
  } else if (warlords.length === 0) {
    const candidates = list.entries.filter(isCharacter).map((e) => e.id)
    issues.push({
      code: 'no-warlord',
      message: 'Assign a Warlord.',
      fix: candidates.length > 0
        ? 'Pick one of your Characters to be the Warlord.'
        : 'Add a Character and make it your Warlord.',
      entryIds: candidates,
    })
  } else if (warlords.length > 1) {
    issues.push({
      code: 'too-many-warlords',
      message: 'Only one Warlord is allowed.',
      fix: 'Keep a single Warlord and clear the others.',
      entryIds: warlords.map((e) => e.id),
    })
  }

  const enhanced = list.entries.filter((e) => e.enhancementId)
  if (enhanced.length > rules.enhancements) {
    issues.push({
      code: 'too-many-enhancements',
      message: `No more than ${rules.enhancements} Enhancements at this battle size.`,
      fix: `Remove ${enhanced.length - rules.enhancements} enhancement(s).`,
      entryIds: enhanced.map((e) => e.id),
      amount: enhanced.length - rules.enhancements,
    })
  }

  // Rule of N: ≤N of a datasheet, Battleline ≤2N, Dedicated Transport unlimited,
  // Epic Hero ≤1.
  const groups = new Map<string, { n: number; name: string; max: number; ids: string[] }>()
  for (const e of list.entries) {
    const max = hasKeyword(e, 'Dedicated Transport')
      ? Infinity
      : isEpicHero(e)
        ? 1
        : hasKeyword(e, 'Battleline')
          ? rules.ruleOfN * 2
          : rules.ruleOfN
    const g = groups.get(e.datasheetId) ?? { n: 0, name: e.datasheetName, max, ids: [] }
    g.n += 1
    g.ids.push(e.id)
    groups.set(e.datasheetId, g)
  }
  for (const g of groups.values()) {
    if (g.n > g.max) {
      issues.push({
        code: 'rule-of-n',
        message: `Too many ${g.name}: ${g.n} of max ${g.max}.`,
        fix: `Reduce ${g.name} to ${g.max} or fewer.`,
        entryIds: g.ids,
        amount: g.n - g.max,
      })
    }
  }

  return issues
}
