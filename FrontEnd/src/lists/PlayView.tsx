import { useEffect, useMemo, useState } from 'react'
import {
  getDatasheet,
  type DatasheetDetail,
  type DatasheetProfile,
  type DatasheetWeapon,
} from '../lib/catalogue'
import type { ArmyList, ListEntry } from '../lib/lists'
import { groupByRole } from './roles'
import { RuleText } from './RuleText'

// A read-optimized, printable view of the list: one datasheet card per entry,
// grouped by role, with the stat line, weapons, abilities (rules text) and the
// entry's chosen wargear. Datasheet detail is fetched once per unique datasheet.
export function PlayView({ list, onClose }: { list: ArmyList; onClose: () => void }) {
  const [details, setDetails] = useState<Record<string, DatasheetDetail>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  const ids = useMemo(() => [...new Set(list.entries.map((e) => e.datasheetId))], [list.entries])

  useEffect(() => {
    let active = true
    setLoading(true)
    Promise.all(ids.map((id) => getDatasheet(id).then((d) => [id, d] as const).catch(() => null))).then((pairs) => {
      if (!active) return
      const map: Record<string, DatasheetDetail> = {}
      for (const p of pairs) if (p) map[p[0]] = p[1]
      setDetails(map)
      setError(pairs.some((p) => p === null))
      setLoading(false)
    })
    return () => {
      active = false
    }
  }, [ids])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const groups = useMemo(() => groupByRole(list.entries, (e) => e.keywords), [list.entries])

  return (
    <div className="play-view">
      <header className="play-head">
        <div>
          <h2>{list.name}</h2>
          <span className="play-sub">
            {list.factionName}
            {list.battleSize ? ` · ${list.battleSize}` : ''}
            {list.pointsLimit != null || list.total > 0 ? ` · ${list.total} pts` : ''}
          </span>
        </div>
        <div className="play-actions">
          <button type="button" className="btn" onClick={() => window.print()}>Print</button>
          <button type="button" className="btn btn-primary" onClick={onClose}>Close</button>
        </div>
      </header>

      {loading ? (
        <p className="hint">Loading datasheets…</p>
      ) : (
        <>
          {error && <p className="builder-error">Some datasheets couldn’t load.</p>}
          <div className="play-cards">
            {groups.map((group) => (
              <section className="play-group" key={group.role}>
                <h3 className="play-role">{group.role}</h3>
                <div className="play-group-cards">
                  {group.items.map((e) => (
                    <PlayCard key={e.id} entry={e} detail={details[e.datasheetId]} />
                  ))}
                </div>
              </section>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function PlayCard({ entry, detail }: { entry: ListEntry; detail?: DatasheetDetail }) {
  const ranged = detail?.weapons.filter((w) => w.ranged) ?? []
  const melee = detail?.weapons.filter((w) => !w.ranged) ?? []

  return (
    <article className="play-card">
      <div className="pc-head">
        <h4>
          {entry.datasheetName}
          {entry.modelCount > 1 && <span className="pc-models"> · {entry.modelCount} models</span>}
        </h4>
        <span className="pc-pts">{entry.lineTotal} pts</span>
      </div>

      {(entry.isWarlord || entry.enhancementName) && (
        <div className="pc-badges">
          {entry.isWarlord && <span className="badge-warlord">★ Warlord</span>}
          {entry.enhancementName && (
            <span className="badge-enh">
              {entry.enhancementName}
              {entry.enhancementPoints ? ` (+${entry.enhancementPoints})` : ''}
            </span>
          )}
        </div>
      )}

      {!detail ? (
        <p className="hint">Datasheet unavailable.</p>
      ) : (
        <>
          {detail.keywords.length > 0 && (
            <div className="keywords">
              {detail.keywords.map((k) => (
                <span className="kw" key={k}>{k}</span>
              ))}
            </div>
          )}

          {detail.profiles.map((p) => (
            <StatTable key={p.name} profile={p} />
          ))}

          <WeaponTable title="Ranged weapons" weapons={ranged} />
          <WeaponTable title="Melee weapons" weapons={melee} />

          {detail.abilities.length > 0 && (
            <div className="pc-abilities">
              <h5>Abilities</h5>
              <dl className="rules-list">
                {detail.abilities.map((a) => (
                  <div className="rule" key={a.name}>
                    <dt>{a.name}</dt>
                    {a.text && <dd><RuleText text={a.text} /></dd>}
                  </div>
                ))}
              </dl>
            </div>
          )}

          {entry.selectedOptions.length > 0 && (
            <div className="pc-wargear">
              <span className="pc-sub">Wargear:</span> {entry.selectedOptions.map((o) => o.name).join(', ')}
            </div>
          )}
        </>
      )}
    </article>
  )
}

function StatTable({ profile }: { profile: DatasheetProfile }) {
  return (
    <div className="stat-scroll">
      <table className="stat-table">
        <thead>
          <tr>{profile.characteristics.map((c) => <th key={c.name}>{c.name}</th>)}</tr>
        </thead>
        <tbody>
          <tr>{profile.characteristics.map((c) => <td key={c.name}>{c.value || '—'}</td>)}</tr>
        </tbody>
      </table>
    </div>
  )
}

function WeaponTable({ title, weapons }: { title: string; weapons: DatasheetWeapon[] }) {
  if (weapons.length === 0) return null
  const cols = weapons[0].characteristics.map((c) => c.name)
  return (
    <div className="stat-scroll">
      <table className="stat-table">
        <thead>
          <tr>
            <th>{title}</th>
            {cols.map((c) => <th key={c}>{c}</th>)}
          </tr>
        </thead>
        <tbody>
          {weapons.map((w) => (
            <tr key={w.name}>
              <td>{w.name}</td>
              {w.characteristics.map((c) => <td key={c.name}>{c.value || '—'}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
