// Groups units by a single "role" derived from their keywords, for collapsible
// sections in the catalogue and the list.
//
// The unit's primary category (BSData's own battlefield-role flag) is the
// authoritative role, and the parser puts it first in the keyword list, so
// keywords[0] wins when it names a known role. Failing that - older cached data
// that predates the ordering, or a primary that is a faction keyword rather than
// a role - we fall back to a priority scan, first match wins, so a Character on a
// mount groups under Characters, not Mounted.

const ROLE_ORDER = [
  'Epic Heroes',
  'Characters',
  'Battleline',
  'Dedicated Transports',
  'Infantry',
  'Mounted',
  'Beasts',
  'Monsters',
  'Vehicles',
  'Fortifications',
  'Other',
] as const

// Single battlefield-role keyword -> its section. Keep in sync with the scan.
const ROLE_BY_KEYWORD: Record<string, string> = {
  'epic hero': 'Epic Heroes',
  character: 'Characters',
  battleline: 'Battleline',
  'dedicated transport': 'Dedicated Transports',
  infantry: 'Infantry',
  mounted: 'Mounted',
  beast: 'Beasts',
  monster: 'Monsters',
  vehicle: 'Vehicles',
  fortification: 'Fortifications',
}

export function roleOf(keywords: string[]): string {
  // Trust the primary category (first keyword) when it names a known role.
  const primary = keywords[0]?.toLowerCase()
  const primaryRole = primary ? ROLE_BY_KEYWORD[primary] : undefined
  if (primaryRole) return primaryRole

  // Fallback: priority scan over all keywords.
  const kw = new Set(keywords.map((k) => k.toLowerCase()))
  const has = (k: string) => kw.has(k.toLowerCase())
  if (has('Epic Hero')) return 'Epic Heroes'
  if (has('Character')) return 'Characters'
  if (has('Battleline')) return 'Battleline'
  if (has('Dedicated Transport')) return 'Dedicated Transports'
  if (has('Infantry')) return 'Infantry'
  if (has('Mounted')) return 'Mounted'
  if (has('Beast')) return 'Beasts'
  if (has('Monster')) return 'Monsters'
  if (has('Vehicle')) return 'Vehicles'
  if (has('Fortification')) return 'Fortifications'
  return 'Other'
}

// Groups items by role, returning sections in the canonical role order.
export function groupByRole<T>(items: T[], keywordsOf: (item: T) => string[]): { role: string; items: T[] }[] {
  const map = new Map<string, T[]>()
  for (const item of items) {
    const role = roleOf(keywordsOf(item))
    const bucket = map.get(role)
    if (bucket) bucket.push(item)
    else map.set(role, [item])
  }
  return ROLE_ORDER.filter((r) => map.has(r)).map((role) => ({ role, items: map.get(role)! }))
}
