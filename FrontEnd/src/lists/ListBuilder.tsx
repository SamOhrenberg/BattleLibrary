import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../lib/api'
import { getDatasheets, type CatalogueDatasheet } from '../lib/catalogue'
import {
  addEntry,
  deleteList,
  removeEntry,
  updateEntry,
  updateList,
  type ArmyList,
} from '../lib/lists'
import { useTour } from '../onboarding/TourContext'
import { UnitDetailModal } from './UnitDetailModal'

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
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<'catalogue' | 'list'>('catalogue')
  const [detailId, setDetailId] = useState<string | null>(null)
  const [name, setName] = useState(initial.name)
  const [limit, setLimit] = useState(initial.pointsLimit?.toString() ?? '')
  const [busy, setBusy] = useState(false)
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

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return q ? datasheets.filter((d) => d.name.toLowerCase().includes(q)) : datasheets
  }, [datasheets, search])

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

  async function save() {
    const parsed = limit.trim() === '' ? null : Number(limit)
    await run(() => updateList(list.id, name.trim() || list.name, Number.isNaN(parsed!) ? null : parsed))
    notify('saved')
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
                  limit{' '}
                  <input
                    className="limit-edit"
                    inputMode="numeric"
                    value={limit}
                    placeholder="none"
                    onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))}
                    onBlur={save}
                  />
                </label>
                {over && <div className="over-badge">Over limit</div>}
              </>
            )}
          </div>
          <div className="lists-actions">
            <button type="button" className="btn btn-primary" data-tour="save-list" onClick={save} disabled={busy}>
              Save
            </button>
            <button type="button" className="btn btn-danger" onClick={remove} disabled={busy}>
              Delete
            </button>
          </div>
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

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
          <div className="scroll">
            {filtered.length === 0 ? (
              <p className="empty">No units match.</p>
            ) : (
              filtered.map((d, i) => (
                <div className="unit-row" key={d.id}>
                  <button type="button" className="unit-name" onClick={() => setDetailId(d.id)}>
                    {d.name}
                  </button>
                  {!pointless && <span className="unit-pts">{d.points != null ? `${d.points}` : '—'}</span>}
                  <button
                    type="button"
                    className="add-btn"
                    data-tour={i === 0 ? 'catalogue-add' : undefined}
                    onClick={() => void add(d.id)}
                    disabled={busy}
                  >
                    Add
                  </button>
                </div>
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
              list.entries.map((e) => (
                <div className="entry-row" key={e.id}>
                  <div style={{ minWidth: 0 }}>
                    <div className="entry-name">{e.datasheetName}</div>
                    {!pointless && (
                      <div className="entry-each">
                        {e.points + e.optionsPoints} pts each
                        {e.optionsPoints > 0 ? ` (${e.points} + ${e.optionsPoints})` : ''}
                      </div>
                    )}
                    {e.selectedOptions.length > 0 && (
                      <div className="entry-opts">
                        {e.selectedOptions.map((o) => o.name).join(', ')}
                      </div>
                    )}
                  </div>
                  <div className="qty">
                    <button type="button" onClick={() => void changeQty(e.id, e.quantity - 1)} disabled={busy || e.quantity <= 1} aria-label="Decrease">−</button>
                    <span>{e.quantity}</span>
                    <button type="button" onClick={() => void changeQty(e.id, e.quantity + 1)} disabled={busy} aria-label="Increase">+</button>
                  </div>
                  {!pointless && <span className="line-total">{e.lineTotal}</span>}
                  <button type="button" className="icon-btn" onClick={() => void run(() => removeEntry(list.id, e.id))} disabled={busy} aria-label="Remove">×</button>
                </div>
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
    </section>
  )
}
