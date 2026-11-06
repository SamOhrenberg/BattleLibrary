import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { getCatalogueSystems, type CatalogueSystem } from '../lib/catalogue'
import { getLists, BATTLE_SIZES, type ArmyListSummary } from '../lib/lists'
import { BATTLE_SIZE_RULES } from '../lists/validation'
import { getFriends, searchUsers, type Friend } from '../lib/social'
import {
  deleteGame,
  getGames,
  recordGame,
  updateGame,
  type Game,
  type GameResult,
  type GameStats,
  type RecordGameInput,
} from '../lib/games'
import { createSession, getMySessions, type SessionSummary } from '../lib/gameSessions'
import { connectHub, onSessionsChanged } from '../lib/gameHub'
import { GameSessionView } from './GameSessionView'
import './games.css'

const RESULTS: GameResult[] = ['Win', 'Loss', 'Draw']

export function GamesView() {
  const [tab, setTab] = useState<'play' | 'history'>('play')
  return (
    <>
      <nav className="segmented main-nav" role="tablist" aria-label="Games sections">
        <button role="tab" aria-selected={tab === 'play'} className={tab === 'play' ? 'active' : ''} onClick={() => setTab('play')}>
          Play
        </button>
        <button role="tab" aria-selected={tab === 'history'} className={tab === 'history' ? 'active' : ''} onClick={() => setTab('history')}>
          History
        </button>
      </nav>
      {tab === 'play' ? <PlayTab /> : <HistoryTab />}
    </>
  )
}

function PlayTab() {
  const [sessions, setSessions] = useState<SessionSummary[] | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setSessions(await getMySessions())
    } catch {
      setError('Could not load your games.')
    }
  }, [])

  useEffect(() => {
    void reload()
    connectHub()
    const off = onSessionsChanged(() => void reload())
    return off
  }, [reload])

  if (openId) {
    return (
      <GameSessionView
        sessionId={openId}
        onExit={() => {
          setOpenId(null)
          void reload()
        }}
      />
    )
  }

  return (
    <section className="card">
      <div className="lists-head">
        <h2>{creating ? 'New game' : 'Live games'}</h2>
        <div className="lists-actions">
          {!creating && (
            <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
              New game
            </button>
          )}
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

      {creating ? (
        <CreateSessionForm onCreated={(id) => { setCreating(false); setOpenId(id) }} onCancel={() => setCreating(false)} />
      ) : (
        <SessionList sessions={sessions} onOpen={setOpenId} onNew={() => setCreating(true)} />
      )}
    </section>
  )
}

function SessionList({
  sessions,
  onOpen,
  onNew,
}: {
  sessions: SessionSummary[] | null
  onOpen: (id: string) => void
  onNew: () => void
}) {
  if (sessions === null) return <p className="hint">Loading…</p>
  if (sessions.length === 0) {
    return (
      <div className="empty-note">
        <p>No live games yet. Start one and invite up to three players, or wait for an invite to appear here.</p>
        <button type="button" className="btn btn-primary" onClick={onNew}>New game</button>
      </div>
    )
  }
  return (
    <ul className="list-index">
      {sessions.map((s) => (
        <li key={s.id} onClick={() => onOpen(s.id)}>
          <div>
            <div className="li-name">{s.name || 'Live game'}</div>
            <div className="li-meta">
              {s.isHost ? 'Host' : 'Player'} · {s.participantCount} player{s.participantCount === 1 ? '' : 's'}
              {s.status === 'Active' ? ` · round ${s.currentRound}` : ''}
            </div>
          </div>
          <div className="li-points">
            {s.myStatus === 'Invited' ? <span className="status-pill s-invited">Invited</span> : <span className={`status-pill s-${s.status.toLowerCase()}`}>{s.status === 'Active' ? 'In play' : 'Setup'}</span>}
          </div>
        </li>
      ))}
    </ul>
  )
}

