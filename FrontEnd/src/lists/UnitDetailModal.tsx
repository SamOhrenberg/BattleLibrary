import { useEffect, useMemo, useState } from 'react'
import { getDatasheet, type DatasheetDetail, type DatasheetOption } from '../lib/catalogue'

interface Props {
  datasheetId: string
  onAdd: (selectedOptionIds: string[]) => void
  onClose: () => void
}

export function UnitDetailModal({ datasheetId, onAdd, onClose }: Props) {
  const [detail, setDetail] = useState<DatasheetDetail | null>(null)
  const [error, setError] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())

  useEffect(() => {
    let active = true
    setDetail(null)
    setError(false)
    setSelected(new Set())
    getDatasheet(datasheetId)
      .then((d) => active && setDetail(d))
      .catch(() => active && setError(true))
    return () => {
      active = false
    }
  }, [datasheetId])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const grouped = useMemo(() => groupOptions(detail?.options ?? []), [detail])
  const optionsTotal = useMemo(() => {
    if (!detail) return 0
    return detail.options
      .filter((o) => selected.has(o.id))
      .reduce((sum, o) => sum + o.points, 0)
  }, [detail, selected])

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const subtotal = (detail?.points ?? 0) + optionsTotal

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        {error ? (
          <p className="builder-error">Could not load this unit.</p>
        ) : !detail ? (
          <p className="hint">Loading…</p>
        ) : (
          <>
            <div className="modal-head">
              <h3>{detail.name}</h3>
              {detail.points != null && <span className="unit-pts">{detail.points} pts</span>}
            </div>

            {detail.keywords.length > 0 && (
              <div className="keywords">
                {detail.keywords.map((k) => (
                  <span className="kw" key={k}>{k}</span>
                ))}
              </div>
            )}

            {detail.profiles.map((p) => (
              <StatTable key={p.name} labels={p.characteristics.map((c) => c.name)} row={p.characteristics} />
            ))}

            {detail.weapons.length > 0 && <WeaponTables weapons={detail.weapons} />}

            {detail.abilities.length > 0 && (
              <div className="detail-section">
                <h4>Abilities</h4>
                <ul>
                  {detail.abilities.map((a) => (
                    <li key={a}>{a}</li>
                  ))}
                </ul>
              </div>
            )}

            {detail.equipment && detail.equipment.length > 0 && (
              <div className="detail-section">
                <h4>Equipment</h4>
                <ul>
                  {detail.equipment.map((e) => (
                    <li key={e}>{e}</li>
                  ))}
                </ul>
              </div>
            )}

            {grouped.length > 0 && (
              <div className="detail-section">
                <h4>Options</h4>
                {grouped.map((g) => (
                  <div className="option-group" key={g.name}>
                    {g.name && <div className="option-group-name">{g.name}</div>}
                    {g.options.map((o) => (
                      <label className="option-row" key={o.id}>
                        <input
                          type="checkbox"
                          checked={selected.has(o.id)}
                          onChange={() => toggle(o.id)}
                        />
                        <span className="opt-name">{o.name}</span>
                        <span className="opt-pts">{o.points > 0 ? `+${o.points}` : 'free'}</span>
                      </label>
                    ))}
                  </div>
                ))}
              </div>
            )}

            <div className="modal-actions">
              {detail.points != null && (
                <span className="modal-subtotal">
                  Subtotal <b>{subtotal}</b> pts
                </span>
              )}
              <button type="button" className="btn btn-primary" onClick={() => onAdd([...selected])}>
                Add to list
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

interface OptionGroup {
  name: string
  options: DatasheetOption[]
}

function groupOptions(options: DatasheetOption[]): OptionGroup[] {
  const groups: OptionGroup[] = []
  const byName = new Map<string, OptionGroup>()
  for (const o of options) {
    let g = byName.get(o.group)
    if (!g) {
      g = { name: o.group, options: [] }
      byName.set(o.group, g)
      groups.push(g)
    }
    g.options.push(o)
  }
  return groups
}

function StatTable({ labels, row }: { labels: string[]; row: { name: string; value: string }[] }) {
  return (
    <div className="stat-scroll">
      <table className="stat-table">
        <thead>
          <tr>{labels.map((l) => <th key={l}>{l}</th>)}</tr>
        </thead>
        <tbody>
          <tr>{row.map((c) => <td key={c.name}>{c.value || '—'}</td>)}</tr>
        </tbody>
      </table>
    </div>
  )
}

function WeaponTables({ weapons }: { weapons: DatasheetDetail['weapons'] }) {
  return (
    <div className="detail-section">
      <h4>Weapons</h4>
      {weapons.map((w) => (
        <div className="stat-scroll" key={w.name}>
          <table className="stat-table">
            <thead>
              <tr>
                <th>{w.name}</th>
                {w.characteristics.map((c) => <th key={c.name}>{c.name}</th>)}
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>{w.ranged ? 'Ranged' : 'Melee'}</td>
                {w.characteristics.map((c) => <td key={c.name}>{c.value || '—'}</td>)}
              </tr>
            </tbody>
          </table>
        </div>
      ))}
    </div>
  )
}
