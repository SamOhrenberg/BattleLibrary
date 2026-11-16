import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { GAME_SYSTEMS, systemName } from '../lib/gameSystems'
import { createList, deleteList, duplicateList, getList, getLists, type ArmyList, type ArmyListSummary } from '../lib/lists'
import { useTour } from '../onboarding/TourContext'
import { useToast } from '../ui/Toast'
import { OverflowMenu } from '../ui/OverflowMenu'
import { ListBuilder } from './ListBuilder'
import './builder.css'

type View = 'index' | 'create' | 'builder'

export function ListsView() {
  const tour = useTour()
  const [view, setView] = useState<View>('index')
  const [lists, setLists] = useState<ArmyListSummary[] | null>(null)
  const [active, setActive] = useState<ArmyList | null>(null)
  const toast = useToast()
  const autoOffered = useRef(false)

  useEffect(() => {
    getLists()
      .then(setLists)
      .catch(() => toast.push('Could not load your lists.', 'error'))
  }, [toast])

  // Offer the tour automatically, once, to a brand-new user with no lists yet.
  useEffect(() => {
    if (!autoOffered.current && lists && lists.length === 0 && !tour.hasRun && !tour.active) {
      autoOffered.current = true
      tour.start()
    }
  }, [lists, tour])

  async function refreshIndex() {
    setView('index')
    setActive(null)
    try {
      setLists(await getLists())
    } catch {
      toast.push('Could not load your lists.', 'error')
    }
  }

  async function onDuplicate(id: string) {
    try {
      await duplicateList(id)
      setLists(await getLists())
    } catch {
      toast.push('Could not duplicate the list.', 'error')
    }
  }

  async function onDelete(id: string) {
    try {
      await deleteList(id)
      setLists(await getLists())
    } catch {
      toast.push('Could not delete the list.', 'error')
    }
  }

  function openCreate() {
    setView('create')
    tour.notify('open-create')
  }

  async function openList(id: string) {
    try {
      setActive(await getList(id))
      setView('builder')
    } catch {
      toast.push('Could not open that list.', 'error')
    }
  }

  function onCreated(list: ArmyList) {
    setActive(list)
    setView('builder')
    tour.notify('list-created')
  }

  return (
    <section className="card">
      <div className="lists-head">
        <h2>Army lists</h2>
        <div className="lists-actions">
          {view !== 'index' && (
            <button type="button" className="btn" onClick={() => void refreshIndex()}>
              All lists
            </button>
          )}
          {view === 'index' && (
            <>
              <button type="button" className="btn btn-primary" data-tour="new-list" onClick={openCreate}>
                New list
              </button>
            </>
          )}
        </div>
      </div>

      {view === 'index' && (
        <ListIndex
          lists={lists}
          onOpen={(id) => void openList(id)}
          onNew={openCreate}
          onTour={tour.start}
          tourLabel={tour.hasRun ? 'Replay the tour' : 'Take the tour'}
          onDuplicate={(id) => void onDuplicate(id)}
          onDelete={(id) => void onDelete(id)}
        />
      )}

      {view === 'create' && <CreateForm onCreated={onCreated} onCancel={() => void refreshIndex()} />}

      {view === 'builder' && active && (
        <ListBuilder initial={active} onBack={() => void refreshIndex()} onDeleted={() => void refreshIndex()} />
      )}
    </section>
  )
}

// Lists grouped by army (faction), filtered by game system when more than one is in use.
// Group open/closed state is remembered per browser.
const COLLAPSE_KEY = 'battlelibrary.lists.collapsed'
const ALL_SYSTEMS = ''

interface ArmyGroup {
  key: string
  faction: string
  system: string
  lists: ArmyListSummary[]
}

// "Chaos - Chaos Daemons" reads as "Chaos Daemons" once grouped under that faction.
const shortFaction = (name: string) => name.split(' - ').pop() || name

