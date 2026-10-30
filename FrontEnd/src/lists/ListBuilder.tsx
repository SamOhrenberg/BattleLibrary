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
  setModelCount,
  setWarlord,
  updateEntry,
  updateList,
  BATTLE_SIZES,
  type ArmyList,
  type ListEntry,
} from '../lib/lists'
import { validateList, isCharacter, BATTLE_SIZE_RULES } from './validation'
import { groupByRole } from './roles'
import { isLegends, isCrucible } from './tiers'
import { useTour } from '../onboarding/TourContext'
import { UnitDetailModal } from './UnitDetailModal'
import { ListAssistant } from './ListAssistant'
import { RuleText } from './RuleText'
import { listToText } from './exportList'
import { CompositionBar } from './CompositionBar'
import { PlayView } from './PlayView'

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
  // Role groups collapsed in the catalogue panel.
  const [catCollapsed, setCatCollapsed] = useState<Set<string>>(new Set())
  const [detailId, setDetailId] = useState<string | null>(null)
  const [editEntry, setEditEntry] = useState<ListEntry | null>(null)
  const [showAssistant, setShowAssistant] = useState(false)
  const [copied, setCopied] = useState(false)
  const [showPlay, setShowPlay] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const exportRef = useRef<HTMLDivElement>(null)

  // Close the export menu on an outside click or Escape.
  useEffect(() => {
    if (!exportOpen) return
    const onDown = (e: MouseEvent) => {
      if (exportRef.current && !exportRef.current.contains(e.target as Node)) setExportOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setExportOpen(false)
    window.addEventListener('mousedown', onDown)
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('mousedown', onDown)
      window.removeEventListener('keydown', onKey)
    }
  }, [exportOpen])
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

  const catalogueGroups = useMemo(() => groupByRole(filtered, (d) => d.keywords), [filtered])
  const allCollapsed = catalogueGroups.length > 0 && catalogueGroups.every((g) => catCollapsed.has(g.role))
  const toggleAllGroups = () =>
    setCatCollapsed(allCollapsed ? new Set() : new Set(catalogueGroups.map((g) => g.role)))

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

  async function add(datasheetId: string, optionIds: string[] = [], models?: number) {
    await run(() => addEntry(list.id, datasheetId, optionIds, models))
    if (!addedOnce.current) {
      addedOnce.current = true
      notify('entry-added')
    }
  }

  async function copyText() {
    try {
      await navigator.clipboard.writeText(listToText(list, pointless))
      setCopied(true)
      setTimeout(() => setCopied(false), 1800)
    } catch {
      setError('Could not copy to the clipboard.')
    }
  }

  function downloadText() {
    const safe = list.name.replace(/[^\w.-]+/g, '_').replace(/^_+|_+$/g, '') || 'army-list'
    const blob = new Blob([listToText(list, pointless)], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${safe}.txt`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }

  // Copies an entry: re-adds the same datasheet, quantity, and wargear options.
  // Warlord/enhancement are intentionally not copied (each is unique).
  function duplicateEntry(e: ListEntry) {
    void run(() => addEntry(list.id, e.datasheetId, e.selectedOptions.map((o) => o.id), e.modelCount))
  }

  // Adds a datasheet and sets the new entry as Warlord in one step.
  async function addAsWarlord(datasheetId: string) {
    setBusy(true)
    setError(null)
    try {
      const before = new Set(list.entries.map((e) => e.id))
      const added = await addEntry(list.id, datasheetId, [])
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
  // A unit whose rules force the Warlord slot locks it: it can't be cleared and
  // no other unit can take the slot while it is in the list.
  const hasForcedWarlord = useMemo(() => list.entries.some((e) => e.forcedWarlord), [list.entries])

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
                <div className="pts">{list.entries.length}</div>
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
            <div className="export-menu" ref={exportRef}>
              <button
                type="button"
                className="btn"
                onClick={() => setExportOpen((v) => !v)}
                disabled={list.entries.length === 0}
                aria-haspopup="menu"
                aria-expanded={exportOpen}
              >
                Export ▾
              </button>
              {exportOpen && (
                <div className="export-dropdown" role="menu">
                  <button
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      void copyText()
                      setExportOpen(false)
                    }}
                  >
                    {copied ? 'Copied ✓' : 'Copy to clipboard'}
                  </button>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      downloadText()
                      setExportOpen(false)
                    }}
                  >
                    Download .txt file
                  </button>
                </div>
              )}
            </div>
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

      {!pointless && list.entries.length > 0 && (
        <CompositionBar list={list} hasDetachments={hasDetachments} />
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
            <div className="panel-head-actions">
              <input
                className="catalogue-search"
                placeholder="Search units…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              {catalogueGroups.length > 1 && (
                <button type="button" className="collapse-all" onClick={toggleAllGroups}>
                  {allCollapsed ? 'Expand all' : 'Collapse all'}
                </button>
              )}
            </div>
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
              catalogueGroups.map((group) => (
                <details
                  className="role-group"
                  key={group.role}
                  open={!catCollapsed.has(group.role)}
                  onToggle={(e) => {
                    const open = (e.currentTarget as HTMLDetailsElement).open
                    setCatCollapsed((prev) => {
                      if (open === !prev.has(group.role)) return prev
                      const next = new Set(prev)
                      if (open) next.delete(group.role)
                      else next.add(group.role)
                      return next
                    })
                  }}
                >
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
            <div className="panel-head-actions">
              <span className="li-points">{list.entries.length} entries</span>
              {list.entries.length > 0 && (
                <button type="button" className="play-btn" onClick={() => setShowPlay(true)}>
                  <span aria-hidden="true">▶</span> Play view
                </button>
              )}
            </div>
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
                        {e.modelCount > 1 ? `${e.modelCount} models · ` : ''}
                        {e.points + e.optionsPoints} pts
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
                    {((!pointless && isCharacter(e)) || e.enhancementName) && (
                      <div className="entry-badges">
                        {!pointless && isCharacter(e) && (
                          <button
                            type="button"
                            className={`warlord-btn${e.isWarlord ? ' on' : ''}${e.forcedWarlord ? ' locked' : ''}`}
                            onClick={() => void run(() => setWarlord(list.id, e.id, !e.isWarlord))}
                            disabled={busy || e.forcedWarlord || (!e.isWarlord && hasForcedWarlord)}
                            title={
                              e.forcedWarlord
                                ? "This unit's rules require it to be your Warlord."
                                : hasForcedWarlord
                                  ? 'Another unit must be your Warlord.'
                                  : undefined
                            }
                          >
                            ★ {e.isWarlord ? 'Warlord' : 'Make Warlord'}
                          </button>
                        )}
                        {e.enhancementName && (
                          <span className="badge-enh">{e.enhancementName} (+{e.enhancementPoints})</span>
                        )}
                      </div>
                    )}
                    <button type="button" className="entry-edit-link" onClick={() => setEditEntry(e)}>
                      Edit…
                    </button>
                  </div>
                  {!pointless && <span className="line-total">{e.lineTotal}</span>}
                  <button
                    type="button"
                    className="icon-btn dup-btn"
                    onClick={() => duplicateEntry(e)}
                    disabled={busy}
                    aria-label="Duplicate"
                    title="Duplicate"
                  >
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                      <rect x="9" y="9" width="11" height="11" rx="2" />
                      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                    </svg>
                  </button>
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
          onAdd={(optionIds, models) => {
            void add(detailId, optionIds, models)
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
          initialModels={editEntry.modelCount}
          submitLabel="Save changes"
          busy={busy}
          onSizeChange={(m) => void runEntry(editEntry.id, () => setModelCount(list.id, editEntry.id, m))}
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
            void run(() => updateEntry(list.id, editEntry.id, optionIds))
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
          onAdd={(datasheetId) => void add(datasheetId)}
          onAddWarlord={(datasheetId) => void addAsWarlord(datasheetId)}
          onGoCatalogue={() => {
            setTab('catalogue')
            setShowAssistant(false)
          }}
          onClose={() => setShowAssistant(false)}
        />
      )}

      {showPlay && <PlayView list={list} onClose={() => setShowPlay(false)} />}
    </section>
  )
}