function CreateSessionForm({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const [name, setName] = useState('')
  const [systems, setSystems] = useState<CatalogueSystem[]>([])
  const [systemKey, setSystemKey] = useState('')
  const [battleSize, setBattleSize] = useState('Strike Force')
  const [limit, setLimit] = useState('2000')
  const [startingCp, setStartingCp] = useState('0')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getCatalogueSystems()
      .then((s) => {
        setSystems(s)
        if (s.length > 0) setSystemKey(s[0].key)
      })
      .catch(() => {})
  }, [])

  const pointless = systems.find((s) => s.key === systemKey)?.pointless ?? false
  const custom = battleSize === 'Custom'

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const parsed = limit.trim() === '' ? null : Number(limit)
      // A named battle size sets its cap server-side; Custom keeps a raw limit.
      const session = await createSession({
        name: name.trim() || null,
        gameSystemKey: systemKey || null,
        battleSize: pointless || custom ? null : battleSize,
        pointsLimit: pointless ? null : custom ? (Number.isNaN(parsed as number) ? null : parsed) : null,
        startingCp: pointless ? 0 : Number(startingCp) || 0,
      })
      onCreated(session.id)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the game.')
      setBusy(false)
    }
  }

  return (
    <form className="create-form" onSubmit={submit}>
      <label>
        Game name (optional)
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={120}
          placeholder="e.g. Friday night 2000pts"
          enterKeyHint="next"
          autoFocus
        />
      </label>
      {systems.length > 1 && (
        <label>
          Game system
          <select value={systemKey} onChange={(e) => setSystemKey(e.target.value)}>
            {systems.map((s) => (
              <option key={s.key} value={s.key}>{s.name}</option>
            ))}
          </select>
        </label>
      )}
      {!pointless && (
        <label>
          Battle size
          <select value={battleSize} onChange={(e) => setBattleSize(e.target.value)}>
            {BATTLE_SIZES.map((s) => (
              <option key={s} value={s}>
                {s} · {BATTLE_SIZE_RULES[s].cap} pts
              </option>
            ))}
            <option value="Custom">Custom</option>
          </select>
        </label>
      )}
      {!pointless && custom && (
        <label>
          Points limit (optional)
          <input inputMode="numeric" value={limit} onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))} placeholder="e.g. 2000" />
        </label>
      )}
      {!pointless && (
        <label>
          Starting Command Points (CP)
          <input inputMode="numeric" enterKeyHint="done" value={startingCp} onChange={(e) => setStartingCp(e.target.value.replace(/[^0-9]/g, ''))} placeholder="0" />
        </label>
      )}
      {error && <p className="builder-error">{error}</p>}
      <div className="create-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Creating…' : 'Create game'}</button>
        <button type="button" className="btn" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

// A game with its display-ready system name and size label resolved on the
// client. Size is derived from the linked army list (battle size, else points),
// since a logged game does not store a size of its own.
type EnrichedGame = Game & { systemName: string; sizeLabel: string }

const UNSIZED = 'Unsized'

