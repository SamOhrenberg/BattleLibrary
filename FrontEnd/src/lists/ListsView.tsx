import { useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { getCatalogueSystems, getFactions, type CatalogueFaction, type CatalogueSystem } from '../lib/catalogue'
import { BATTLE_SIZES, createList, deleteList, duplicateList, exportRoster, getList, getLists, importRoster, type ArmyList, type ArmyListSummary } from '../lib/lists'
import { BATTLE_SIZE_RULES } from './validation'
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
  const [systemNames, setSystemNames] = useState<Record<string, string>>({})
  const [active, setActive] = useState<ArmyList | null>(null)
  const toast = useToast()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const autoOffered = useRef(false)

  useEffect(() => {
    getLists()
      .then(setLists)
      .catch(() => toast.push('Could not load your lists.', 'error'))
  }, [toast])

  // Readable game names for the "by game" grouping; keyed by gameSystemKey.
  useEffect(() => {
    getCatalogueSystems()
      .then((systems) => {
        const map: Record<string, string> = {}
        for (const s of systems) map[s.key] = s.name
        setSystemNames(map)
      })
      .catch(() => {
        /* Grouping falls back to the raw system key if names can't be loaded. */
      })
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

  async function onExport(id: string, name: string) {
    try {
      await exportRoster(id, name)
    } catch {
      toast.push('Could not export that list.', 'error')
    }
  }

  async function onImportFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // let the same file be re-picked later
    if (!file) return
    try {
      const result = await importRoster(file)
      setLists(await getLists())
      const n = result.unmatched.length
      toast.push(
        n > 0
          ? `Imported “${result.list.name}”. ${n} ${n === 1 ? 'entry' : 'entries'} couldn’t be matched and ${n === 1 ? 'was' : 'were'} skipped: ${result.unmatched.join(', ')}`
          : `Imported “${result.list.name}”.`,
        n > 0 ? 'info' : 'success',
      )
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not import that roster.', 'error')
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
              <button type="button" className="btn" onClick={() => fileInputRef.current?.click()}>
                Import roster
              </button>
              <button type="button" className="btn btn-primary" data-tour="new-list" onClick={openCreate}>
                New list
              </button>
            </>
          )}
        </div>
      </div>

      <input
        ref={fileInputRef}
        type="file"
        accept=".ros,.rosz"
        hidden
        onChange={(e) => void onImportFile(e)}
      />

      {view === 'index' && (
        <ListIndex
          lists={lists}
          systemNames={systemNames}
          onOpen={(id) => void openList(id)}
          onNew={openCreate}
          onTour={tour.start}
          tourLabel={tour.hasRun ? 'Replay the tour' : 'Take the tour'}
          onDuplicate={(id) => void onDuplicate(id)}
          onDelete={(id) => void onDelete(id)}
          onExport={(id, name) => void onExport(id, name)}
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

function groupLists(lists: ArmyListSummary[], systemNames: Record<string, string>): ArmyGroup[] {
  const groups = new Map<string, ArmyGroup>()
  for (const l of lists) {
    const faction = l.factionName || 'Unknown army'
    const key = `${l.gameSystemKey}\u0000${faction}`
    let group = groups.get(key)
    if (!group) {
      group = { key, faction, system: systemNames[l.gameSystemKey] ?? l.gameSystemKey, lists: [] }
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
  systemNames,
  onOpen,
  onNew,
  onTour,
  tourLabel,
  onDuplicate,
  onDelete,
  onExport,
}: {
  lists: ArmyListSummary[] | null
  systemNames: Record<string, string>
  onOpen: (id: string) => void
  onNew: () => void
  onTour: () => void
  tourLabel: string
  onDuplicate: (id: string) => void
  onDelete: (id: string) => void
  onExport: (id: string, name: string) => void
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
        .map((key) => ({ key, name: systemNames[key] ?? key }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [lists, systemNames],
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
  const groups = useMemo(() => groupLists(visible, systemNames), [visible, systemNames])

  if (lists === null) return <p className="hint">Loading…</p>
  if (lists.length === 0) {
    return (
      <div className="empty-note">
        <p>No lists yet. Build your first army list, or import a BattleScribe roster.</p>
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
                    onExport={onExport}
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
  onExport,
}: {
  list: ArmyListSummary
  onOpen: (id: string) => void
  onDuplicate: (id: string) => void
  onDelete: (id: string) => void
  onExport: (id: string, name: string) => void
}) {
  return (
    <li className="list-card" onClick={() => onOpen(l.id)}>
      <div className="lc-main">
        <div className="lc-top">
          <span className="li-name">{l.name}</span>
          <span className="li-points">
            {l.total}
            {l.pointsLimit != null ? ` / ${l.pointsLimit}` : ''} pts
          </span>
        </div>
        <div className="li-meta">
          {shortFaction(l.factionName)} · {l.entryCount} {l.entryCount === 1 ? 'entry' : 'entries'}
          {l.usesLegends && <span className="tier-tag legends">Legends</span>}
          {l.usesCrucible && <span className="tier-tag crucible">Crucible</span>}
        </div>
      </div>
      <OverflowMenu
        label={`Actions for ${l.name}`}
        items={[
          { label: 'Duplicate', onSelect: () => onDuplicate(l.id) },
          { label: 'Export roster (.rosz)', onSelect: () => onExport(l.id, l.name) },
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

// Faction names encode a hierarchy with " - " (e.g. "Imperium - Adeptus Astartes
// - Blood Angels"): alliance, faction, and an optional third level. The tree
// drives the cascading selects; a flat list (no separators, e.g. Kill Team)
// leaves `hierarchical` false and the form shows a single faction dropdown.
const NAME_SEP = ' - '
const KEY_SEP = '\u0000'

interface FactionLeaf {
  label: string
  id: string
}

interface FactionTree {
  hierarchical: boolean
  alliances: string[]
  groups: Record<string, string[]>
  leaves: Record<string, FactionLeaf[]>
}

const leafKey = (alliance: string, group: string) => `${alliance}${KEY_SEP}${group}`

function buildFactionTree(factions: CatalogueFaction[]): FactionTree {
  const tree: FactionTree = { hierarchical: false, alliances: [], groups: {}, leaves: {} }
  for (const f of factions) {
    const parts = f.name.split(NAME_SEP)
    if (parts.length > 1) tree.hierarchical = true
    const alliance = parts[0]
    const group = parts[1] ?? parts[0]
    const label = parts[2] ?? group
    if (!tree.alliances.includes(alliance)) {
      tree.alliances.push(alliance)
      tree.groups[alliance] = []
    }
    if (!tree.groups[alliance].includes(group)) tree.groups[alliance].push(group)
    ;(tree.leaves[leafKey(alliance, group)] ??= []).push({ label, id: f.id })
  }
  tree.alliances.sort()
  for (const a of tree.alliances) tree.groups[a].sort()
  for (const key of Object.keys(tree.leaves)) tree.leaves[key].sort((a, b) => a.label.localeCompare(b.label))
  return tree
}

function CreateForm({ onCreated, onCancel }: { onCreated: (list: ArmyList) => void; onCancel: () => void }) {
  const tour = useTour()
  const [systems, setSystems] = useState<CatalogueSystem[]>([])
  const [factions, setFactions] = useState<CatalogueFaction[]>([])
  const [systemKey, setSystemKey] = useState('')
  const [factionId, setFactionId] = useState('')
  const [alliance, setAlliance] = useState('')
  const [group, setGroup] = useState('')
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
      .then(setFactions)
      .catch(() => setError('Could not load factions.'))
  }, [systemKey])

  const tree = useMemo(() => buildFactionTree(factions), [factions])
  const leafOptions = tree.leaves[leafKey(alliance, group)] ?? []

  // Seed the cascade (or the flat dropdown) from the first faction once loaded.
  useEffect(() => {
    if (factions.length === 0) return
    if (!tree.hierarchical) {
      setFactionId(factions[0].id)
      return
    }
    const a = tree.alliances[0]
    const g = tree.groups[a][0]
    setAlliance(a)
    setGroup(g)
    setFactionId(tree.leaves[leafKey(a, g)][0].id)
  }, [tree, factions])

  function pickAlliance(a: string) {
    const g = tree.groups[a][0]
    setAlliance(a)
    setGroup(g)
    setFactionId(tree.leaves[leafKey(a, g)][0].id)
  }

  function pickGroup(g: string) {
    setGroup(g)
    setFactionId(tree.leaves[leafKey(alliance, g)][0].id)
  }

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

      <div className="create-faction-region" data-tour="create-faction">
        <div className="create-grid">
          <label>
            Game system
            <select value={systemKey} onChange={(e) => setSystemKey(e.target.value)}>
              {systems.length === 0 && <option value="">Loading…</option>}
              {systems.map((s) => (
                <option key={s.key} value={s.key}>{s.name}</option>
              ))}
            </select>
          </label>
          {tree.hierarchical ? (
            <label>
              Alliance
              <select value={alliance} onChange={(e) => pickAlliance(e.target.value)}>
                {tree.alliances.map((a) => (
                  <option key={a} value={a}>{a}</option>
                ))}
              </select>
            </label>
          ) : (
            <label>
              Faction
              <select value={factionId} onChange={(e) => setFactionId(e.target.value)}>
                {factions.length === 0 && <option value="">Loading…</option>}
                {factions.map((f) => (
                  <option key={f.id} value={f.id}>{f.name}</option>
                ))}
              </select>
            </label>
          )}
        </div>

        {tree.hierarchical && (
          <div className="create-grid">
            <label>
              Faction
              <select value={group} onChange={(e) => pickGroup(e.target.value)}>
                {(tree.groups[alliance] ?? []).map((g) => (
                  <option key={g} value={g}>{g}</option>
                ))}
              </select>
            </label>
            {leafOptions.length > 1 && (
              <label>
                Chapter
                <select value={factionId} onChange={(e) => setFactionId(e.target.value)}>
                  {leafOptions.map((l) => (
                    <option key={l.id} value={l.id}>{l.label}</option>
                  ))}
                </select>
              </label>
            )}
          </div>
        )}
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
