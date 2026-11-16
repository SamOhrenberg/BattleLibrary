import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../lib/api'
import { GAME_SYSTEMS, systemName } from '../lib/gameSystems'
import {
  addEntry,
  addForce,
  attachEntry,
  deleteList,
  entryFields,
  removeEntry,
  removeForce,
  renameForce,
  reorderEntries,
  setWarlord,
  updateEntry,
  updateList,
  type ArmyList,
  type EntryFields,
  type ListEntry,
} from '../lib/lists'
import { createPreset, deletePreset, getPresets, type UnitPreset } from '../lib/presets'
import { useTour } from '../onboarding/TourContext'
import { useToast } from '../ui/Toast'
import { OverflowMenu } from '../ui/OverflowMenu'
import { listToText } from './exportList'
import { PlayView } from './PlayView'
import { UnitForm, type UnitFormResult } from './UnitForm'
import { entryTree } from './entryTree'

interface Props {
  initial: ArmyList
  onBack: () => void
  onDeleted: () => void
}

interface Details {
  name: string
  gameSystemKey: string
  factionName: string
  detachment: string
  pointsLimit: string
  notes: string
}

const detailsOf = (l: ArmyList): Details => ({
  name: l.name,
  gameSystemKey: l.gameSystemKey,
  factionName: l.factionName,
  detachment: l.detachment ?? '',
  pointsLimit: l.pointsLimit?.toString() ?? '',
  notes: l.notes ?? '',
})

// The unit form: adding into a section, or editing an entry.
type FormState = { mode: 'add'; forceId: string } | { mode: 'edit'; entry: ListEntry }