function HistoryTab() {
  const [games, setGames] = useState<Game[] | null>(null)
  const [systems, setSystems] = useState<CatalogueSystem[]>([])
  const [lists, setLists] = useState<ArmyListSummary[]>([])
  const [view, setView] = useState<'index' | 'form'>('index')
  const [editing, setEditing] = useState<Game | null>(null)
  const [sessionView, setSessionView] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [systemFilter, setSystemFilter] = useState('all')
  const [sizeFilter, setSizeFilter] = useState('all')

  const reload = useCallback(async () => {
    try {
      setGames(await getGames())
    } catch {
      setError('Could not load your games.')
    }
  }, [])

  useEffect(() => {
    void reload()
    getCatalogueSystems().then(setSystems).catch(() => {})
    getLists().then(setLists).catch(() => {})
  }, [reload])

  const listsById = useMemo(() => new Map(lists.map((l) => [l.id, l])), [lists])

  const enriched = useMemo<EnrichedGame[]>(() => {
    const systemName = (key: string) =>
      systems.find((s) => s.key === key)?.name || (key ? key : 'Unspecified')
    const sizeOf = (g: Game): string => {
      const list = g.playerListId ? listsById.get(g.playerListId) : undefined
      if (list?.battleSize) return list.battleSize
      if (list?.pointsLimit) return `${list.pointsLimit} pts`
      return UNSIZED
    }
    return (games ?? []).map((g) => ({ ...g, systemName: systemName(g.gameSystemKey), sizeLabel: sizeOf(g) }))
  }, [games, systems, listsById])

  const systemOptions = useMemo(() => {
    const seen = new Map<string, string>()
    enriched.forEach((g) => {
      if (g.gameSystemKey) seen.set(g.gameSystemKey, g.systemName)
    })
    return [...seen.entries()].sort((a, b) => a[1].localeCompare(b[1]))
  }, [enriched])

  const sizeOptions = useMemo(() => {
    const seen = new Set(enriched.map((g) => g.sizeLabel))
    return [...seen].sort(compareSizes)
  }, [enriched])

  const filtered = useMemo(
    () =>
      enriched.filter(
        (g) =>
          (systemFilter === 'all' || g.gameSystemKey === systemFilter) &&
          (sizeFilter === 'all' || g.sizeLabel === sizeFilter),
      ),
    [enriched, systemFilter, sizeFilter],
  )

  const viewStats = useMemo(() => computeStats(filtered), [filtered])
  const filtersActive = systemFilter !== 'all' || sizeFilter !== 'all'

  function openNew() {
    setEditing(null)
    setError(null)
    setView('form')
  }

  function openEdit(game: Game) {
    setEditing(game)
    setError(null)
    setView('form')
  }

  async function onSaved() {
    setView('index')
    setEditing(null)
    await reload()
  }

  async function remove(game: Game) {
    try {
      await deleteGame(game.id)
      await reload()
    } catch {
      setError('Could not delete that game.')
    }
  }

  function clearFilters() {
    setSystemFilter('all')
    setSizeFilter('all')
  }

  if (sessionView) {
    return (
      <GameSessionView
        sessionId={sessionView}
        onExit={() => {
          setSessionView(null)
          void reload()
        }}
      />
    )
  }

  if (view === 'form') {
    return <GameForm initial={editing} onSaved={onSaved} onCancel={() => setView('index')} />
  }

  const hasGames = (games?.length ?? 0) > 0

  return (
    <section className="card">
      <div className="lists-head">
        <h2>Games</h2>
        <div className="lists-actions">
          <button type="button" className="btn btn-primary" onClick={openNew}>
            Log a game
          </button>
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

      {hasGames && (
        <GameFilters
          systemOptions={systemOptions}
          sizeOptions={sizeOptions}
          systemFilter={systemFilter}
          sizeFilter={sizeFilter}
          onSystem={setSystemFilter}
          onSize={setSizeFilter}
          matched={filtered.length}
          filtersActive={filtersActive}
          onClear={clearFilters}
        />
      )}

      {viewStats.total > 0 && <StatsBar stats={viewStats} />}

      <div className="history-layout">
        <Timeline games={filtered} onPick={openEdit} />
        <div className="history-main">
          <GameHistory
            games={games === null ? null : filtered}
            filtersActive={filtersActive}
            onClear={clearFilters}
            onEdit={openEdit}
            onDelete={remove}
            onNew={openNew}
            onDetails={(g) => g.gameSessionId && setSessionView(g.gameSessionId)}
          />
        </div>
      </div>
    </section>
  )
}

