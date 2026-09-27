import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { getCatalogueSystems, type CatalogueSystem } from '../lib/catalogue'
import { getLists, type ArmyListSummary } from '../lib/lists'
import { getFriends, searchUsers, type Friend } from '../lib/social'
import {
  deleteGame,
  getGames,
  getGameStats,
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
      <nav className="segmented main-nav" aria-label="Games sections">
        <button aria-selected={tab === 'play'} className={tab === 'play' ? 'active' : ''} onClick={() => setTab('play')}>
          Play
        </button>
        <button aria-selected={tab === 'history'} className={tab === 'history' ? 'active' : ''} onClick={() => setTab('history')}>
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
        <h2>Live games</h2>
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
        <SessionList sessions={sessions} onOpen={setOpenId} />
      )}
    </section>
  )
}

function SessionList({ sessions, onOpen }: { sessions: SessionSummary[] | null; onOpen: (id: string) => void }) {
  if (sessions === null) return <p className="hint">Loading…</p>
  if (sessions.length === 0) {
    return <p className="hint">No live games. Press New game to start one and invite up to three friends.</p>
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
  const [limit, setLimit] = useState('2000')
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

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const parsed = limit.trim() === '' ? null : Number(limit)
      const session = await createSession({
        name: name.trim() || null,
        gameSystemKey: systemKey || null,
        pointsLimit: Number.isNaN(parsed as number) ? null : parsed,
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
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} placeholder="e.g. Friday night 2000pts" autoFocus />
      </label>
      {systems.length > 0 && (
        <label>
          Game system
          <select value={systemKey} onChange={(e) => setSystemKey(e.target.value)}>
            {systems.map((s) => (
              <option key={s.key} value={s.key}>{s.name}</option>
            ))}
          </select>
        </label>
      )}
      <label>
        Points limit (optional)
        <input inputMode="numeric" value={limit} onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))} placeholder="e.g. 2000" />
      </label>
      {error && <p className="builder-error">{error}</p>}
      <div className="create-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Creating…' : 'Create game'}</button>
        <button type="button" className="btn" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

function HistoryTab() {
  const [games, setGames] = useState<Game[] | null>(null)
  const [stats, setStats] = useState<GameStats | null>(null)
  const [view, setView] = useState<'index' | 'form'>('index')
  const [editing, setEditing] = useState<Game | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const [g, s] = await Promise.all([getGames(), getGameStats()])
      setGames(g)
      setStats(s)
    } catch {
      setError('Could not load your games.')
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

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

  if (view === 'form') {
    return <GameForm initial={editing} onSaved={onSaved} onCancel={() => setView('index')} />
  }

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

      {stats && stats.total > 0 && <StatsBar stats={stats} />}

      <GameHistory games={games} onEdit={openEdit} onDelete={remove} onNew={openNew} />
    </section>
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

function GameHistory({
  games,
  onEdit,
  onDelete,
  onNew,
}: {
  games: Game[] | null
  onEdit: (g: Game) => void
  onDelete: (g: Game) => void
  onNew: () => void
}) {
  if (games === null) return <p className="hint">Loading…</p>
  if (games.length === 0) {
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
        <GameRow key={g.id} game={g} onEdit={() => onEdit(g)} onDelete={() => onDelete(g)} />
      ))}
    </ul>
  )
}

function GameRow({ game, onEdit, onDelete }: { game: Game; onEdit: () => void; onDelete: () => void }) {
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
          <button className="btn-sm ghost" onClick={() => setConfirming(true)}>
            Delete
          </button>
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

        {lists.length > 0 && (
          <label>
            Your list (optional)
            <select value={playerListId ?? ''} onChange={(e) => pickList(e.target.value)}>
              <option value="">No list</option>
              {lists.map((l) => (
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
            <select value={gameSystemKey} onChange={(e) => setGameSystemKey(e.target.value)}>
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
              placeholder="—"
            />
          </label>
          <label>
            Their score
            <input
              inputMode="numeric"
              value={opponentScore}
              onChange={(e) => setOpponentScore(e.target.value.replace(/[^0-9]/g, ''))}
              placeholder="—"
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
