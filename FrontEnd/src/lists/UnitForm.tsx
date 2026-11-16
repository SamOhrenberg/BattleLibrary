import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { ArmyList, EntryFields, ListEntry } from '../lib/lists'
import type { UnitPreset } from '../lib/presets'

// Values the form edits, as typed. Numbers stay strings until submit.
interface Draft {
  name: string
  models: string
  points: string
  wargear: string
  enhancement: string
  notes: string
  forceId: string
  attachTo: string
  warlord: boolean
}

export interface UnitFormResult {
  fields: EntryFields
  warlord: boolean
  attachTo: string | null
}

interface Props {
  list: ArmyList
  // The entry being edited; absent when adding.
  entry?: ListEntry
  defaultForceId: string
  presets: UnitPreset[]
  busy?: boolean
  onSubmit: (result: UnitFormResult, addAnother: boolean) => Promise<boolean>
  onSavePreset: (name: string, fields: EntryFields) => void
  onDeletePreset: (id: string) => void
  onDelete?: () => void
  onClose: () => void
}

const toText = (n: number | null | undefined) => (n == null ? '' : String(n))
const digits = (s: string) => s.replace(/[^0-9]/g, '')

function toNumber(s: string): number | null {
  const n = parseInt(s, 10)
  return Number.isNaN(n) ? null : n
}

function draftFrom(entry: ListEntry | undefined, forceId: string): Draft {
  return {
    name: entry?.name ?? '',
    models: toText(entry?.modelCount),
    points: toText(entry?.points),
    wargear: entry?.wargear ?? '',
    enhancement: entry?.enhancement ?? '',
    notes: entry?.notes ?? '',
    forceId: entry?.forceId ?? forceId,
    attachTo: entry?.attachedToEntryId ?? '',
    warlord: entry?.isWarlord ?? false,
  }
}