function groupLists(lists: ArmyListSummary[]): ArmyGroup[] {
  const groups = new Map<string, ArmyGroup>()
  for (const l of lists) {
    const faction = l.factionName || 'No faction'
    const key = `${l.gameSystemKey}\u0000${faction}`
    let group = groups.get(key)
    if (!group) {
      group = { key, faction, system: systemName(l.gameSystemKey), lists: [] }
      groups.set(key, group)
    }
    group.lists.push(l)
  }
  return [...groups.values()].sort((a, b) => a.faction.localeCompare(b.faction) || a.system.localeCompare(b.system))
}

function loadCollapsed(): Set<string> {
  try {
    const raw = localStorage.getItem(COLLAPSE_KEY)
    if (raw) return new Set<string>(JSON.parse(raw) as string[])
  } catch {
    /* ignore unavailable or malformed storage */
  }
  return new Set<string>()
}

function ListIndex({
  lists,
  onOpen,
  onNew,
  onTour,
  tourLabel,
  onDuplicate,
  onDelete,
}: {
  lists: ArmyListSummary[] | null
  onOpen: (id: string) => void
  onNew: () => void
  onTour: () => void
  tourLabel: string
  onDuplicate: (id: string) => void
  onDelete: (id: string) => void
}) {
  const [collapsed, setCollapsed] = useState<Set<string>>(loadCollapsed)
  const [system, setSystem] = useState(ALL_SYSTEMS)
  const [query, setQuery] = useState('')

  function toggle(key: string) {
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      try {
        localStorage.setItem(COLLAPSE_KEY, JSON.stringify([...next]))
      } catch {
        /* persistence is best-effort */
      }
      return next
    })
  }

  const systems = useMemo(
    () =>
      [...new Set((lists ?? []).map((l) => l.gameSystemKey))]
        .map((key) => ({ key, name: systemName(key) }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [lists],
  )
  const multiSystem = systems.length > 1
  const term = query.trim().toLowerCase()

  const visible = useMemo(
    () =>
      (lists ?? []).filter(
        (l) =>
          (system === ALL_SYSTEMS || l.gameSystemKey === system) &&
          (!term || l.name.toLowerCase().includes(term) || l.factionName.toLowerCase().includes(term)),
      ),
    [lists, system, term],
  )
  const groups = useMemo(() => groupLists(visible), [visible])

  if (lists === null) return <p className="hint">Loading…</p>
  if (lists.length === 0) {
    return (
      <div className="empty-note">
        <p>No lists yet. Write up your first army list.</p>
        <button type="button" className="btn btn-primary" onClick={onNew}>New list</button>
        <p className="tour-link">
          <button type="button" className="link-button" onClick={onTour}>{tourLabel}</button>
        </p>
      </div>
    )
  }

  return (
    <div className="list-groups">
      {(multiSystem || lists.length > 5) && (
        <div className="list-filters">
          {lists.length > 5 && (
            <input
              className="list-search"
              type="search"
              enterKeyHint="search"
              aria-label="Search lists"
              placeholder="Search lists"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          )}
          {multiSystem && (
            <select aria-label="Game system" className="list-system" value={system} onChange={(e) => setSystem(e.target.value)}>
              <option value={ALL_SYSTEMS}>All game systems</option>
              {systems.map((s) => (
                <option key={s.key} value={s.key}>{s.name}</option>
              ))}
            </select>
          )}
        </div>
      )}

      {groups.length === 0 && <p className="hint">No lists match.</p>}

      {groups.map((group) => {
        const open = !collapsed.has(group.key) || !!term
        return (
          <section className="list-group army-group" key={group.key}>
            <button
              type="button"
              className="group-head army-head"
              aria-expanded={open}
              onClick={() => toggle(group.key)}
            >
              <span className={`group-caret${open ? ' open' : ''}`} aria-hidden="true">▸</span>
              <span className="group-title">
                {group.faction}
                {multiSystem && system === ALL_SYSTEMS && <span className="group-system">{group.system}</span>}
              </span>
              <span className="group-count">{group.lists.length}</span>
            </button>

            {open && (
              <ul className="list-index">
                {group.lists.map((l) => (
                  <ListRow
                    key={l.id}
                    list={l}
                    onOpen={onOpen}
                    onDuplicate={onDuplicate}
                    onDelete={onDelete}
                  />
                ))}
              </ul>
            )}
          </section>
        )
      })}

      <p className="tour-link">
        <button type="button" className="link-button" onClick={onTour}>{tourLabel}</button>
      </p>
    </div>
  )
}

function ListRow({
  list: l,
  onOpen,
  onDuplicate,
  onDelete,
}: {
  list: ArmyListSummary
  onOpen: (id: string) => void
  onDuplicate: (id: string) => void
  onDelete: (id: string) => void
}) {
  return (
    <li className="list-card" onClick={() => onOpen(l.id)}>
      <div className="lc-main">
        <div className="lc-top">
          <span className="li-name">{l.name}</span>
          {(l.total > 0 || l.pointsLimit != null) && (
            <span className="li-points">
              {l.total}
              {l.pointsLimit != null ? ` / ${l.pointsLimit}` : ''} pts
            </span>
          )}
        </div>
        <div className="li-meta">
          {l.factionName ? `${shortFaction(l.factionName)} · ` : ''}
          {l.entryCount} {l.entryCount === 1 ? 'unit' : 'units'}
        </div>
      </div>
      <OverflowMenu
        label={`Actions for ${l.name}`}
        items={[
          { label: 'Duplicate', onSelect: () => onDuplicate(l.id) },
          {
            label: 'Delete',
            danger: true,
            onSelect: () => {
              if (confirm(`Delete "${l.name}"? This cannot be undone.`)) onDelete(l.id)
            },
          },
        ]}
      />
    </li>
  )
}

function CreateForm({ onCreated, onCancel }: { onCreated: (list: ArmyList) => void; onCancel: () => void }) {
  const tour = useTour()
  const [name, setName] = useState('')
  const [systemKey, setSystemKey] = useState<string>(GAME_SYSTEMS[0].key)
  const [faction, setFaction] = useState('')
  const [detachment, setDetachment] = useState('')
  const [limit, setLimit] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const parsed = parseInt(limit, 10)
      const list = await createList({
        name,
        gameSystemKey: systemKey,
        factionName: faction,
        detachment,
        pointsLimit: Number.isNaN(parsed) ? null : parsed,
      })
      onCreated(list)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the list.')
      setBusy(false)
    }
  }

  return (
    <form className="create-form" onSubmit={onSubmit}>
      <label data-tour="create-name">
        List name
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} placeholder="Untitled list" autoFocus />
      </label>

      <div className="create-grid" data-tour="create-faction">
        <label>
          Game system
          <select value={systemKey} onChange={(e) => setSystemKey(e.target.value)}>
            {GAME_SYSTEMS.map((s) => (
              <option key={s.key} value={s.key}>{s.name}</option>
            ))}
          </select>
        </label>
        <label>
          Faction (optional)
          <input value={faction} onChange={(e) => setFaction(e.target.value)} maxLength={200} />
        </label>
      </div>

      <div className="create-grid" data-tour="create-limit">
        <label>
          Detachment (optional)
          <input value={detachment} onChange={(e) => setDetachment(e.target.value)} maxLength={200} />
        </label>
        <label>
          Points limit (optional)
          <input
            inputMode="numeric"
            value={limit}
            placeholder="e.g. 2000"
            onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))}
          />
        </label>
      </div>

      {error && <p className="builder-error">{error}</p>}

      <div className="create-actions">
        <button type="submit" className="btn btn-primary" data-tour="create-submit" disabled={busy}>
          {busy ? 'Creating…' : 'Create list'}
        </button>
        <button type="button" className="btn" onClick={() => { tour.stop(false); onCancel() }}>
          Cancel
        </button>
      </div>
    </form>
  )
}
