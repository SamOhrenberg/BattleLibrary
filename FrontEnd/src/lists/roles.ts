// Groups units by a single "role" derived from their keywords, for collapsible
// sections in the catalogue and the list. First matching role in priority order
// wins, so a Character on a mount groups under Characters, not Mounted.

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

export function roleOf(keywords: string[]): string {
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
