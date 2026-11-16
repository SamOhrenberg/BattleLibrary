import type { ArmyList, ListEntry, ListForce } from '../lib/lists'

export interface EntryItem {
  entry: ListEntry
  // Units attached to this one, shown with it.
  attached: ListEntry[]
}

export interface EntrySection {
  force: ListForce
  items: EntryItem[]
}

// Groups a list's entries by section, in saved order, nesting attached units
// under the unit they join. An attachment to a missing unit is shown standalone.
export function entryTree(list: ArmyList): { sections: EntrySection[] } {
  const ordered = [...list.entries].sort((a, b) => a.sortOrder - b.sortOrder)
  const ids = new Set(ordered.map((e) => e.id))
  const attachedTo = new Map<string, ListEntry[]>()
  for (const e of ordered) {
    if (e.attachedToEntryId && ids.has(e.attachedToEntryId)) {
      const arr = attachedTo.get(e.attachedToEntryId)
      if (arr) arr.push(e)
      else attachedTo.set(e.attachedToEntryId, [e])
    }
  }
  const isNested = (e: ListEntry) => !!e.attachedToEntryId && ids.has(e.attachedToEntryId)

  const forces = [...list.forces].sort((a, b) => a.sortOrder - b.sortOrder)
  const forceIds = new Set(forces.map((f) => f.id))
  const sections = forces.map((force, i) => ({
    force,
    items: ordered
      // Entries whose section is missing fall into the first one.
      .filter((e) => !isNested(e) && (e.forceId === force.id || (i === 0 && !forceIds.has(e.forceId))))
      .map((entry) => ({ entry, attached: attachedTo.get(entry.id) ?? [] })),
  }))
  return { sections }
}
