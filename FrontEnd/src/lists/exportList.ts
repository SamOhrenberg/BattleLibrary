import { systemName } from '../lib/gameSystems'
import type { ArmyList, ListEntry } from '../lib/lists'
import { entryTree } from './entryTree'

// Renders a list to a plain-text block for sharing.
export function listToText(list: ArmyList): string {
  const lines: string[] = []
  const pts = list.total > 0 || list.pointsLimit != null
  lines.push(`${list.name}${pts ? ` - ${list.total}${list.pointsLimit != null ? `/${list.pointsLimit}` : ''} pts` : ''}`)
  lines.push([systemName(list.gameSystemKey), list.factionName].filter(Boolean).join(' · '))
  if (list.detachment) lines.push(`Detachment: ${list.detachment}`)
  if (list.notes) lines.push('', list.notes)
  lines.push('')

  function printEntry(e: ListEntry, indent: string) {
    const meta = [
      e.modelCount != null ? `${e.modelCount} ${e.modelCount === 1 ? 'model' : 'models'}` : null,
      e.points != null ? `${e.points} pts` : null,
    ].filter(Boolean)
    lines.push(`${indent}${e.name}${meta.length > 0 ? ` (${meta.join(', ')})` : ''}`)
    const sub = indent + '  '
    if (e.isWarlord) lines.push(`${sub}Warlord`)
    if (e.enhancement) lines.push(`${sub}Enhancement: ${e.enhancement}`)
    for (const w of (e.wargear ?? '').split('\n').map((x) => x.trim()).filter(Boolean)) {
      lines.push(`${sub}- ${w}`)
    }
    if (e.notes) lines.push(`${sub}Note: ${e.notes}`)
  }

  const { sections } = entryTree(list)
  const multi = sections.length > 1
  for (const section of sections) {
    if (multi) lines.push(`== ${section.force.name} ==`)
    for (const item of section.items) {
      printEntry(item.entry, '')
      for (const a of item.attached) printEntry(a, '  + ')
    }
    if (multi) lines.push('')
  }
  return lines.join('\n').trimEnd() + '\n'
}
