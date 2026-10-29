import type { ArmyList } from '../lib/lists'
import { groupByRole } from './roles'

// Renders a list to a clean, shareable plaintext block, grouped by role.
export function listToText(list: ArmyList, pointless: boolean): string {
  const lines: string[] = []

  lines.push(
    pointless
      ? `${list.name} (${list.factionName})`
      : `${list.name} (${list.factionName}) — ${list.total}${list.pointsLimit ? `/${list.pointsLimit}` : ''} pts`,
  )
  if (!pointless && list.battleSize) lines.push(list.battleSize)
  if (list.detachments.length > 0) {
    lines.push(`Detachment: ${list.detachments.map((d) => d.name).join(', ')}`)
  }
  lines.push('')

  for (const group of groupByRole(list.entries, (e) => e.keywords)) {
    lines.push(group.role)
    for (const e of group.items) {
      const models = e.modelCount > 1 ? ` (${e.modelCount} models)` : ''
      const pts = pointless ? '' : ` (${e.lineTotal} pts)`
      lines.push(`  ${e.datasheetName}${models}${pts}`)
      if (e.isWarlord) lines.push('    ★ Warlord')
      if (e.enhancementName) {
        lines.push(`    Enhancement: ${e.enhancementName}${e.enhancementPoints ? ` (+${e.enhancementPoints})` : ''}`)
      }
      if (e.selectedOptions.length > 0) {
        lines.push(`    ${e.selectedOptions.map((o) => o.name).join(', ')}`)
      }
    }
    lines.push('')
  }

  lines.push(
    pointless
      ? `Operatives: ${list.entries.length}`
      : `Total: ${list.total} pts`,
  )
  return lines.join('\n').trim() + '\n'
}
