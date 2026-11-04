import { useEffect, useMemo, useState } from 'react'
import { getDatasheet, dynamicPoints, type DatasheetDetail, type DatasheetOption, type ModelVariantGroup } from '../lib/catalogue'
import type { VariantQuantity } from '../lib/lists'
import type { LoadoutPreset } from '../lib/presets'
import { RuleText } from './RuleText'

// Flattens a unit's loadout pools to their variants.
const allVariants = (groups: ModelVariantGroup[]) => groups.flatMap((g) => g.variants)

// Models the pool must hold at a given size: the size minus the fixed models.
const poolTarget = (models: number | undefined, fixed: number, sizes: { models: number }[]) =>
  Math.max(0, (models ?? sizes[0]?.models ?? 0) - fixed)

// Reconciles chosen variant counts to a pool target: clamps each to its cap,
// then tops up the default (first) variant when short, or trims the extras
// first when over. Used on open and on every size change.
function reconcile(groups: ModelVariantGroup[], current: Record<string, number>, target: number): Record<string, number> {
  const variants = allVariants(groups)
  const next: Record<string, number> = {}
  for (const v of variants) next[v.id] = Math.min(v.max, Math.max(0, current[v.id] ?? 0))
  let total = variants.reduce((n, v) => n + next[v.id], 0)
  const def = variants.find((v) => v.isDefault) ?? variants[0]
  if (!def) return next
  if (total < target) {
    next[def.id] = Math.min(def.max, next[def.id] + (target - total))
  } else if (total > target) {
    let over = total - target
    for (let i = variants.length - 1; i >= 0 && over > 0; i--) {
      const take = Math.min(over, next[variants[i].id])
      next[variants[i].id] -= take
      over -= take
    }
  }
  return next
}

// The non-zero variant counts, as the API's variant payload.
const toVariantPayload = (qty: Record<string, number>): VariantQuantity[] =>
  Object.entries(qty)
    .filter(([, n]) => n > 0)
    .map(([id, quantity]) => ({ id, quantity }))

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
  // Current per-variant counts (edit mode), keyed by variant id.
  initialQuantities?: Record<string, number>
  submitLabel?: string
  busy?: boolean
  // Edit-only: assign an enhancement, applied immediately. (Warlord is set from
  // the list overview, not here.)
  enhancement?: { value: string | null; options: EnhancementOption[]; onChange: (id: string | null) => void }
  // Edit-only: change the unit size, applied immediately. Carries the re-filled
  // loadout pool so points stay correct.
  onSizeChange?: (models: number, variants?: VariantQuantity[]) => void
  // Lifts the loaded datasheet up (e.g. so the builder can cache variant info).
  onDetail?: (detail: DatasheetDetail) => void
  // Saved loadouts for this datasheet, plus callbacks to save/delete them.
  presets?: LoadoutPreset[]
  onSavePreset?: (name: string, optionIds: string[], models?: number, variants?: VariantQuantity[]) => void
  onDeletePreset?: (id: string) => void
  onAdd: (selectedOptionIds: string[], models?: number, variants?: VariantQuantity[]) => void
  onClose: () => void
}