// A bottom sheet for adding or editing one unit. Every field is optional.
export function UnitForm({
  list,
  entry,
  defaultForceId,
  presets,
  busy,
  onSubmit,
  onSavePreset,
  onDeletePreset,
  onDelete,
  onClose,
}: Props) {
  const [draft, setDraft] = useState<Draft>(() => draftFrom(entry, defaultForceId))
  const [presetName, setPresetName] = useState<string | null>(null)
  const nameRef = useRef<HTMLInputElement>(null)
  const editing = entry !== undefined

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }))

  function fields(): EntryFields {
    return {
      name: draft.name,
      points: toNumber(draft.points),
      modelCount: toNumber(draft.models),
      wargear: draft.wargear,
      enhancement: draft.enhancement,
      notes: draft.notes,
      forceId: draft.forceId || null,
    }
  }

  function applyPreset(p: UnitPreset) {
    setDraft((d) => ({
      ...d,
      name: p.unitName,
      models: toText(p.modelCount),
      points: toText(p.points),
      wargear: p.wargear ?? '',
      enhancement: p.enhancement ?? '',
      notes: p.notes ?? '',
    }))
  }

  async function submit(addAnother: boolean) {
    const ok = await onSubmit({ fields: fields(), warlord: draft.warlord, attachTo: draft.attachTo || null }, addAnother)
    if (ok && addAnother) {
      setDraft(draftFrom(undefined, draft.forceId))
      nameRef.current?.focus()
    }
  }

  function onFormSubmit(e: FormEvent) {
    e.preventDefault()
    void submit(false)
  }

  // Attach targets: other top-level units. Attachments are one level deep, so a
  // unit that already has units attached to it can't attach elsewhere.
  const hasAttached = entry !== undefined && list.entries.some((x) => x.attachedToEntryId === entry.id)
  const attachable = hasAttached
    ? []
    : list.entries.filter((e) => e.id !== entry?.id && e.attachedToEntryId === null)

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <form className="modal unit-form" onClick={(e) => e.stopPropagation()} onSubmit={onFormSubmit}>
        <div className="modal-top">
          <div className="sheet-grip" aria-hidden="true" />
          <button type="button" className="sheet-close" aria-label="Close" onClick={onClose}>
            ×
          </button>
          <div className="modal-head">
            <h3>{editing ? 'Edit unit' : 'Add unit'}</h3>
          </div>
        </div>

        {!editing && presets.length > 0 && (
          <div className="uf-presets">
            <span className="uf-label">From a preset</span>
            <div className="uf-chips">
              {presets.map((p) => (
                <span className="uf-chip" key={p.id}>
                  <button type="button" onClick={() => applyPreset(p)}>
                    {p.name}
                  </button>
                  <button
                    type="button"
                    className="uf-chip-x"
                    aria-label={`Delete preset ${p.name}`}
                    onClick={() => {
                      if (confirm(`Delete the preset "${p.name}"?`)) onDeletePreset(p.id)
                    }}
                  >
                    ×
                  </button>
                </span>
              ))}
            </div>
          </div>
        )}

        <div className="uf-fields">
          <label className="uf-wide">
            Unit
            <input
              ref={nameRef}
              value={draft.name}
              maxLength={200}
              placeholder="e.g. Intercessor Squad"
              autoFocus={!editing}
              onChange={(e) => set('name', e.target.value)}
              data-tour="unit-name"
            />
          </label>
          <label>
            Models
            <input inputMode="numeric" value={draft.models} placeholder="-" onChange={(e) => set('models', digits(e.target.value))} />
          </label>
          <label>
            Points
            <input inputMode="numeric" value={draft.points} placeholder="-" onChange={(e) => set('points', digits(e.target.value))} />
          </label>
          <label className="uf-wide">
            Wargear
            <textarea
              rows={3}
              value={draft.wargear}
              maxLength={2000}
              placeholder="One item per line"
              onChange={(e) => set('wargear', e.target.value)}
            />
          </label>
          <label className="uf-wide">
            Enhancement
            <input value={draft.enhancement} maxLength={200} onChange={(e) => set('enhancement', e.target.value)} />
          </label>
          <label className="uf-wide">
            Notes
            <textarea rows={2} value={draft.notes} maxLength={2000} onChange={(e) => set('notes', e.target.value)} />
          </label>
          {list.forces.length > 1 && (
            <label>
              Section
              <select value={draft.forceId} onChange={(e) => set('forceId', e.target.value)}>
                {list.forces.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          {attachable.length > 0 && (
            <label>
              Attached to
              <select value={draft.attachTo} onChange={(e) => set('attachTo', e.target.value)}>
                <option value="">Nothing</option>
                {attachable.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="uf-check uf-wide">
            <input type="checkbox" checked={draft.warlord} onChange={(e) => set('warlord', e.target.checked)} />
            Warlord
          </label>
        </div>

        <div className="uf-preset-save">
          {presetName === null ? (
            <button type="button" className="link-button" onClick={() => setPresetName(draft.name)}>
              Save as preset
            </button>
          ) : (
            <span className="uf-preset-row">
              <input
                value={presetName}
                maxLength={80}
                placeholder="Preset name"
                aria-label="Preset name"
                autoFocus
                onChange={(e) => setPresetName(e.target.value)}
              />
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => {
                  onSavePreset(presetName, fields())
                  setPresetName(null)
                }}
              >
                Save preset
              </button>
              <button type="button" className="btn btn-sm ghost" onClick={() => setPresetName(null)}>
                Cancel
              </button>
            </span>
          )}
        </div>

        <div className="modal-actions">
          {editing && onDelete ? (
            <button type="button" className="btn btn-danger" onClick={onDelete} disabled={busy}>
              Remove
            </button>
          ) : (
            <button type="button" className="btn" onClick={() => void submit(true)} disabled={busy}>
              Add another
            </button>
          )}
          <button type="submit" className="btn btn-primary" disabled={busy} data-tour="unit-save">
            {editing ? 'Save' : 'Add'}
          </button>
        </div>
      </form>
    </div>
  )
}