// A freeform list editor: units are typed in by hand, like a paper list.
export function ListBuilder({ initial, onBack, onDeleted }: Props) {
  const { notify } = useTour()
  const toast = useToast()
  const [list, setList] = useState<ArmyList>(initial)
  const [details, setDetails] = useState<Details>(() => detailsOf(initial))
  const [busy, setBusy] = useState(false)
  const [form, setForm] = useState<FormState | null>(null)
  const [presets, setPresets] = useState<UnitPreset[]>([])
  const [showPlay, setShowPlay] = useState(false)
  const [showDetails, setShowDetails] = useState(false)
  const [highlightId, setHighlightId] = useState<string | null>(null)
  const [renamingForce, setRenamingForce] = useState<{ id: string; name: string } | null>(null)
  const lastSaved = useRef(JSON.stringify(detailsOf(initial)))

  useEffect(() => {
    getPresets(list.gameSystemKey)
      .then(setPresets)
      .catch(() => setPresets([]))
  }, [list.gameSystemKey])

  const tree = useMemo(() => entryTree(list), [list])
  const over = list.pointsLimit != null && list.total > list.pointsLimit
  const multiSection = list.forces.length > 1

  async function run(action: () => Promise<ArmyList>): Promise<ArmyList | null> {
    setBusy(true)
    try {
      const next = await action()
      setList(next)
      return next
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Something went wrong.', 'error')
      return null
    } finally {
      setBusy(false)
    }
  }

  // Saves the list-level fields when any changed since the last save.
  async function saveDetails(next: Details = details) {
    const key = JSON.stringify(next)
    if (key === lastSaved.current) return
    lastSaved.current = key
    const limit = parseInt(next.pointsLimit, 10)
    const saved = await run(() =>
      updateList(list.id, {
        name: next.name,
        gameSystemKey: next.gameSystemKey,
        factionName: next.factionName,
        detachment: next.detachment,
        pointsLimit: Number.isNaN(limit) ? null : limit,
        notes: next.notes,
      }),
    )
    if (saved && saved.name !== next.name) {
      // A blank name falls back to the server default.
      setDetails((d) => ({ ...d, name: saved.name }))
    }
  }

  function flashEntry(id: string) {
    setHighlightId(id)
    requestAnimationFrame(() =>
      document.querySelector(`[data-entry-id="${id}"]`)?.scrollIntoView({ block: 'center', behavior: 'smooth' }),
    )
    window.setTimeout(() => setHighlightId((cur) => (cur === id ? null : cur)), 1600)
  }

  // Adds an entry and returns its id, found by diffing the entry ids.
  async function addAndFind(fields: EntryFields): Promise<{ list: ArmyList; id: string } | null> {
    const before = new Set(list.entries.map((e) => e.id))
    const next = await run(() => addEntry(list.id, fields))
    const id = next?.entries.find((e) => !before.has(e.id))?.id
    return next && id ? { list: next, id } : null
  }

  async function submitForm(result: UnitFormResult, addAnother: boolean): Promise<boolean> {
    if (!form) return false
    let id: string
    let current: ArmyList
    if (form.mode === 'add') {
      const added = await addAndFind(result.fields)
      if (!added) return false
      id = added.id
      current = added.list
      notify('entry-added')
    } else {
      const next = await run(() => updateEntry(list.id, form.entry.id, result.fields))
      if (!next) return false
      id = form.entry.id
      current = next
    }

    const entry = current.entries.find((e) => e.id === id)
    if (entry && entry.isWarlord !== result.warlord) {
      await run(() => setWarlord(list.id, id, result.warlord))
    }
    if (entry && entry.attachedToEntryId !== result.attachTo) {
      await run(() => attachEntry(list.id, id, result.attachTo))
    }

    if (!addAnother) setForm(null)
    flashEntry(id)
    return true
  }

  async function duplicate(e: ListEntry) {
    const added = await addAndFind({ ...entryFields(e) })
    if (added) flashEntry(added.id)
  }

  async function removeWithUndo(e: ListEntry) {
    const next = await run(() => removeEntry(list.id, e.id))
    if (!next) return
    setForm(null)
    toast.push(`Removed ${e.name}.`, 'info', {
      label: 'Undo',
      onClick: () =>
        void (async () => {
          const added = await addAndFind(entryFields(e))
          if (added) flashEntry(added.id)
        })(),
    })
  }

  // Moves a top-level item (with anything attached to it) up or down within its
  // section, then saves the full order.
  async function move(forceId: string, index: number, delta: -1 | 1) {
    const sections = tree.sections.map((s) => ({ ...s, items: [...s.items] }))
    const section = sections.find((s) => s.force.id === forceId)
    if (!section) return
    const target = index + delta
    if (target < 0 || target >= section.items.length) return
    ;[section.items[index], section.items[target]] = [section.items[target], section.items[index]]
    const ids = sections.flatMap((s) => s.items.flatMap((item) => [item.entry.id, ...item.attached.map((a) => a.id)]))
    await run(() => reorderEntries(list.id, ids))
  }

  async function savePreset(name: string, fields: EntryFields) {
    try {
      const preset = await createPreset({
        gameSystemKey: list.gameSystemKey,
        name,
        unitName: fields.name,
        points: fields.points,
        modelCount: fields.modelCount,
        wargear: fields.wargear,
        enhancement: fields.enhancement,
        notes: fields.notes,
      })
      setPresets((prev) =>
        [...prev.filter((p) => p.id !== preset.id), preset].sort((a, b) => a.name.localeCompare(b.name)),
      )
      toast.push(`Saved preset "${preset.name}".`, 'success')
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not save the preset.', 'error')
    }
  }

  async function removePreset(id: string) {
    try {
      await deletePreset(id)
      setPresets((prev) => prev.filter((p) => p.id !== id))
    } catch {
      toast.push('Could not delete the preset.', 'error')
    }
  }

  async function addSection() {
    const name = prompt('Section name (e.g. Allies)')
    if (name === null) return
    await run(() => addForce(list.id, name))
  }

  async function commitRenameSection() {
    if (!renamingForce) return
    const { id, name } = renamingForce
    setRenamingForce(null)
    if (name.trim() && name !== list.forces.find((f) => f.id === id)?.name) {
      await run(() => renameForce(list.id, id, name))
    }
  }

  async function dropSection(forceId: string, name: string) {
    const count = list.entries.filter((e) => e.forceId === forceId).length
    const msg = count > 0 ? `Remove "${name}" and its ${count} ${count === 1 ? 'unit' : 'units'}?` : `Remove "${name}"?`
    if (confirm(msg)) await run(() => removeForce(list.id, forceId))
  }

  async function copyText() {
    try {
      await navigator.clipboard.writeText(listToText(list))
      toast.push('Copied the list to your clipboard.', 'success')
    } catch {
      toast.push('Could not copy to the clipboard.', 'error')
    }
  }

  function downloadText() {
    const blob = new Blob([listToText(list)], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${list.name.replace(/[\\/:*?"<>|]/g, '_').trim() || 'list'}.txt`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }

  async function remove() {
    if (!confirm(`Delete "${list.name}"? This cannot be undone.`)) return
    try {
      await deleteList(list.id)
      onDeleted()
    } catch {
      toast.push('Could not delete the list.', 'error')
    }
  }

  const renderEntry = (e: ListEntry, variant: 'normal' | 'leader' | 'embedded') => {
    const meta = [
      e.modelCount != null ? `${e.modelCount} ${e.modelCount === 1 ? 'model' : 'models'}` : null,
      e.points != null ? `${e.points} pts` : null,
    ].filter(Boolean)
    const wargear = (e.wargear ?? '').split('\n').map((w) => w.trim()).filter(Boolean)
    return (
      <div
        className={`entry-row${variant === 'embedded' ? ' entry-embedded' : ''}${variant === 'leader' ? ' entry-leader' : ''}${highlightId === e.id ? ' flash' : ''}`}
        key={e.id}
        data-entry-id={e.id}
      >
        <div className="entry-main">
          <button type="button" className="entry-name entry-edit" onClick={() => setForm({ mode: 'edit', entry: e })}>
            {variant === 'embedded' && <span className="lead-marker" aria-hidden="true">↳ </span>}
            {e.name}
          </button>
          {meta.length > 0 && <div className="entry-each">{meta.join(' · ')}</div>}
          {wargear.length > 0 && (
            <ul className="entry-opts">
              {wargear.map((w, i) => (
                <li key={i}>{w}</li>
              ))}
            </ul>
          )}
          {(e.isWarlord || e.enhancement) && (
            <div className="entry-badges">
              {e.isWarlord && <span className="badge-warlord">★ Warlord</span>}
              {e.enhancement && <span className="badge-enh">{e.enhancement}</span>}
            </div>
          )}
          {e.notes && <div className="entry-note-text">{e.notes}</div>}
        </div>
        <button
          type="button"
          className="icon-btn dup-btn"
          onClick={() => void duplicate(e)}
          disabled={busy}
          aria-label={`Duplicate ${e.name}`}
          title="Duplicate"
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <rect x="9" y="9" width="11" height="11" rx="2" />
            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
          </svg>
        </button>
        <button
          type="button"
          className="icon-btn remove-btn"
          onClick={() => void removeWithUndo(e)}
          disabled={busy}
          aria-label={`Remove ${e.name}`}
        >
          ×
        </button>
      </div>
    )
  }

  const renderSection = (forceId: string) => {
    const section = tree.sections.find((s) => s.force.id === forceId)
    const items = section?.items ?? []
    if (items.length === 0) {
      return <p className="empty">No units here yet.</p>
    }
    return (
      <div className="custom-list">
        {items.map((item, i) => {
          const pts = [item.entry, ...item.attached].reduce((n, x) => n + (x.points ?? 0), 0)
          return (
            <div className="custom-item" key={item.entry.id}>
              <div className="reorder">
                <button
                  type="button"
                  className="reorder-btn"
                  onClick={() => void move(forceId, i, -1)}
                  disabled={busy || i === 0}
                  aria-label="Move up"
                >
                  ↑
                </button>
                <button
                  type="button"
                  className="reorder-btn"
                  onClick={() => void move(forceId, i, 1)}
                  disabled={busy || i === items.length - 1}
                  aria-label="Move down"
                >
                  ↓
                </button>
              </div>
              <div className="custom-body">
                {item.attached.length > 0 ? (
                  <div className="attached-unit">
                    <div className="attached-head">
                      <span className="attached-label">Attached unit</span>
                      {pts > 0 && <span className="attached-pts">{pts} pts</span>}
                    </div>
                    {item.attached.map((a) => renderEntry(a, 'leader'))}
                    {renderEntry(item.entry, 'embedded')}
                  </div>
                ) : (
                  renderEntry(item.entry, 'normal')
                )}
              </div>
            </div>
          )
        })}
      </div>
    )
  }

  const subtitle = [systemName(list.gameSystemKey), list.factionName, list.detachment].filter(Boolean).join(' · ')

  return (
    <section className="card builder-card">
      <div className="builder-bar">
        <div className="builder-id">
          <button type="button" className="tour-skip" onClick={onBack} style={{ textAlign: 'left' }}>
            ← All lists
          </button>
          <input
            className="builder-name"
            value={details.name}
            onChange={(e) => setDetails((d) => ({ ...d, name: e.target.value }))}
            onBlur={() => void saveDetails()}
            aria-label="List name"
            maxLength={120}
          />
          <span className="builder-faction">{subtitle}</span>
        </div>

        <div className="builder-meta">
          <div className="points-readout" data-tour="list-total">
            <div className={`pts${over ? ' over' : ''}`}>{list.total}</div>
            <span className="limit-label">{list.pointsLimit != null ? `of ${list.pointsLimit} pts` : 'pts'}</span>
          </div>
          <div className="lists-actions">
            <button type="button" className="btn" onClick={() => setShowDetails((v) => !v)} aria-expanded={showDetails}>
              Details
            </button>
            <OverflowMenu
              label="More list actions"
              items={[
                { label: 'Play view', onSelect: () => setShowPlay(true) },
                { label: 'Copy as text', onSelect: () => void copyText() },
                { label: 'Download .txt', onSelect: downloadText },
                { label: 'Add section', onSelect: () => void addSection() },
                { label: 'Delete list', danger: true, onSelect: () => void remove() },
              ]}
            />
          </div>
        </div>
      </div>

      {showDetails && (
        <div className="list-setup list-details">
          <div className="create-grid">
            <label className="setup-field">
              <span>Game system</span>
              <select
                value={details.gameSystemKey}
                onChange={(e) => {
                  const next = { ...details, gameSystemKey: e.target.value }
                  setDetails(next)
                  void saveDetails(next)
                }}
              >
                {GAME_SYSTEMS.map((s) => (
                  <option key={s.key} value={s.key}>
                    {s.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="setup-field">
              <span>Points limit</span>
              <input
                inputMode="numeric"
                value={details.pointsLimit}
                placeholder="None"
                onChange={(e) => setDetails((d) => ({ ...d, pointsLimit: e.target.value.replace(/[^0-9]/g, '') }))}
                onBlur={() => void saveDetails()}
              />
            </label>
            <label className="setup-field">
              <span>Faction</span>
              <input
                value={details.factionName}
                maxLength={200}
                onChange={(e) => setDetails((d) => ({ ...d, factionName: e.target.value }))}
                onBlur={() => void saveDetails()}
              />
            </label>
            <label className="setup-field">
              <span>Detachment</span>
              <input
                value={details.detachment}
                maxLength={200}
                onChange={(e) => setDetails((d) => ({ ...d, detachment: e.target.value }))}
                onBlur={() => void saveDetails()}
              />
            </label>
          </div>
          <label className="setup-field">
            <span>Notes</span>
            <textarea
              rows={3}
              value={details.notes}
              maxLength={4000}
              onChange={(e) => setDetails((d) => ({ ...d, notes: e.target.value }))}
              onBlur={() => void saveDetails()}
            />
          </label>
        </div>
      )}

      <div className="panel panel-list">
        <div className="panel-head">
          <h3>Units</h3>
          <div className="panel-head-actions">
            <span className="li-points">
              {list.entries.length} {list.entries.length === 1 ? 'unit' : 'units'}
            </span>
            {list.entries.length > 0 && (
              <button type="button" className="play-btn" onClick={() => setShowPlay(true)}>
                <span aria-hidden="true">▶</span> Play view
              </button>
            )}
          </div>
        </div>
        <div className="unit-sections">
          {list.entries.length === 0 && !multiSection ? (
            <div className="empty empty-cta">
              <p>No units yet. Write in what you are bringing.</p>
            </div>
          ) : multiSection ? (
            list.forces.map((f) => (
              <section className="force-section" key={f.id}>
                <div className="force-section-head">
                  {renamingForce?.id === f.id ? (
                    <input
                      className="force-rename"
                      autoFocus
                      value={renamingForce.name}
                      maxLength={200}
                      aria-label="Section name"
                      onChange={(e) => setRenamingForce({ id: f.id, name: e.target.value })}
                      onBlur={() => void commitRenameSection()}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') e.currentTarget.blur()
                        else if (e.key === 'Escape') setRenamingForce(null)
                      }}
                    />
                  ) : (
                    <button
                      type="button"
                      className="force-name force-name-btn"
                      onClick={() => setRenamingForce({ id: f.id, name: f.name })}
                      title="Rename section"
                    >
                      {f.name}
                    </button>
                  )}
                  <span className="force-count">{list.entries.filter((e) => e.forceId === f.id).length}</span>
                  <button
                    type="button"
                    className="entry-edit-link"
                    onClick={() => setForm({ mode: 'add', forceId: f.id })}
                    disabled={busy}
                  >
                    + Add
                  </button>
                  <button
                    type="button"
                    className="entry-edit-link force-remove"
                    onClick={() => void dropSection(f.id, f.name)}
                    disabled={busy}
                  >
                    Remove
                  </button>
                </div>
                {renderSection(f.id)}
              </section>
            ))
          ) : (
            renderSection(list.forces[0]?.id ?? '')
          )}
        </div>
        <div className="add-unit-bar">
          <button
            type="button"
            className="btn btn-primary add-unit-btn"
            data-tour="add-unit"
            onClick={() => {
              setForm({ mode: 'add', forceId: list.forces[0]?.id ?? '' })
              notify('unit-form-opened')
            }}
            disabled={busy}
          >
            + Add unit
          </button>
        </div>
      </div>

      {form && (
        <UnitForm
          key={form.mode === 'edit' ? form.entry.id : 'add'}
          list={list}
          entry={form.mode === 'edit' ? form.entry : undefined}
          defaultForceId={form.mode === 'add' ? form.forceId : form.entry.forceId}
          presets={presets}
          busy={busy}
          onSubmit={submitForm}
          onSavePreset={(name, fields) => void savePreset(name, fields)}
          onDeletePreset={(id) => void removePreset(id)}
          onDelete={form.mode === 'edit' ? () => void removeWithUndo(form.entry) : undefined}
          onClose={() => setForm(null)}
        />
      )}

      {showPlay && <PlayView list={list} onClose={() => setShowPlay(false)} />}
    </section>
  )
}
