import type { ArmyList, ListEntry } from '../lib/lists'
import { groupByRole } from './roles'

// Renders a list to a clean, shareable plaintext block, grouped by role.
export function listToText(list: ArmyList, pointless: boolean): string {
  const lines: string[] = []

  lines.push(
    pointless
      ? `${list.name} (${list.factionName})`
      : `${list.name} (${list.factionName}) - ${list.total}${list.pointsLimit ? `/${list.pointsLimit}` : ''} pts`,
  )
  if (!pointless && list.battleSize) lines.push(list.battleSize)
  if (!pointless && list.forceDisposition) lines.push(`Disposition: ${list.forceDisposition}`)
  if (list.detachments.length > 0) {
    lines.push(`Detachment: ${list.detachments.map((d) => d.name).join(', ')}`)
  }
  lines.push('')

  // Attached Leaders print indented under their Bodyguard, not in their own role.
  const ids = new Set(list.entries.map((e) => e.id))
  const leadersBy = new Map<string, ListEntry[]>()
  const nested = new Set<string>()
  for (const e of list.entries) {
    if (e.attachedToEntryId && ids.has(e.attachedToEntryId)) {
      const arr = leadersBy.get(e.attachedToEntryId)
      if (arr) arr.push(e)
      else leadersBy.set(e.attachedToEntryId, [e])
      nested.add(e.id)
    }
  }

  function printEntry(e: ListEntry, indent: string) {
    const models = e.modelCount > 1 ? ` (${e.modelCount} models)` : ''
    const pts = pointless ? '' : ` (${e.lineTotal} pts)`
    const label = e.customName ? `${e.customName} [${e.datasheetName}]` : e.datasheetName
    lines.push(`${indent}${label}${models}${pts}`)
    const sub = indent + '  '
    if (e.isWarlord) lines.push(`${sub}★ Warlord`)
    if (e.enhancementName) {
      lines.push(`${sub}Enhancement: ${e.enhancementName}${e.enhancementPoints ? ` (+${e.enhancementPoints})` : ''}`)
    }
    if (e.selectedOptions.length > 0) {
      lines.push(`${sub}${e.selectedOptions.map((o) => (o.quantity > 1 ? `${o.quantity}× ${o.name}` : o.name)).join(', ')}`)
    }
    if (e.notes) {
      for (const note of e.notes.split('\n')) lines.push(`${sub}Note: ${note}`)
    }
  }

  const multiForce = list.forces.length > 1
  const forces = list.forces.length > 0 ? list.forces : [{ id: '', factionName: list.factionName }]
  for (const force of forces) {
    const forceEntries = list.entries.filter(
      (e) => (force.id ? e.forceId === force.id : true) && !nested.has(e.id),
    )
    if (multiForce) lines.push(`== ${force.factionName} ==`)
    for (const group of groupByRole(forceEntries, (e) => e.keywords)) {
      lines.push(group.role)
      for (const e of group.items) {
        const leaders = leadersBy.get(e.id) ?? []
        // Leader(s) on top, then the Bodyguard embedded beneath.
        for (const leader of leaders) {
          printEntry(leader, '  ')
        }
        printEntry(e, leaders.length > 0 ? '    ↳ ' : '  ')
      }
      lines.push('')
    }
  }

  lines.push(
    pointless
      ? `Operatives: ${list.entries.length}`
      : `Total: ${list.total} pts`,
  )
  return lines.join('\n').trim() + '\n'
}
