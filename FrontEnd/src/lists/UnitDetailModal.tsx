import { useEffect, useMemo, useState } from 'react'
import { getDatasheet, type DatasheetDetail, type DatasheetOption } from '../lib/catalogue'
import type { LoadoutPreset } from '../lib/presets'
import { RuleText } from './RuleText'

interface EnhancementOption {
  id: string
  name: string
  points: number
  text?: string
}

interface Props {
  datasheetId: string
  initialSelected?: string[]
  // The entry's current model count (edit mode), to preselect the size.
  initialModels?: number
  submitLabel?: string
  busy?: boolean
  // Edit-only: assign an enhancement, applied immediately. (Warlord is set from
  // the list overview, not here.)
  enhancement?: { value: string | null; options: EnhancementOption[]; onChange: (id: string | null) => void }
  // Edit-only: change the unit size, applied immediately.
  onSizeChange?: (models: number) => void
  // Saved loadouts for this datasheet, plus callbacks to save/delete them.
  presets?: LoadoutPreset[]
  onSavePreset?: (name: string, optionIds: string[], models?: number) => void
  onDeletePreset?: (id: string) => void
  onAdd: (selectedOptionIds: string[], models?: number) => void
  onClose: () => void
}

export function UnitDetailModal({
  datasheetId,
  initialSelected,
  initialModels,
  submitLabel,
  busy,
  enhancement,
  onSizeChange,
  presets,
  onSavePreset,
  onDeletePreset,
  onAdd,
  onClose,
}: Props) {
  const [detail, setDetail] = useState<DatasheetDetail | null>(null)
  const [error, setError] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(() => new Set(initialSelected ?? []))
  const [models, setModels] = useState<number | undefined>(initialModels)
  const [presetName, setPresetName] = useState('')

  useEffect(() => {
    let active = true
    setDetail(null)
    setError(false)
    setSelected(new Set(initialSelected ?? []))
    getDatasheet(datasheetId)
      .then((d) => {
        if (!active) return
        setDetail(d)
        // Default the size to the entry's current count, else the smallest.
        setModels(initialModels ?? d.sizes[0]?.models)
      })
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

  const sizePoints = detail?.sizes.find((s) => s.models === models)?.points ?? detail?.points ?? 0
  const subtotal = sizePoints + optionsTotal

  function changeSize(m: number) {
    setModels(m)
    onSizeChange?.(m)
  }

  // Applies a saved loadout's options + size to the current selection.
  function applyPreset(p: LoadoutPreset) {
    setSelected(new Set(p.selectedOptions.map((o) => o.id)))
    if (p.modelCount !== models) changeSize(p.modelCount)
  }

  function saveCurrentPreset() {
    const name = presetName.trim()
    if (!name || !onSavePreset) return
    onSavePreset(name, [...selected], models)
    setPresetName('')
  }

  // Presets are worth showing only when there's something to vary.
  const canConfigure = (detail?.options.length ?? 0) > 0 || (detail?.sizes.length ?? 0) > 1
  const showPresets = (presets?.length ?? 0) > 0 || (!!onSavePreset && canConfigure)

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="sheet-grip" aria-hidden="true" />
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

            {detail.sizes.length > 1 && (
              <div className="detail-section">
                <h4>Unit size</h4>
                <div className="size-pick">
                  {detail.sizes.map((s) => (
                    <label key={s.models} className={`size-opt${models === s.models ? ' on' : ''}`}>
                      <input
                        type="radio"
                        name="unit-size"
                        checked={models === s.models}
                        onChange={() => changeSize(s.models)}
                        disabled={busy}
                      />
                      {s.models} models · {s.points} pts
                    </label>
                  ))}
                </div>
              </div>
            )}

            {detail.profiles.map((p) => (
              <StatTable key={p.name} labels={p.characteristics.map((c) => c.name)} row={p.characteristics} />
            ))}

            {detail.weapons.length > 0 && <WeaponTables weapons={detail.weapons} />}

            {detail.modelGroups.length > 0 && (
              <div className="detail-section">
                <h4>Models</h4>
                <ul className="model-groups">
                  {detail.modelGroups.map((m) => (
                    <li key={m.label}>
                      <span className="mg-label">{m.label}</span>
                      {m.weapons.length > 0 && <span className="mg-weapons">{m.weapons.join(', ')}</span>}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {detail.abilities.length > 0 && (
              <div className="detail-section">
                <h4>Abilities</h4>
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
                      <div className="option-wrap" key={o.id}>
                        <label className="option-row">
                          <input
                            type="checkbox"
                            checked={selected.has(o.id)}
                            onChange={() => toggle(o.id)}
                          />
                          <span className="opt-name">{o.name}</span>
                          <span className="opt-pts">{o.points > 0 ? `+${o.points}` : 'free'}</span>
                        </label>
                        {o.weapons?.length > 0 && (
                          <div className="opt-weapon">
                            {o.weapons.map((w) => (
                              <div className="owl" key={`${w.name}-${w.ranged}`}>
                                <span className="owl-type">{w.ranged ? 'Ranged' : 'Melee'}</span>
                                {w.characteristics
                                  .filter((c) => c.value)
                                  .map((c) => (
                                    <span className="owl-stat" key={c.name}>
                                      {c.name}
                                      <b>{c.value}</b>
                                    </span>
                                  ))}
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            )}

            {showPresets && (
              <div className="detail-section">
                <h4>Saved loadouts</h4>
                {(presets?.length ?? 0) > 0 ? (
                  <div className="preset-list">
                    {presets!.map((p) => (
                      <div className="preset-chip" key={p.id}>
                        <button type="button" className="preset-apply" onClick={() => applyPreset(p)}>
                          <span className="preset-name">{p.name}</span>
                          {detail.points != null && <span className="preset-pts">{p.total} pts</span>}
                        </button>
                        {onDeletePreset && (
                          <button
                            type="button"
                            className="preset-del"
                            aria-label={`Delete preset ${p.name}`}
                            title="Delete preset"
                            onClick={() => onDeletePreset(p.id)}
                          >
                            ×
                          </button>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="panel-hint">No saved loadouts for this unit yet.</p>
                )}
                {onSavePreset && (
                  <div className="preset-save">
                    <input
                      className="preset-input"
                      value={presetName}
                      maxLength={80}
                      placeholder="Save current as…"
                      aria-label="Preset name"
                      onChange={(e) => setPresetName(e.target.value)}
                      onKeyDown={(e) => e.key === 'Enter' && saveCurrentPreset()}
                    />
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={saveCurrentPreset}
                      disabled={presetName.trim().length === 0}
                    >
                      Save
                    </button>
                  </div>
                )}
              </div>
            )}

            {(() => {
              const kw = new Set(detail.keywords.map((k) => k.toLowerCase()))
              const isChar = kw.has('character')
              const isEpic = kw.has('epic hero')
              return (
                <>
                  {enhancement && isChar && !isEpic && (
                    <div className="detail-section">
                      <h4>Enhancement</h4>
                      <select
                        className="enh-select"
                        value={enhancement.value ?? ''}
                        onChange={(e) => enhancement.onChange(e.target.value || null)}
                        disabled={busy}
                      >
                        <option value="">No enhancement</option>
                        {enhancement.options.map((o) => (
                          <option key={o.id} value={o.id}>{o.name} (+{o.points})</option>
                        ))}
                      </select>
                      {(() => {
                        const chosen = enhancement.options.find((o) => o.id === enhancement.value)
                        return chosen?.text ? <p className="enh-text"><RuleText text={chosen.text} /></p> : null
                      })()}
                    </div>
                  )}
                </>
              )
            })()}

            <div className="modal-actions">
              {detail.points != null && (
                <span className="modal-subtotal">
                  Subtotal <b>{subtotal}</b> pts
                </span>
              )}
              <button type="button" className="btn btn-primary" onClick={() => onAdd([...selected], models)}>
                {submitLabel ?? 'Add to list'}
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
          <tr>{row.map((c) => <td key={c.name}>{c.value || '-'}</td>)}</tr>
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
                {w.characteristics.map((c) => <td key={c.name}>{c.value || '-'}</td>)}
              </tr>
            </tbody>
          </table>
        </div>
      ))}
    </div>
  )
}
