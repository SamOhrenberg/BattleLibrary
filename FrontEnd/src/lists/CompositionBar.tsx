import { useMemo } from 'react'
import type { ArmyList } from '../lib/lists'
import { groupByRole } from './roles'
import { rulesFor } from './validation'

// At-a-glance composition for a pointed list: a points-vs-cap meter, the DP and
// enhancement budgets, and a points-by-role breakdown (single-hue magnitude bars
// showing each role's share of the total).
export function CompositionBar({ list, hasDetachments }: { list: ArmyList; hasDetachments: boolean }) {
  const rules = rulesFor(list.battleSize)
  const total = list.total
  const cap = list.pointsLimit
  const over = cap != null && total > cap
  const pct = cap != null && cap > 0 ? Math.min(100, (total / cap) * 100) : null

  const dpUsed = list.detachments.reduce((n, d) => n + d.dp, 0)
  const enhUsed = list.entries.filter((e) => e.enhancementId).length

  const roles = useMemo(
    () =>
      groupByRole(list.entries, (e) => e.keywords)
        .map((g) => ({ role: g.role, pts: g.items.reduce((n, e) => n + e.lineTotal, 0) }))
        .filter((r) => r.pts > 0)
        .sort((a, b) => b.pts - a.pts),
    [list.entries],
  )

  return (
    <div className="composition">
      <div className="comp-top">
        <div className="comp-points">
          <div className="cp-head">
            <span className="cp-label">Points</span>
            <span className={`cp-val${over ? ' over' : ''}`}>
              {total}
              {cap != null ? ` / ${cap}` : ''}
            </span>
          </div>
          {pct != null && (
            <div className="meter" role="meter" aria-valuenow={total} aria-valuemin={0} aria-valuemax={cap ?? undefined}>
              <div className={`meter-fill${over ? ' over' : ''}`} style={{ width: `${pct}%` }} />
            </div>
          )}
        </div>
        <div className="comp-stats">
          {hasDetachments && (
            <span className="comp-stat">
              <b>
                {dpUsed}
                {Number.isFinite(rules.dp) ? `/${rules.dp}` : ''}
              </b>{' '}
              DP
            </span>
          )}
          <span className="comp-stat">
            <b>
              {enhUsed}/{rules.enhancements}
            </b>{' '}
            Enh
          </span>
        </div>
      </div>

      {roles.length > 0 && total > 0 && (
        <div className="comp-roles">
          {roles.map((r) => (
            <div className="comp-role" key={r.role}>
              <span className="cr-label">{r.role}</span>
              <div className="cr-track">
                <div className="cr-fill" style={{ width: `${(r.pts / total) * 100}%` }} />
              </div>
              <span className="cr-pts">{r.pts}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