function GameFilters({
  systemOptions,
  sizeOptions,
  systemFilter,
  sizeFilter,
  onSystem,
  onSize,
  matched,
  filtersActive,
  onClear,
}: {
  systemOptions: [string, string][]
  sizeOptions: string[]
  systemFilter: string
  sizeFilter: string
  onSystem: (v: string) => void
  onSize: (v: string) => void
  matched: number
  filtersActive: boolean
  onClear: () => void
}) {
  return (
    <div className="history-filters" role="group" aria-label="Filter games">
      <label className="filter-field">
        <span className="filter-label">Game</span>
        <select value={systemFilter} onChange={(e) => onSystem(e.target.value)}>
          <option value="all">All games</option>
          {systemOptions.map(([key, name]) => (
            <option key={key} value={key}>
              {name}
            </option>
          ))}
        </select>
      </label>
      <label className="filter-field">
        <span className="filter-label">Game size</span>
        <select value={sizeFilter} onChange={(e) => onSize(e.target.value)}>
          <option value="all">All sizes</option>
          {sizeOptions.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </label>
      <div className="filter-meta">
        <span className="filter-count">
          {matched} game{matched === 1 ? '' : 's'}
        </span>
        {filtersActive && (
          <button type="button" className="link-button" onClick={onClear}>
            Clear
          </button>
        )}
      </div>
    </div>
  )
}

function StatsBar({ stats }: { stats: GameStats }) {
  return (
    <div className="stats-bar" aria-label="Your record">
      <div className="stat-tile record">
        <span className="stat-n">
          {stats.wins}–{stats.losses}–{stats.draws}
        </span>
        <span className="stat-k">W–L–D</span>
      </div>
      <div className="stat-tile">
        <span className="stat-n">{stats.total}</span>
        <span className="stat-k">Games</span>
      </div>
      <div className="stat-tile">
        <span className="stat-n">{stats.winRate}%</span>
        <span className="stat-k">Win rate</span>
      </div>
    </div>
  )
}

// Left-hand visual history: a cumulative win/loss trend sparkline above a
// vertical timeline of games (newest first), colour-coded by result.
function Timeline({ games, onPick }: { games: EnrichedGame[]; onPick: (g: Game) => void }) {
  const recent = games // already newest-first from the API
  const chrono = useMemo(() => [...games].reverse(), [games])

  const net = useMemo(() => {
    const out: number[] = []
    let run = 0
    for (const g of chrono) {
      run += g.result === 'Win' ? 1 : g.result === 'Loss' ? -1 : 0
      out.push(run)
    }
    return out
  }, [chrono])

  if (games.length === 0) {
    return (
      <aside className="history-timeline empty" aria-label="Game timeline">
        <p className="hint">Your timeline will appear here once games match.</p>
      </aside>
    )
  }

  const finalNet = net[net.length - 1] ?? 0

  return (
    <aside className="history-timeline" aria-label="Game timeline">
      <div className="tl-head">
        <span className="tl-title">Timeline</span>
        <span className={`tl-net ${finalNet > 0 ? 'up' : finalNet < 0 ? 'down' : ''}`}>
          {finalNet > 0 ? `+${finalNet}` : finalNet} net
        </span>
      </div>

      <TrendChart net={net} />

      <ol className="tl-list">
        {recent.map((g) => {
          const r = g.result.toLowerCase()
          const tag = g.sizeLabel !== UNSIZED ? g.sizeLabel : g.systemName
          return (
            <li key={g.id} className={`tl-node ${r}`}>
              <span className="tl-dot" aria-hidden="true" />
              <button type="button" className="tl-entry" onClick={() => onPick(g)}>
                <span className="tl-row">
                  <span className="tl-opp">vs {g.opponentName}</span>
                  <span className={`tl-result ${r}`}>{g.result[0]}</span>
                </span>
                <span className="tl-sub">
                  <span className="tl-date">{formatShort(g.playedOn)}</span>
                  {tag && <span className="tl-tag">{tag}</span>}
                </span>
              </button>
            </li>
          )
        })}
      </ol>
    </aside>
  )
}

// Cumulative net-record sparkline (wins minus losses over time). Draws hold the
// line flat. Rendered as a themed SVG so it works in light and dark.
function TrendChart({ net }: { net: number[] }) {
  const W = 100
  const H = 54
  const PAD = 6
  const n = net.length
  const maxV = Math.max(0, ...net)
  const minV = Math.min(0, ...net)
  const range = maxV - minV || 1

  const x = (i: number) => (n <= 1 ? W / 2 : PAD + (i / (n - 1)) * (W - 2 * PAD))
  const y = (v: number) => PAD + (1 - (v - minV) / range) * (H - 2 * PAD)
  const zeroY = y(0)

  const pts = net.map((v, i) => `${x(i).toFixed(2)},${y(v).toFixed(2)}`)
  const line = n === 1 ? `M0,${zeroY.toFixed(2)} L${W},${zeroY.toFixed(2)}` : `M${pts.join(' L')}`
  const area =
    n === 1
      ? `M0,${zeroY.toFixed(2)} L${W},${zeroY.toFixed(2)} L${W},${zeroY.toFixed(2)} Z`
      : `M${pts[0]} L${pts.slice(1).join(' L')} L${x(n - 1).toFixed(2)},${zeroY.toFixed(2)} L${x(0).toFixed(2)},${zeroY.toFixed(2)} Z`
  const last = net[n - 1] ?? 0

  return (
    <svg
      className="tl-chart"
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      role="img"
      aria-label="Cumulative win/loss trend"
    >
      <defs>
        <linearGradient id="tl-fill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--accent)" stopOpacity="0.28" />
          <stop offset="100%" stopColor="var(--accent)" stopOpacity="0" />
        </linearGradient>
      </defs>
      <line className="tl-zero" x1="0" y1={zeroY} x2={W} y2={zeroY} />
      <path className="tl-area" d={area} fill="url(#tl-fill)" />
      <path className="tl-line" d={line} />
      <circle className={`tl-end ${last >= 0 ? 'up' : 'down'}`} cx={x(n - 1)} cy={y(last)} r="2.6" />
    </svg>
  )
}

function GameHistory({
  games,
  filtersActive,
  onClear,
  onEdit,
  onDelete,
  onNew,
  onDetails,
}: {
  games: Game[] | null
  filtersActive: boolean
  onClear: () => void
  onEdit: (g: Game) => void
  onDelete: (g: Game) => void
  onNew: () => void
  onDetails: (g: Game) => void
}) {
  if (games === null) return <p className="hint">Loading…</p>
  if (games.length === 0) {
    if (filtersActive) {
      return (
        <p className="hint">
          No games match these filters.{' '}
          <button type="button" className="link-button" onClick={onClear}>
            Clear filters
          </button>
        </p>
      )
    }
    return (
      <p className="hint">
        No games yet. Press{' '}
        <button type="button" className="link-button" onClick={onNew}>
          Log a game
        </button>{' '}
        to record your first.
      </p>
    )
  }
  return (
    <ul className="game-list">
      {games.map((g) => (
        <GameRow key={g.id} game={g} onEdit={() => onEdit(g)} onDelete={() => onDelete(g)} onDetails={() => onDetails(g)} />
      ))}
    </ul>
  )
}

// Recompute the record for the currently filtered set so the stats strip and
// timeline stay in step with the active filters.
function computeStats(games: Game[]): GameStats {
  let wins = 0
  let losses = 0
  let draws = 0
  for (const g of games) {
    if (g.result === 'Win') wins++
    else if (g.result === 'Loss') losses++
    else draws++
  }
  const total = wins + losses + draws
  const winRate = total === 0 ? 0 : Math.round((wins * 1000) / total) / 10
  return { total, wins, losses, draws, winRate }
}

// Battle sizes first (in play order), then points-based sizes ascending, then
// Unsized last, so the dropdown reads naturally.
function compareSizes(a: string, b: string): number {
  const rank = (s: string) => {
    if (s === UNSIZED) return 3
    const idx = BATTLE_SIZES.indexOf(s as (typeof BATTLE_SIZES)[number])
    if (idx >= 0) return idx
    return 2
  }
  const ra = rank(a)
  const rb = rank(b)
  if (ra !== rb) return ra - rb
  if (ra === 2) {
    const pa = parseInt(a, 10)
    const pb = parseInt(b, 10)
    if (!Number.isNaN(pa) && !Number.isNaN(pb)) return pa - pb
  }
  return a.localeCompare(b)
}

function formatShort(iso: string): string {
  const d = new Date(iso + 'T00:00:00')
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

function GameRow({
  game,
  onEdit,
  onDelete,
  onDetails,
}: {
  game: Game
  onEdit: () => void
  onDelete: () => void
  onDetails: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const r = game.result.toLowerCase()
  const score =
    game.playerScore != null && game.opponentScore != null
      ? `${game.playerScore}–${game.opponentScore}`
      : null
  const factions = [game.playerFactionName, game.opponentFactionName].filter(Boolean).join(' vs ')

  return (
    <li className="game-row">
      <button className="game-tap" onClick={onEdit}>
        <span className={`result-badge ${r}`}>{game.result[0]}</span>
        <span className="game-main">
          <span className="game-top">
            <span className="game-opp">vs {game.opponentName}</span>
            {score && <span className="game-score">{score}</span>}
          </span>
          <span className="game-sub">
            {formatDate(game.playedOn)}
            {factions ? ` · ${factions}` : ''}
          </span>
          {game.notes && <span className="game-notes">{game.notes}</span>}
        </span>
      </button>
      <div className="person-actions">
        {confirming ? (
          <>
            <button className="btn-sm ghost" onClick={() => setConfirming(false)}>
              Keep
            </button>
            <button className="btn-sm danger" onClick={onDelete}>
              Delete
            </button>
          </>
        ) : (
          <>
            {game.gameSessionId && (
              <button className="btn-sm ghost" onClick={onDetails}>
                Details
              </button>
            )}
            <button className="btn-sm ghost" onClick={() => setConfirming(true)}>
              Delete
            </button>
          </>
        )}
      </div>
    </li>
  )
}

function GameForm({
  initial,
  onSaved,
  onCancel,
}: {
  initial: Game | null
  onSaved: () => void
  onCancel: () => void
}) {
  const [playedOn, setPlayedOn] = useState(initial?.playedOn ?? today())
  const [result, setResult] = useState<GameResult>(initial?.result ?? 'Win')
  const [opponentName, setOpponentName] = useState(initial?.opponentName ?? '')
  const [opponentUserId, setOpponentUserId] = useState<string | null>(initial?.opponentUserId ?? null)
  const [gameSystemKey, setGameSystemKey] = useState(initial?.gameSystemKey ?? '')
  const [playerListId, setPlayerListId] = useState<string | null>(initial?.playerListId ?? null)
  const [playerFaction, setPlayerFaction] = useState(initial?.playerFactionName ?? '')
  const [opponentFaction, setOpponentFaction] = useState(initial?.opponentFactionName ?? '')
  const [playerScore, setPlayerScore] = useState(initial?.playerScore?.toString() ?? '')
  const [opponentScore, setOpponentScore] = useState(initial?.opponentScore?.toString() ?? '')
  const [notes, setNotes] = useState(initial?.notes ?? '')

  const [systems, setSystems] = useState<CatalogueSystem[]>([])
  const [lists, setLists] = useState<ArmyListSummary[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getCatalogueSystems().then(setSystems).catch(() => {})
    getLists().then(setLists).catch(() => {})
  }, [])

  function pickList(id: string) {
    setPlayerListId(id || null)
    const list = lists.find((l) => l.id === id)
    if (list) {
      setGameSystemKey(list.gameSystemKey)
      if (!playerFaction.trim()) setPlayerFaction(list.factionName)
    }
  }

  // Only offer lists that match the chosen game system.
  const listsForSystem = gameSystemKey ? lists.filter((l) => l.gameSystemKey === gameSystemKey) : lists

  function pickSystem(key: string) {
    setGameSystemKey(key)
    if (key && playerListId && !lists.some((l) => l.id === playerListId && l.gameSystemKey === key)) {
      setPlayerListId(null)
    }
  }

  const canSubmit = opponentName.trim().length > 0 && !busy

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const input: RecordGameInput = {
      playedOn: playedOn || null,
      gameSystemKey: gameSystemKey || null,
      result,
      opponentUserId,
      opponentName: opponentUserId ? null : opponentName.trim() || null,
      playerFactionName: playerFaction.trim() || null,
      opponentFactionName: opponentFaction.trim() || null,
      playerListId,
      playerScore: parseScore(playerScore),
      opponentScore: parseScore(opponentScore),
      notes: notes.trim() || null,
    }
    try {
      if (initial) {
        await updateGame(initial.id, input)
      } else {
        await recordGame(input)
      }
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the game.')
      setBusy(false)
    }
  }

  return (
    <section className="card">
      <div className="lists-head">
        <h2>{initial ? 'Edit game' : 'Log a game'}</h2>
        <div className="lists-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Back
          </button>
        </div>
      </div>

      <form className="create-form" onSubmit={onSubmit}>
        <div className="game-result-pick">
          <span className="field-label">Result</span>
          <div className="segmented">
            {RESULTS.map((r) => (
              <button
                key={r}
                type="button"
                className={result === r ? 'active' : ''}
                aria-selected={result === r}
                onClick={() => setResult(r)}
              >
                {r}
              </button>
            ))}
          </div>
        </div>

        <OpponentPicker
          name={opponentName}
          linkedId={opponentUserId}
          onName={(n) => {
            setOpponentName(n)
            setOpponentUserId(null)
          }}
          onPick={(id, name) => {
            setOpponentUserId(id)
            setOpponentName(name)
          }}
        />

        <label>
          Date
          <input type="date" value={playedOn} onChange={(e) => setPlayedOn(e.target.value)} max={today()} />
        </label>

        {listsForSystem.length > 0 && (
          <label>
            Your list (optional)
            <select value={playerListId ?? ''} onChange={(e) => pickList(e.target.value)}>
              <option value="">No list</option>
              {listsForSystem.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name} · {l.factionName}
                </option>
              ))}
            </select>
          </label>
        )}

        {systems.length > 0 && (
          <label>
            Game system (optional)
            <select value={gameSystemKey} onChange={(e) => pickSystem(e.target.value)}>
              <option value="">Unspecified</option>
              {systems.map((s) => (
                <option key={s.key} value={s.key}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
        )}

        <div className="create-grid">
          <label>
            Your faction
            <input
              value={playerFaction}
              onChange={(e) => setPlayerFaction(e.target.value)}
              maxLength={200}
              placeholder="e.g. Orks"
            />
          </label>
          <label>
            Their faction
            <input
              value={opponentFaction}
              onChange={(e) => setOpponentFaction(e.target.value)}
              maxLength={200}
              placeholder="e.g. Necrons"
            />
          </label>
        </div>

        <div className="create-grid">
          <label>
            Your score
            <input
              inputMode="numeric"
              value={playerScore}
              onChange={(e) => setPlayerScore(e.target.value.replace(/[^0-9]/g, ''))}
              placeholder="-"
            />
          </label>
          <label>
            Their score
            <input
              inputMode="numeric"
              value={opponentScore}
              onChange={(e) => setOpponentScore(e.target.value.replace(/[^0-9]/g, ''))}
              placeholder="-"
            />
          </label>
        </div>

        <label>
          Notes (optional)
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            maxLength={1000}
            placeholder="How the game went, key moments…"
          />
        </label>

        {error && <p className="builder-error">{error}</p>}

        <div className="create-actions">
          <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
            {busy ? 'Saving…' : initial ? 'Save changes' : 'Log game'}
          </button>
          <button type="button" className="btn" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  )
}

// Opponent field: free-text by default, with live suggestions of registered
// players and quick-pick of friends. Picking one links the game to that user.
function OpponentPicker({
  name,
  linkedId,
  onName,
  onPick,
}: {
  name: string
  linkedId: string | null
  onName: (name: string) => void
  onPick: (id: string, name: string) => void
}) {
  const [friends, setFriends] = useState<Friend[]>([])
  const [results, setResults] = useState<{ id: string; username: string }[]>([])
  const [open, setOpen] = useState(false)
  const blurTimer = useRef<number | undefined>(undefined)

  useEffect(() => {
    getFriends().then(setFriends).catch(() => {})
  }, [])

  useEffect(() => {
    if (linkedId) {
      setResults([])
      return
    }
    const q = name.trim()
    if (q.length < 1) {
      setResults([])
      return
    }
    const handle = setTimeout(() => {
      searchUsers(q)
        .then((r) => setResults(r.map((u) => ({ id: u.id, username: u.username }))))
        .catch(() => setResults([]))
    }, 250)
    return () => clearTimeout(handle)
  }, [name, linkedId])

  const suggestions = useMemo(() => {
    if (name.trim().length > 0) return results
    // Empty field: offer friends as quick picks.
    return friends.map((f) => ({ id: f.id, username: f.username }))
  }, [name, results, friends])

  return (
    <div className="opponent-field">
      <label>
        Opponent
        <div className="opponent-input">
          <input
            value={name}
            onChange={(e) => onName(e.target.value)}
            onFocus={() => setOpen(true)}
            onBlur={() => {
              blurTimer.current = window.setTimeout(() => setOpen(false), 150)
            }}
            placeholder="Name, or search a player…"
            autoComplete="off"
            maxLength={80}
            required
          />
          {linkedId && <span className="linked-pill" title="Linked to a registered player">✓ player</span>}
        </div>
      </label>
      {open && !linkedId && suggestions.length > 0 && (
        <ul className="opponent-suggest">
          {name.trim().length === 0 && <li className="suggest-head">Your friends</li>}
          {suggestions.slice(0, 6).map((s) => (
            <li key={s.id}>
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  clearTimeout(blurTimer.current)
                  onPick(s.id, s.username)
                  setOpen(false)
                }}
              >
                <span className="avatar sm">{s.username.slice(0, 2)}</span>
                {s.username}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function parseScore(value: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') return null
  const n = Number(trimmed)
  return Number.isNaN(n) ? null : n
}

function today(): string {
  return new Date().toISOString().slice(0, 10)
}

function formatDate(iso: string): string {
  const d = new Date(iso + 'T00:00:00')
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}
