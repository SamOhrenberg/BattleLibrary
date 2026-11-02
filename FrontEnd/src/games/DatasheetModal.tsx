import { useEffect, useState } from 'react'
import { getDatasheet, type DatasheetDetail, type DatasheetProfile, type DatasheetWeapon } from '../lib/catalogue'
import { RuleText } from '../lists/RuleText'

// Read-only datasheet lookup for play: the stat line, weapons and abilities of a
// single unit, fetched on open. No editing: the list builder handles that.
export function DatasheetModal({
  datasheetId,
  title,
  onClose,
}: {
  datasheetId: string
  title?: string
  onClose: () => void
}) {
  const [detail, setDetail] = useState<DatasheetDetail | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    let active = true
    setDetail(null)
    setError(false)
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

  const ranged = detail?.weapons.filter((w) => w.ranged) ?? []
  const melee = detail?.weapons.filter((w) => !w.ranged) ?? []

  return (
    <div className="ds-backdrop" onClick={onClose}>
      <div className="ds-modal" onClick={(e) => e.stopPropagation()}>
        <div className="ds-head">
          <h3>{detail?.name ?? title ?? 'Datasheet'}</h3>
          <button className="icon-btn" aria-label="Close" onClick={onClose}>×</button>
        </div>

        {error ? (
          <p className="builder-error">Could not load this unit.</p>
        ) : !detail ? (
          <p className="hint">Loading…</p>
        ) : (
          <div className="ds-body">
            {detail.keywords.length > 0 && (
              <div className="ds-kws">
                {detail.keywords.map((k) => (
                  <span className="ds-kw" key={k}>{k}</span>
                ))}
              </div>
            )}

            {detail.profiles.map((p) => (
              <ProfileTable key={p.name} profile={p} />
            ))}

            <WeaponTable title="Ranged weapons" weapons={ranged} />
            <WeaponTable title="Melee weapons" weapons={melee} />

            {detail.abilities.length > 0 && (
              <div className="ds-section">
                <h4>Abilities</h4>
                <dl className="ds-rules">
                  {detail.abilities.map((a) => (
                    <div className="ds-rule" key={a.name}>
                      <dt>{a.name}</dt>
                      {a.text && <dd><RuleText text={a.text} /></dd>}
                    </div>
                  ))}
                </dl>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function ProfileTable({ profile }: { profile: DatasheetProfile }) {
  return (
    <div className="ds-scroll">
      <table className="ds-table">
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
    <div className="ds-scroll">
      <table className="ds-table">
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
