import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { getCatalogueSystems, getFactions, type CatalogueFaction, type CatalogueSystem } from '../lib/catalogue'
import { BATTLE_SIZES, createList, getList, getLists, type ArmyList, type ArmyListSummary } from '../lib/lists'
import { BATTLE_SIZE_RULES } from './validation'
import { useTour } from '../onboarding/TourContext'
import { ListBuilder } from './ListBuilder'
import './builder.css'

type View = 'index' | 'create' | 'builder'

export function ListsView() {
  const tour = useTour()
  const [view, setView] = useState<View>('index')
  const [lists, setLists] = useState<ArmyListSummary[] | null>(null)
  const [active, setActive] = useState<ArmyList | null>(null)
  const [error, setError] = useState<string | null>(null)
  const autoOffered = useRef(false)

  useEffect(() => {
    getLists()
      .then(setLists)
      .catch(() => setError('Could not load your lists.'))
  }, [])

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
      setError('Could not load your lists.')
    }
  }

  function openCreate() {
    setError(null)
    setView('create')
    tour.notify('open-create')
  }

  async function openList(id: string) {
    setError(null)
    try {
      setActive(await getList(id))
      setView('builder')
    } catch {
      setError('Could not open that list.')
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
              <button type="button" className="btn" onClick={tour.start}>
                {tour.hasRun ? 'Replay tour' : 'Take the tour'}
              </button>
              <button type="button" className="btn btn-primary" data-tour="new-list" onClick={openCreate}>
                New list
              </button>
            </>
          )}
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

      {view === 'index' && (
        <ListIndex lists={lists} onOpen={(id) => void openList(id)} onNew={openCreate} />
      )}

      {view === 'create' && <CreateForm onCreated={onCreated} onCancel={() => void refreshIndex()} />}

      {view === 'builder' && active && (
        <ListBuilder initial={active} onBack={() => void refreshIndex()} onDeleted={() => void refreshIndex()} />
      )}
    </section>
  )
}

function ListIndex({
  lists,
  onOpen,
  onNew,
}: {
  lists: ArmyListSummary[] | null
  onOpen: (id: string) => void
  onNew: () => void
}) {
  if (lists === null) return <p className="hint">Loading…</p>
  if (lists.length === 0) {
    return (
      <p className="hint">
        No lists yet. Press <button type="button" className="link-button" onClick={onNew}>New list</button> to build
        your first — or take the tour.
      </p>
    )
  }
  return (
    <ul className="list-index">
      {lists.map((l) => (
        <li key={l.id} onClick={() => onOpen(l.id)}>
          <div>
            <div className="li-name">{l.name}</div>
            <div className="li-meta">
              {l.factionName} · {l.entryCount} {l.entryCount === 1 ? 'entry' : 'entries'}
            </div>
          </div>
          <div className="li-points">
            {l.total}
            {l.pointsLimit != null ? ` / ${l.pointsLimit}` : ''} pts
          </div>
        </li>
      ))}
    </ul>
  )
}

function CreateForm({ onCreated, onCancel }: { onCreated: (list: ArmyList) => void; onCancel: () => void }) {
  const tour = useTour()
  const [systems, setSystems] = useState<CatalogueSystem[]>([])
  const [factions, setFactions] = useState<CatalogueFaction[]>([])
  const [systemKey, setSystemKey] = useState('')
  const [factionId, setFactionId] = useState('')
  const [name, setName] = useState('')
  const [limit, setLimit] = useState('2000')
  const [battleSize, setBattleSize] = useState<string>('Strike Force')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getCatalogueSystems()
      .then((s) => {
        setSystems(s)
        // Default to the first pointed system so the points step is present.
        const preferred = s.find((x) => !x.pointless) ?? s[0]
        if (preferred) setSystemKey(preferred.key)
      })
      .catch(() => setError('Could not load game systems. Is the catalogue loaded?'))
  }, [])

  useEffect(() => {
    if (!systemKey) return
    setFactions([])
    setFactionId('')
    getFactions(systemKey)
      .then((f) => {
        setFactions(f)
        if (f.length > 0) setFactionId(f[0].id)
      })
      .catch(() => setError('Could not load factions.'))
  }, [systemKey])

  const pointless = useMemo(
    () => systems.find((s) => s.key === systemKey)?.pointless ?? false,
    [systems, systemKey],
  )

  const canSubmit = useMemo(
    () => name.trim().length > 0 && systemKey !== '' && factionId !== '' && !busy,
    [name, systemKey, factionId, busy],
  )

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const custom = battleSize === 'Custom'
      const parsed = limit.trim() === '' ? null : Number(limit)
      const list = await createList({
        name: name.trim(),
        gameSystemKey: systemKey,
        factionId,
        pointsLimit: pointless || custom ? (Number.isNaN(parsed!) ? null : parsed) : BATTLE_SIZE_RULES[battleSize].cap,
        battleSize: pointless || custom ? null : battleSize,
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
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} autoFocus required />
      </label>

      <div className="create-grid" data-tour="create-faction">
        <label>
          Game system
          <select value={systemKey} onChange={(e) => setSystemKey(e.target.value)}>
            {systems.length === 0 && <option value="">Loading…</option>}
            {systems.map((s) => (
              <option key={s.key} value={s.key}>{s.name}</option>
            ))}
          </select>
        </label>
        <label>
          Faction
          <select value={factionId} onChange={(e) => setFactionId(e.target.value)}>
            {factions.length === 0 && <option value="">Loading…</option>}
            {factions.map((f) => (
              <option key={f.id} value={f.id}>{f.name}</option>
            ))}
          </select>
        </label>
      </div>

      {!pointless && (
        <div className="create-grid" data-tour="create-limit">
          <label>
            Battle size
            <select value={battleSize} onChange={(e) => setBattleSize(e.target.value)}>
              <option value="Custom">Custom</option>
              {BATTLE_SIZES.map((b) => (
                <option key={b} value={b}>{b} · {BATTLE_SIZE_RULES[b].cap} pts</option>
              ))}
            </select>
          </label>
          {battleSize === 'Custom' ? (
            <label>
              Points limit (optional)
              <input
                inputMode="numeric"
                value={limit}
                placeholder="e.g. 2000"
                onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))}
              />
            </label>
          ) : (
            <label>
              Points limit
              <input value={`${BATTLE_SIZE_RULES[battleSize].cap} pts`} disabled readOnly />
            </label>
          )}
        </div>
      )}

      {error && <p className="builder-error">{error}</p>}

      <div className="create-actions">
        <button type="submit" className="btn btn-primary" data-tour="create-submit" disabled={!canSubmit}>
          {busy ? 'Creating…' : 'Create list'}
        </button>
        <button type="button" className="btn" onClick={() => { tour.stop(false); onCancel() }}>
          Cancel
        </button>
      </div>
    </form>
  )
}
