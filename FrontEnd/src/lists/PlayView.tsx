import { useEffect, useMemo, useState } from 'react'
import {
  getDatasheet,
  getDatasheets,
  type CatalogueDetachment,
  type DatasheetDetail,
  type DatasheetProfile,
  type DatasheetWeapon,
} from '../lib/catalogue'
import type { ArmyList, ListEntry } from '../lib/lists'
import { groupByRole } from './roles'
import { RuleText } from './RuleText'
import { RuleSearch } from './RuleSearch'
import { DatasheetModal } from '../games/DatasheetModal'
import { OverflowMenu } from '../ui/OverflowMenu'

// A read-optimized, printable view of the list: one datasheet card per entry,
// grouped by role, with the stat line, weapons, abilities (rules text) and the
// entry's chosen wargear. Datasheet detail is fetched once per unique datasheet.
const shortFaction = (name: string) => name.split(' - ').pop() || name

// `context` adds a lead-in to the subtitle, e.g. the live game's name and round.
export function PlayView({ list, context, onClose }: { list: ArmyList; context?: string; onClose: () => void }) {
  const [details, setDetails] = useState<Record<string, DatasheetDetail>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [factionDetachments, setFactionDetachments] = useState<CatalogueDetachment[]>([])
  const [showSearch, setShowSearch] = useState(false)
  const [lookup, setLookup] = useState<{ id: string; name: string } | null>(null)

  // Escape closes, unless a sheet layered on top handles it first.
  useEffect(() => {
    if (showSearch || lookup) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [showSearch, lookup, onClose])

  // Collapsed detachment rules still print in full.
  useEffect(() => {
    const expand = () =>
      document.querySelectorAll<HTMLDetailsElement>('.play-det').forEach((d) => {
        d.open = true
      })
    window.addEventListener('beforeprint', expand)
    return () => window.removeEventListener('beforeprint', expand)
  }, [])

  const ids =useMemo(() => [...new Set(list.entries.map((e) => e.datasheetId))], [list.entries])

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

  // The primary faction's detachments, to show the chosen detachment's rules.
  useEffect(() => {
    let active = true
    getDatasheets(list.factionId)
      .then((d) => active && setFactionDetachments(d.faction.detachments ?? []))
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [list.factionId])

  // Rules for the detachment(s) the list actually chose.
  const chosenIds = new Set(list.detachments.map((d) => d.id))
  const detachmentRules = factionDetachments.filter((d) => chosenIds.has(d.id) && d.rules.length > 0)

  // Attached Leaders render beside their Bodyguard, not in their own role group.
  const { leadersBy, nested } = useMemo(() => {
    const entryIds = new Set(list.entries.map((e) => e.id))
    const by = new Map<string, ListEntry[]>()
    const nestedIds = new Set<string>()
    for (const e of list.entries) {
      if (e.attachedToEntryId && entryIds.has(e.attachedToEntryId)) {
        const arr = by.get(e.attachedToEntryId)
        if (arr) arr.push(e)
        else by.set(e.attachedToEntryId, [e])
        nestedIds.add(e.id)
      }
    }
    return { leadersBy: by, nested: nestedIds }
  }, [list.entries])

  const multiForce = list.forces.length > 1
  const forces = list.forces.length > 0 ? list.forces : [{ id: '', factionName: list.factionName }]
  const roleGroupsFor = (forceId: string) =>
    groupByRole(
      list.entries.filter((e) => (forceId ? e.forceId === forceId : true) && !nested.has(e.id)),
      (e) => e.keywords,
    )

  return (
    <div className="play-view">
      <header className="play-head">
        <button type="button" className="play-back" aria-label="Close play view" onClick={onClose}>
          <span aria-hidden="true">‹</span>
        </button>
        <div className="play-title">
          <h2>{list.name}</h2>
          <span className="play-sub">
            {context ? `${context} · ` : ''}
            {shortFaction(list.factionName)}
            {list.pointsLimit != null || list.total > 0 ? ` · ${list.total} pts` : ''}
          </span>
        </div>
        <div className="play-actions">
          <button type="button" className="play-icon" aria-label="Rule search" onClick={() => setShowSearch(true)}>
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <circle cx="11" cy="11" r="6.5" />
              <path d="M16 16l4.5 4.5" strokeLinecap="round" />
            </svg>
          </button>
          <OverflowMenu label="More play view actions" items={[{ label: 'Print', onSelect: () => window.print() }]} />
        </div>
      </header>

      {loading ? (
        <p className="hint">Loading datasheets…</p>
      ) : (
        <>
          {error && <p className="builder-error">Some datasheets couldn’t load.</p>}
          <div className="play-cards">
            {forces.map((force) => (
              <div className="play-force-section" key={force.id || 'single'}>
                {multiForce && <h3 className="play-force">{force.factionName}</h3>}
                {roleGroupsFor(force.id).map((group) => (
                  <section className="play-group" key={group.role}>
                    <h3 className="play-role">{group.role}</h3>
                    <div className="play-group-cards">
                      {group.items.map((e) => {
                        const leaders = leadersBy.get(e.id)
                        if (leaders && leaders.length > 0) {
                          return (
                            <div className="play-attached" key={e.id}>
                              <span className="play-attached-label">Attached unit</span>
                              {leaders.map((l) => (
                                <PlayCard key={l.id} entry={l} detail={details[l.datasheetId]} />
                              ))}
                              <PlayCard entry={e} detail={details[e.datasheetId]} />
                            </div>
                          )
                        }
                        return <PlayCard key={e.id} entry={e} detail={details[e.datasheetId]} />
                      })}
                    </div>
                  </section>
                ))}
              </div>
            ))}
          </div>
          {detachmentRules.length > 0 && (
            <section className="play-group play-detachments">
              <h3 className="play-role">Detachment rules</h3>
              {detachmentRules.map((d) => (
                <details className="pc-abilities play-det" key={d.id}>
                  <summary>{d.name}</summary>
                  <dl className="rules-list">
                    {d.rules.map((r) => (
                      <div className="rule" key={r.name}>
                        <dt>{r.name}</dt>
                        {r.text && <dd><RuleText text={r.text} /></dd>}
                      </div>
                    ))}
                  </dl>
                </details>
              ))}
            </section>
          )}
        </>
      )}

      {showSearch && (
        <RuleSearch
          systemKey={list.gameSystemKey}
          onOpenDatasheet={(id, name) => setLookup({ id, name })}
          onClose={() => setShowSearch(false)}
        />
      )}
      {lookup && <DatasheetModal datasheetId={lookup.id} title={lookup.name} onClose={() => setLookup(null)} />}
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
          {entry.customName || entry.datasheetName}
          {entry.modelCount > 1 && <span className="pc-models"> · {entry.modelCount} models</span>}
        </h4>
        <span className="pc-pts">{entry.lineTotal} pts</span>
      </div>
      {entry.customName && <div className="pc-realname">{entry.datasheetName}</div>}

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
          {entry.notes && <div className="pc-note">{entry.notes}</div>}
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
          <tr>{profile.characteristics.map((c) => <td key={c.name}>{c.value || '-'}</td>)}</tr>
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
              {w.characteristics.map((c) => <td key={c.name}>{c.value || '-'}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
