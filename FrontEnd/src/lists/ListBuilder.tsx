import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../lib/api'
import { getDatasheets, type CatalogueDatasheet, type CatalogueDetachment } from '../lib/catalogue'
import {
  addEntry,
  deleteList,
  removeEntry,
  setBattleSize,
  setDetachments,
  setEnhancement,
  setWarlord,
  updateEntry,
  updateList,
  BATTLE_SIZES,
  type ArmyList,
  type ListEntry,
} from '../lib/lists'
import { validateList, BATTLE_SIZE_RULES } from './validation'
import { groupByRole } from './roles'
import { isLegends, isCrucible } from './tiers'
import { useTour } from '../onboarding/TourContext'
import { UnitDetailModal } from './UnitDetailModal'
import { ListAssistant } from './ListAssistant'
import { RuleText } from './RuleText'

interface Props {
  initial: ArmyList
  onBack: () => void
  onDeleted: () => void
}

export function ListBuilder({ initial, onBack, onDeleted }: Props) {
  const { notify } = useTour()
  const [list, setList] = useState<ArmyList>(initial)
  const [datasheets, setDatasheets] = useState<CatalogueDatasheet[]>([])
  const [equipment, setEquipment] = useState<string[]>([])
  const [detachments, setFactionDetachments] = useState<CatalogueDetachment[]>([])
  const [search, setSearch] = useState('')
  // Legends is extra, non-standard content, so hide it by default; Crucible is
  // current, so show it. Toggles below let either be flipped.
  const [showLegends, setShowLegends] = useState(false)
  const [showCrucible, setShowCrucible] = useState(true)
  const [tab, setTab] = useState<'catalogue' | 'list'>('catalogue')
  const [detailId, setDetailId] = useState<string | null>(null)
  const [editEntry, setEditEntry] = useState<ListEntry | null>(null)
  const [showAssistant, setShowAssistant] = useState(false)
  const [name, setName] = useState(initial.name)
  const [limit, setLimit] = useState(initial.pointsLimit?.toString() ?? '')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Fire the 'entry-added' tour milestone only on the first add.
  const addedOnce = useRef(initial.entries.length > 0)

  useEffect(() => {
    let active = true
    getDatasheets(list.factionId)
      .then((d) => {
        if (!active) return
        setDatasheets(d.datasheets)
        setEquipment(d.faction.equipment ?? [])
        setFactionDetachments(d.faction.detachments ?? [])
      })
      .catch(() => active && setError('Could not load the faction catalogue.'))
    return () => {
      active = false
    }
  }, [list.factionId])

  // A point-less system (e.g. Kill Team 2024): operatives carry no points, so
  // show operative counts instead of points and drop the limit controls.
  const pointless = datasheets.length > 0 && datasheets.every((d) => d.points == null)
  const unitWord = pointless ? 'operatives' : 'units'

  const legendsCount = useMemo(() => datasheets.filter((d) => isLegends(d.name)).length, [datasheets])
  const crucibleCount = useMemo(
    () => datasheets.filter((d) => isCrucible(d.name, d.keywords)).length,
    [datasheets],
  )

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return datasheets.filter((d) => {
      if (q && !d.name.toLowerCase().includes(q)) return false
      if (!showLegends && isLegends(d.name)) return false
      if (!showCrucible && isCrucible(d.name, d.keywords)) return false
      return true
    })
  }, [datasheets, search, showLegends, showCrucible])

  async function run(action: () => Promise<ArmyList>) {
    setBusy(true)
    setError(null)
    try {
      setList(await action())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  // Applies an entry change and keeps the open edit modal in sync.
  async function runEntry(entryId: string, action: () => Promise<ArmyList>) {
    setBusy(true)
    setError(null)
    try {
      const next = await action()
      setList(next)
      setEditEntry(next.entries.find((x) => x.id === entryId) ?? null)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  async function add(datasheetId: string, optionIds: string[] = []) {
    await run(() => addEntry(list.id, datasheetId, 1, optionIds))
    if (!addedOnce.current) {
      addedOnce.current = true
      notify('entry-added')
    }
  }

  async function changeQty(entryId: string, quantity: number) {
    if (quantity < 1) return
    await run(() => updateEntry(list.id, entryId, quantity))
  }

  // Adds a datasheet and sets the new entry as Warlord in one step.
  async function addAsWarlord(datasheetId: string) {
    setBusy(true)
    setError(null)
    try {
      const before = new Set(list.entries.map((e) => e.id))
      const added = await addEntry(list.id, datasheetId, 1, [])
      const fresh = added.entries.find((e) => !before.has(e.id))
      setList(fresh ? await setWarlord(list.id, fresh.id, true) : added)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    setBusy(true)
    setError(null)
    try {
      const parsed = limit.trim() === '' ? null : Number(limit)
      setList(await updateList(list.id, name.trim() || list.name, Number.isNaN(parsed!) ? null : parsed))
      setSaved(true)
      setTimeout(() => setSaved(false), 1800)
      notify('saved')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the list.')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!confirm('Delete this list? This cannot be undone.')) return
    setBusy(true)
    try {
      await deleteList(list.id)
      onDeleted()
    } catch {
      setError('Could not delete the list.')
      setBusy(false)
    }
  }

  const over = list.isOverLimit
  const hasDetachments = detachments.length > 0
  const chosenIds = useMemo(() => new Set(list.detachments.map((d) => d.id)), [list.detachments])
  const rules = BATTLE_SIZE_RULES[list.battleSize ?? '']
  const dpUsed = list.detachments.reduce((n, d) => n + d.dp, 0)
  const issues = useMemo(() => validateList(list, hasDetachments, pointless), [list, hasDetachments, pointless])

  const chosenDetachments = useMemo(
    () => detachments.filter((d) => chosenIds.has(d.id)),
    [detachments, chosenIds],
  )

  // Enhancements offered by any chosen detachment.
  const availableEnhancements = useMemo(
    () => chosenDetachments.flatMap((d) => d.enhancements),
    [chosenDetachments],
  )

  const changeBattleSize = (b: string | null) => run(() => setBattleSize(list.id, b))
  const toggleDetachment = (id: string) =>
    run(() => setDetachments(list.id, chosenIds.has(id) ? [...chosenIds].filter((x) => x !== id) : [...chosenIds, id]))

  // Enhancements still free to take (plus the entry's own current one).
  function enhancementsFor(e: ListEntry) {
    const usedByOthers = new Set(
      list.entries.filter((x) => x.id !== e.id && x.enhancementId).map((x) => x.enhancementId),
    )
    return availableEnhancements.filter((en) => !usedByOthers.has(en.id))
  }

  return (
    <section className="card">
      <div className="builder-bar">
        <div className="builder-id">
          <button type="button" className="tour-skip" onClick={onBack} style={{ textAlign: 'left' }}>
            ← All lists
          </button>
          <input
            className="builder-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={save}
            aria-label="List name"
            maxLength={120}
          />
          <span className="builder-faction">{list.factionName}</span>
        </div>

        <div className="builder-meta">
          <div className="points-readout" data-tour="list-total">
            {pointless ? (
              <>
                <div className="pts">{list.entries.reduce((n, e) => n + e.quantity, 0)}</div>
                <label className="limit-label">operatives</label>
              </>
            ) : (
              <>
                <div className={`pts${over ? ' over' : ''}`}>{list.total}</div>
                <label className="limit-label">
                  {list.battleSize ? (
                    <>{list.battleSize} · {list.pointsLimit}</>
                  ) : (
                    <>
                      limit{' '}
                      <input
                        className="limit-edit"
                        inputMode="numeric"
                        value={limit}
                        placeholder="none"
                        onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))}
                        onBlur={save}
                      />
                    </>
                  )}
                </label>
                {over && <div className="over-badge">Over limit</div>}
              </>
            )}
          </div>
          <div className="lists-actions">
            <button
              type="button"
              className={`btn btn-primary${saved ? ' is-saved' : ''}`}
              data-tour="save-list"
              onClick={save}
              disabled={busy || saved}
            >
              {busy ? 'Saving…' : saved ? 'Saved ✓' : 'Save'}
            </button>
            <button type="button" className="btn btn-danger" onClick={remove} disabled={busy}>
              Delete
            </button>
          </div>
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

      {!pointless && (
        <div className="list-setup">
          <label className="setup-field battle-size">
            <span>Battle size</span>
            <select value={list.battleSize ?? ''} onChange={(e) => changeBattleSize(e.target.value || null)} disabled={busy}>
              <option value="">Custom</option>
              {BATTLE_SIZES.map((b) => (
                <option key={b} value={b}>{b} · {BATTLE_SIZE_RULES[b].cap} pts</option>
              ))}
            </select>
          </label>
          {hasDetachments && (
            <div className="setup-field">
              <span>
                Detachments{rules ? ` · ${dpUsed}/${rules.dp} DP` : dpUsed > 0 ? ` · ${dpUsed} DP` : ''}
              </span>
              <div className="detachment-chips">
                {detachments.map((d) => {
                  const selected = chosenIds.has(d.id)
                  // One detachment is always allowed; past that, adding can't exceed the budget.
                  const overBudget =
                    !selected && !!rules && list.detachments.length >= 1 && dpUsed + d.dp > rules.dp
                  const hasPreview = d.rules.length > 0 || d.enhancements.length > 0
                  return (
                    <div className="det-chip-wrap" key={d.id}>
                      <button
                        type="button"
                        className={`det-chip${selected ? ' on' : ''}`}
                        onClick={() => toggleDetachment(d.id)}
                        disabled={busy || overBudget}
                        title={overBudget ? 'Not enough Detachment Points left' : undefined}
                      >
                        {d.name} <span className="det-dp">{d.dp} DP</span>
                      </button>
                      {hasPreview && (
                        <div className="det-popover" role="tooltip">
                          <div className="dp-title">{d.name} · {d.dp} DP</div>
                          {d.rules.length > 0 && (
                            <dl className="rules-list">
                              {d.rules.map((r) => (
                                <div className="rule" key={r.name}>
                                  <dt>{r.name}</dt>
                                  {r.text && <dd><RuleText text={r.text} /></dd>}
                                </div>
                              ))}
                            </dl>
                          )}
                          {d.enhancements.length > 0 && (
                            <div className="dp-enh">
                              <span className="dp-sub">Enhancements</span>
                              <ul>
                                {d.enhancements.map((e) => (
                                  <li key={e.id}>
                                    {e.name} <span className="dp-pts">+{e.points}</span>
                                  </li>
                                ))}
                              </ul>
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          )}
        </div>
      )}

      {!pointless && chosenDetachments.some((d) => d.rules.length > 0) && (
        <div className="detachment-rules">
          {chosenDetachments
            .filter((d) => d.rules.length > 0)
            .map((d) => (
              <details className="det-rules" key={d.id} open>
                <summary>{d.name} — detachment rule{d.rules.length > 1 ? 's' : ''}</summary>
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
        </div>
      )}

      {!pointless && (
        <div className={`legality ${issues.length === 0 ? 'ok' : 'bad'}`}>
          {issues.length === 0 ? (
            <span className="legality-head">✓ Battle ready{list.detachments.length > 0 ? ` · ${list.detachments.map((d) => d.name).join(', ')}` : ''}</span>
          ) : (
            <>
              <div className="legality-top">
                <span className="legality-head">{issues.length} to fix before this list is legal</span>
                <button type="button" className="btn btn-sm" onClick={() => setShowAssistant(true)}>
                  Help me fix
                </button>
              </div>
              <ul>{issues.map((i, idx) => <li key={idx}>{i.message}</li>)}</ul>
            </>
          )}
        </div>
      )}

      {equipment.length > 0 && (
        <div className="faction-equipment">
          <span className="fe-label">Kill team equipment</span>
          <div className="fe-chips">
            {equipment.map((e) => (
              <span className="fe-chip" key={e}>{e}</span>
            ))}
          </div>
        </div>
      )}

      <div className="builder-tabs" role="tablist" aria-label="Builder panels">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'catalogue'}
          className={tab === 'catalogue' ? 'active' : ''}
          onClick={() => setTab('catalogue')}
        >
          Catalogue
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'list'}
          className={tab === 'list' ? 'active' : ''}
          onClick={() => setTab('list')}
        >
          Your list{list.entries.length > 0 ? ` · ${list.entries.length}` : ''}
        </button>
      </div>

      <div className={`builder-grid tab-${tab}`}>
        <div className="panel panel-catalogue" data-tour="catalogue">
          <div className="panel-head">
            <h3>Catalogue</h3>
            <input
              className="catalogue-search"
              placeholder="Search units…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <p className="panel-hint">Tap a unit's name for its full datasheet.</p>
          {(legendsCount > 0 || crucibleCount > 0) && (
            <div className="tier-toggles">
              <span className="tier-label">Show</span>
              {legendsCount > 0 && (
                <label className="tier-toggle">
                  <input type="checkbox" checked={showLegends} onChange={(e) => setShowLegends(e.target.checked)} />
                  Legends <span className="tier-count">{legendsCount}</span>
                </label>
              )}
              {crucibleCount > 0 && (
                <label className="tier-toggle">
                  <input type="checkbox" checked={showCrucible} onChange={(e) => setShowCrucible(e.target.checked)} />
                  Crucible <span className="tier-count">{crucibleCount}</span>
                </label>
              )}
            </div>
          )}
          <div className="scroll">
            {filtered.length === 0 ? (
              <p className="empty">No units match.</p>
            ) : (
              groupByRole(filtered, (d) => d.keywords).map((group) => (
                <details className="role-group" key={group.role} open>
                  <summary className="role-summary">
                    {group.role} <span className="role-count">{group.items.length}</span>
                  </summary>
                  {group.items.map((d) => (
                    <div className="unit-row" key={d.id}>
                      <button type="button" className="unit-name" onClick={() => setDetailId(d.id)}>
                        {d.name}
                      </button>
                      {!pointless && <span className="unit-pts">{d.points != null ? `${d.points}` : '—'}</span>}
                      <button
                        type="button"
                        className="add-btn"
                        data-tour={d.id === filtered[0].id ? 'catalogue-add' : undefined}
                        onClick={() => void add(d.id)}
                        disabled={busy}
                      >
                        Add
                      </button>
                    </div>
                  ))}
                </details>
              ))
            )}
          </div>
        </div>

        <div className="panel panel-list">
          <div className="panel-head">
            <h3>Your list</h3>
            <span className="li-points">{list.entries.length} entries</span>
          </div>
          <div className="scroll">
            {list.entries.length === 0 ? (
              <p className="empty">No {unitWord} yet. Add some from the catalogue.</p>
            ) : (
              groupByRole(list.entries, (e) => e.keywords).map((group) => (
                <details className="role-group" key={group.role} open>
                  <summary className="role-summary">
                    {group.role} <span className="role-count">{group.items.length}</span>
                  </summary>
                  {group.items.map((e) => (
                <div className="entry-row" key={e.id}>
                  <div style={{ minWidth: 0, flex: 1 }}>
                    <button type="button" className="entry-name entry-edit" onClick={() => setEditEntry(e)} title="View / edit options">
                      {e.datasheetName}
                    </button>
                    {!pointless && (
                      <div className="entry-each">
                        {e.points + e.optionsPoints} pts each
                        {e.optionsPoints > 0 ? ` (${e.points} + ${e.optionsPoints})` : ''}
                      </div>
                    )}
                    {e.selectedOptions.length > 0 && (
                      <ul className="entry-opts">
                        {e.selectedOptions.map((o) => (
                          <li key={o.id}>
                            {o.name}
                            {o.points > 0 ? ` (+${o.points})` : ''}
                          </li>
                        ))}
                      </ul>
                    )}
                    {(e.isWarlord || e.enhancementName) && (
                      <div className="entry-badges">
                        {e.isWarlord && <span className="badge-warlord">★ Warlord</span>}
                        {e.enhancementName && (
                          <span className="badge-enh">{e.enhancementName} (+{e.enhancementPoints})</span>
                        )}
                      </div>
                    )}
                    <button type="button" className="entry-edit-link" onClick={() => setEditEntry(e)}>
                      Edit…
                    </button>
                  </div>
                  <div className="qty">
                    <button type="button" onClick={() => void changeQty(e.id, e.quantity - 1)} disabled={busy || e.quantity <= 1} aria-label="Decrease">−</button>
                    <span>{e.quantity}</span>
                    <button type="button" onClick={() => void changeQty(e.id, e.quantity + 1)} disabled={busy} aria-label="Increase">+</button>
                  </div>
                  {!pointless && <span className="line-total">{e.lineTotal}</span>}
                  <button type="button" className="icon-btn" onClick={() => void run(() => removeEntry(list.id, e.id))} disabled={busy} aria-label="Remove">×</button>
                </div>
                  ))}
                </details>
              ))
            )}
          </div>
        </div>
      </div>

      {detailId && (
        <UnitDetailModal
          datasheetId={detailId}
          onAdd={(optionIds) => {
            void add(detailId, optionIds)
            setDetailId(null)
          }}
          onClose={() => setDetailId(null)}
        />
      )}

      {editEntry && (
        <UnitDetailModal
          key={editEntry.id}
          datasheetId={editEntry.datasheetId}
          initialSelected={editEntry.selectedOptions.map((o) => o.id)}
          submitLabel="Save changes"
          busy={busy}
          warlord={
            pointless
              ? undefined
              : {
                  value: editEntry.isWarlord,
                  onChange: (v) => void runEntry(editEntry.id, () => setWarlord(list.id, editEntry.id, v)),
                }
          }
          enhancement={
            pointless || list.detachments.length === 0
              ? undefined
              : {
                  value: editEntry.enhancementId,
                  options: enhancementsFor(editEntry),
                  onChange: (id) => void runEntry(editEntry.id, () => setEnhancement(list.id, editEntry.id, id)),
                }
          }
          onAdd={(optionIds) => {
            void run(() => updateEntry(list.id, editEntry.id, editEntry.quantity, optionIds))
            setEditEntry(null)
          }}
          onClose={() => setEditEntry(null)}
        />
      )}

      {showAssistant && (
        <ListAssistant
          list={list}
          issues={issues}
          datasheets={datasheets}
          detachments={detachments}
          chosenDetachmentIds={chosenIds}
          busy={busy}
          onToggleDetachment={toggleDetachment}
          onSetWarlord={(entryId, value) => void run(() => setWarlord(list.id, entryId, value))}
          onRemoveEnhancement={(entryId) => void run(() => setEnhancement(list.id, entryId, null))}
          onRemoveEntry={(entryId) => void run(() => removeEntry(list.id, entryId))}
          onChangeQty={(entryId, quantity) => void changeQty(entryId, quantity)}
          onAdd={(datasheetId) => void add(datasheetId)}
          onAddWarlord={(datasheetId) => void addAsWarlord(datasheetId)}
          onGoCatalogue={() => {
            setTab('catalogue')
            setShowAssistant(false)
          }}
          onClose={() => setShowAssistant(false)}
        />
      )}
    </section>
  )
}