export function UnitDetailModal({
  datasheetId,
  initialSelected,
  initialModels,
  initialQuantities,
  submitLabel,
  busy,
  enhancement,
  onSizeChange,
  onDetail,
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
  // Chosen count per loadout variant, keyed by variant id.
  const [variantQty, setVariantQty] = useState<Record<string, number>>({})
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
        onDetail?.(d)
        // Default the size to the entry's current count, else the smallest.
        const size = initialModels ?? d.sizes[0]?.models
        setModels(size)
        // Add mode (no saved selection): pre-select each choice set's default
        // wargear so the unit starts on its datasheet loadout. Edit mode keeps
        // the saved picks seeded before the fetch.
        if (!initialSelected) {
          setSelected(new Set(d.options.filter((o) => o.isDefault).map((o) => o.id)))
        }
        // Seed the loadout pool: restore current counts in edit mode, else
        // auto-fill the default variant up to the pool target.
        const groups = d.variantGroups ?? []
        if (groups.length > 0) {
          const target = poolTarget(size, d.fixedModelCount ?? 0, d.sizes)
          setVariantQty(reconcile(groups, initialQuantities ?? {}, target))
        } else {
          setVariantQty({})
        }
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
  const variantGroups = detail?.variantGroups ?? []
  const hasPool = variantGroups.length > 0
  const variantList = useMemo(() => allVariants(variantGroups), [variantGroups])

  const optionsTotal = useMemo(() => {
    if (!detail) return 0
    return detail.options
      .filter((o) => selected.has(o.id))
      .reduce((sum, o) => sum + o.points, 0)
  }, [detail, selected])

  // The pool size the chosen unit size needs, and how many are assigned so far.
  const target = hasPool ? poolTarget(models, detail?.fixedModelCount ?? 0, detail?.sizes ?? []) : 0
  const assigned = variantList.reduce((n, v) => n + (variantQty[v.id] ?? 0), 0)
  const variantPoints = variantList.reduce((n, v) => n + v.points * (variantQty[v.id] ?? 0), 0)

  // Plain checkbox toggle (unbounded groups): flip membership.
  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  // Multi-select with a cap (groupMax > 1): flip, but never go over the cap.
  function toggleCapped(id: string, g: OptionGroup) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else if (g.options.filter((o) => next.has(o.id)).length < g.max) {
        next.add(id)
      }
      return next
    })
  }

  // Pick-one group (groupMax === 1): select this, clearing the set's others.
  function selectOnly(id: string, g: OptionGroup) {
    setSelected((prev) => {
      const next = new Set(prev)
      for (const o of g.options) next.delete(o.id)
      next.add(id)
      return next
    })
  }

  // Clear every choice in a set (the "None" radio of an optional pick-one group).
  function clearGroup(g: OptionGroup) {
    setSelected((prev) => {
      const next = new Set(prev)
      for (const o of g.options) next.delete(o.id)
      return next
    })
  }

  // Changes a variant's count within [0, its cap] and the remaining pool room.
  function stepVariant(id: string, delta: 1 | -1) {
    setVariantQty((prev) => {
      const variant = variantList.find((v) => v.id === id)
      if (!variant) return prev
      const current = prev[id] ?? 0
      const totalNow = variantList.reduce((n, v) => n + (prev[v.id] ?? 0), 0)
      const room = target - totalNow
      const next = delta === 1 ? current + Math.min(1, Math.max(0, room)) : current - 1
      const clamped = Math.min(variant.max, Math.max(0, next))
      if (clamped === current) return prev
      return { ...prev, [id]: clamped }
    })
  }

  const sizePoints = detail?.sizes.find((s) => s.models === models)?.points ?? detail?.points ?? 0
  // Selection-dependent pricing (Crucible warband weapons), matching the server.
  const dynamicTotal = useMemo(
    () => dynamicPoints(detail?.costRules, [...selected].map((id) => ({ id }))),
    [detail, selected],
  )
  const subtotal = sizePoints + optionsTotal + variantPoints + dynamicTotal

  function changeSize(m: number) {
    setModels(m)
    // Re-fill the pool for the new size before telling the parent, so the size
    // change and its variant counts are applied together.
    let next = variantQty
    if (hasPool) {
      next = reconcile(variantGroups, variantQty, poolTarget(m, detail?.fixedModelCount ?? 0, detail?.sizes ?? []))
      setVariantQty(next)
    }
    onSizeChange?.(m, hasPool ? toVariantPayload(next) : undefined)
  }

  // Applies a saved loadout's options + size + loadout counts to the selection.
  function applyPreset(p: LoadoutPreset) {
    const variantIds = new Set(variantList.map((v) => v.id))
    setSelected(new Set(p.selectedOptions.filter((o) => !variantIds.has(o.id)).map((o) => o.id)))
    if (hasPool) {
      const current: Record<string, number> = {}
      for (const o of p.selectedOptions) if (variantIds.has(o.id)) current[o.id] = o.quantity
      setVariantQty(reconcile(variantGroups, current, poolTarget(p.modelCount, detail?.fixedModelCount ?? 0, detail?.sizes ?? [])))
    }
    if (p.modelCount !== models) changeSize(p.modelCount)
  }

  function saveCurrentPreset() {
    const name = presetName.trim()
    if (!name || !onSavePreset) return
    onSavePreset(name, [...selected], models, hasPool ? toVariantPayload(variantQty) : undefined)
    setPresetName('')
  }

  // Presets are worth showing only when there's something to vary.
  const canConfigure = (detail?.options.length ?? 0) > 0 || (detail?.sizes.length ?? 0) > 1 || hasPool
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
              <details className="detail-section rules-group">
                <summary className="rules-summary">Abilities</summary>
                <dl className="rules-list">
                  {detail.abilities.map((a) => (
                    <div className="rule" key={a.name}>
                      <dt>{a.name}</dt>
                      {a.text && <dd><RuleText text={a.text} rules={detail.rules} /></dd>}
                    </div>
                  ))}
                </dl>
              </details>
            )}

            {detail.rules && detail.rules.length > 0 && (
              <details className="detail-section rules-group">
                <summary className="rules-summary">Rules</summary>
                <dl className="rules-list">
                  {detail.rules.map((a) => (
                    <div className="rule" key={a.name}>
                      <dt>{a.name}</dt>
                      {a.text && <dd><RuleText text={a.text} /></dd>}
                    </div>
                  ))}
                </dl>
              </details>
            )}

            {detail.leaderAttachments && detail.leaderAttachments.length > 0 && (
              <div className="detail-section">
                <h4>Leads</h4>
                {detail.leaderAttachments.map((a) => (
                  <div key={a.label}>
                    {a.label !== 'Leader' && <p>{a.label}:</p>}
                    <ul>
                      {a.targets.map((t) => (
                        <li key={t}>{t}</li>
                      ))}
                    </ul>
                  </div>
                ))}
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

            {hasPool && (
              <div className="detail-section">
                <div className="loadout-head">
                  <h4>Loadouts</h4>
                  <span className={`pool-count${assigned === target ? ' full' : ''}`}>
                    {assigned} / {target} assigned
                  </span>
                </div>
                {variantGroups.map((g) => (
                  <div className="variant-group" key={g.id}>
                    {g.name && <div className="option-group-name">{g.name}</div>}
                    {g.variants.map((v) => {
                      const n = variantQty[v.id] ?? 0
                      const atCap = n >= v.max || assigned >= target
                      return (
                        <div className="variant-row" key={v.id}>
                          <span className="opt-name">{v.name}</span>
                          {v.points > 0 && <span className="opt-pts">+{v.points}</span>}
                          <div className="stepper">
                            <button
                              type="button"
                              className="step-btn"
                              aria-label={`Fewer ${v.name}`}
                              onClick={() => stepVariant(v.id, -1)}
                              disabled={busy || n <= 0}
                            >
                              −
                            </button>
                            <span className="step-n" aria-live="polite">{n}</span>
                            <button
                              type="button"
                              className="step-btn"
                              aria-label={`More ${v.name}`}
                              onClick={() => stepVariant(v.id, 1)}
                              disabled={busy || atCap}
                            >
                              +
                            </button>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                ))}
                {assigned !== target && (
                  <p className="pool-warn">
                    {assigned < target
                      ? `${target - assigned} more to assign for a legal pool.`
                      : `${assigned - target} over the pool size.`}
                  </p>
                )}
              </div>
            )}

            {grouped.length > 0 && (
              <div className="detail-section">
                <h4>Options</h4>
                {grouped.map((section) => (
                  <div className="option-model" key={section.model || '_unit'}>
                    {section.model && <div className="option-model-name">{section.model}</div>}
                    {section.groups.map((g) => {
                      const pickOne = g.max === 1
                      const capped = g.max > 1
                      const atCap = capped && g.options.filter((o) => selected.has(o.id)).length >= g.max
                      const hint = groupHint(g)
                      const radioName = `grp-${section.model || '_unit'}-${g.key}`
                      return (
                      <div className="option-group" key={g.key}>
                        {g.name && (
                          <div className="option-group-name">
                            {g.name}
                            {hint && <span className="option-group-hint">{hint}</span>}
                          </div>
                        )}
                        {pickOne && g.min < 1 && (
                          <div className="option-wrap">
                            <label className="option-row">
                              <input
                                type="radio"
                                name={radioName}
                                checked={!g.options.some((o) => selected.has(o.id))}
                                onChange={() => clearGroup(g)}
                                disabled={busy}
                              />
                              <span className="opt-name opt-none">None</span>
                            </label>
                          </div>
                        )}
                        {g.options.map((o) => (
                          <div className="option-wrap" key={o.id}>
                            <label className="option-row">
                              <input
                                type={pickOne ? 'radio' : 'checkbox'}
                                name={pickOne ? radioName : undefined}
                                checked={selected.has(o.id)}
                                onChange={() =>
                                  pickOne ? selectOnly(o.id, g) : capped ? toggleCapped(o.id, g) : toggle(o.id)
                                }
                                disabled={busy || (atCap && !selected.has(o.id))}
                              />
                              <span className="opt-name">{o.name}</span>
                              {o.isDefault && <span className="opt-default">Default</span>}
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
                      )
                    })}
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
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => onAdd([...selected], models, hasPool ? toVariantPayload(variantQty) : undefined)}
              >
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
  // Stable choice-set id (falls back to the name when absent). Used as the key
  // for single-select enforcement, so same-named groups stay independent.
  key: string
  name: string
  // Selection bounds: max 1 is a "pick one" swap, max 0 means no declared cap.
  min: number
  max: number
  options: DatasheetOption[]
}

// Options grouped by the model they are for, then by choice set, so the sheet
// shows which model each option (e.g. a pistol swap) applies to.
interface ModelSection {
  model: string
  groups: OptionGroup[]
}

function groupOptions(options: DatasheetOption[]): ModelSection[] {
  const sections: ModelSection[] = []
  const byModel = new Map<string, ModelSection>()
  for (const o of options) {
    const modelKey = o.model ?? ''
    let section = byModel.get(modelKey)
    if (!section) {
      section = { model: modelKey, groups: [] }
      byModel.set(modelKey, section)
      sections.push(section)
    }
    const key = o.groupId || o.group
    let g = section.groups.find((x) => x.key === key)
    if (!g) {
      g = { key, name: o.group, min: o.groupMin ?? 0, max: o.groupMax ?? 0, options: [] }
      section.groups.push(g)
    }
    g.options.push(o)
  }
  return sections
}

// The "pick one" / "pick up to N" hint for a choice set, or null when unbounded.
function groupHint(g: OptionGroup): string | null {
  if (g.max === 1) return g.min >= 1 ? 'Pick one' : 'Pick up to one'
  if (g.max > 1) return `Pick up to ${g.max}`
  return null
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
