import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import { getLists, type ArmyListSummary } from '../lib/lists'
import { getFriends, searchUsers, type Friend, type UserSearchResult } from '../lib/social'
import { joinSessionGroup, leaveSessionGroup, onSessionUpdated } from '../lib/gameHub'
import {
  advanceSession,
  deleteSessionAction,
  finishSession,
  getSession,
  inviteToSession,
  leaveSession,
  logSessionAction,
  respondToSession,
  setSessionList,
  setSessionRound,
  startSession,
  updateSessionUnit,
  type ActionKind,
  type GameSession,
  type SessionParticipant,
} from '../lib/gameSessions'
import './games.css'

export function GameSessionView({ sessionId, onExit }: { sessionId: string; onExit: () => void }) {
  const [session, setSession] = useState<GameSession | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setSession(await getSession(sessionId))
    } catch (err) {
      setError(err instanceof ApiError && err.status === 404 ? 'This game is no longer available.' : 'Could not load the game.')
    }
  }, [sessionId])

  useEffect(() => {
    void reload()
    void joinSessionGroup(sessionId).catch(() => {})
    const off = onSessionUpdated((id) => {
      if (id === sessionId) void reload()
    })
    return () => {
      off()
      void leaveSessionGroup(sessionId)
    }
  }, [sessionId, reload])

  if (!session) {
    return (
      <section className="card">
        <button className="btn" onClick={onExit}>Back</button>
        <p className="hint">{error ?? 'Loading…'}</p>
      </section>
    )
  }

  const me = session.participants.find((p) => p.isMe) ?? null

  async function act(fn: () => Promise<GameSession | void>) {
    setError(null)
    try {
      const next = await fn()
      if (next) setSession(next)
      else onExit()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    }
  }

  return (
    <section className="card">
      <div className="lists-head">
        <h2>{session.name || 'Live game'}</h2>
        <div className="lists-actions">
          <button className="btn" onClick={onExit}>Back</button>
        </div>
      </div>

      <div className="session-meta">
        <span className={`status-pill s-${session.status.toLowerCase()}`}>{statusLabel(session)}</span>
        {session.pointsLimit != null && <span className="status-pill">{session.pointsLimit} pts</span>}
        {session.gameSystemKey && <span className="status-pill">{session.gameSystemKey}</span>}
      </div>

      {error && <p className="builder-error">{error}</p>}

      {session.status === 'Setup' && <SetupView session={session} me={me} act={act} />}
      {session.status === 'Active' && me && <ActiveView session={session} me={me} act={act} setError={setError} />}
      {(session.status === 'Finished' || session.status === 'Abandoned') && <FinishedView session={session} />}
    </section>
  )
}

function statusLabel(s: GameSession): string {
  if (s.status === 'Active') return `Round ${s.currentRound}`
  if (s.status === 'Setup') return 'Setting up'
  if (s.status === 'Finished') return 'Finished'
  return 'Abandoned'
}

// ---------- Setup ----------

function SetupView({
  session,
  me,
  act,
}: {
  session: GameSession
  me: SessionParticipant | null
  act: (fn: () => Promise<GameSession | void>) => Promise<void>
}) {
  const [lists, setLists] = useState<ArmyListSummary[]>([])
  const acceptedCount = session.participants.filter((p) => p.status === 'Accepted').length

  useEffect(() => {
    if (me && me.status === 'Accepted') getLists().then(setLists).catch(() => {})
  }, [me])

  // Only lists for this game's system can be used here.
  const usableLists = lists.filter((l) => !session.gameSystemKey || l.gameSystemKey === session.gameSystemKey)

  return (
    <div className="session-body">
      <div>
        <h3 className="section-title">Players ({acceptedCount} ready · max {session.maxParticipants})</h3>
        <ul className="people-list">
          {session.participants
            .filter((p) => p.status !== 'Declined' && p.status !== 'Left')
            .map((p) => (
              <li className="person-row" key={p.id}>
                <span className="person-tap" style={{ cursor: 'default' }}>
                  <span className="avatar">{p.username.slice(0, 2)}</span>
                  <span className="person-main">
                    <span className="person-name">
                      {p.username}
                      {p.userId === session.createdByUserId ? ' · host' : ''}
                      {p.isMe ? ' (you)' : ''}
                    </span>
                    <span className="person-sub">
                      {p.status === 'Invited' ? 'invited…' : p.armyListId ? `${p.factionName} · list ready` : 'no list yet'}
                    </span>
                  </span>
                </span>
                <span className={`status-pill s-${p.status.toLowerCase()}`}>{p.status}</span>
              </li>
            ))}
        </ul>
      </div>

      {me?.status === 'Invited' && (
        <div className="invite-actions">
          <p className="empty-note">You've been invited to this game.</p>
          <div className="create-actions">
            <button className="btn btn-primary" onClick={() => act(() => respondToSession(session.id, true))}>Accept</button>
            <button className="btn" onClick={() => act(() => respondToSession(session.id, false))}>Decline</button>
          </div>
        </div>
      )}

      {me?.status === 'Accepted' && (
        <div>
          <h3 className="section-title">Your army</h3>
          <label className="create-form">
            <select
              value={me.armyListId ?? ''}
              onChange={(e) => act(() => setSessionList(session.id, e.target.value || null))}
            >
              <option value="">Choose a list…</option>
              {usableLists.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name} · {l.factionName}{l.total > 0 ? ` (${l.total} pts)` : ''}
                </option>
              ))}
            </select>
          </label>
          {usableLists.length === 0 && (
            <p className="empty-note">No lists for this game system yet. Build one in the Lists tab first.</p>
          )}
          {me.units.length > 0 && (
            <>
              <p className="empty-note">
                Models default to your list. Bring fewer if you like — you can't bring more than your list allows.
              </p>
              <ul className="unit-list">
                {me.units.map((u) => (
                  <li className="unit-row" key={u.id}>
                    <span className="unit-name">{u.name}</span>
                    <Stepper
                      value={u.startingModels}
                      min={1}
                      max={u.maxModels > 0 ? u.maxModels : undefined}
                      onChange={(v) => act(() => updateSessionUnit(session.id, u.id, { startingModels: v }))}
                      suffix={u.maxModels > 1 ? `/ ${u.maxModels} models` : 'models'}
                    />
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}

      {session.isHost && <InvitePicker session={session} act={act} />}

      <div className="create-actions">
        {session.isHost && (
          <button className="btn btn-primary" disabled={acceptedCount < 2} onClick={() => act(() => startSession(session.id))}>
            Start game
          </button>
        )}
        <button className="btn" onClick={() => act(() => leaveSession(session.id).then(() => undefined))}>
          {session.isHost ? 'Cancel game' : 'Leave'}
        </button>
      </div>
    </div>
  )
}

// Invite lookup: friends first, then public players, with a text fallback so a
// private-profile player can still be invited by exact username.
function InvitePicker({
  session,
  act,
}: {
  session: GameSession
  act: (fn: () => Promise<GameSession | void>) => Promise<void>
}) {
  const [query, setQuery] = useState('')
  const [friends, setFriends] = useState<Friend[]>([])
  const [results, setResults] = useState<UserSearchResult[]>([])

  useEffect(() => {
    getFriends().then(setFriends).catch(() => {})
  }, [])

  useEffect(() => {
    const q = query.trim()
    if (q.length < 1) {
      setResults([])
      return
    }
    const handle = setTimeout(() => {
      searchUsers(q)
        .then(setResults)
        .catch(() => setResults([]))
    }, 250)
    return () => clearTimeout(handle)
  }, [query])

  const term = query.trim().toLowerCase()
  const taken = new Set(
    session.participants
      .filter((p) => p.status !== 'Declined' && p.status !== 'Left')
      .map((p) => p.username.toLowerCase()),
  )
  const friendMatches = friends.filter(
    (f) => !taken.has(f.username.toLowerCase()) && (term === '' || f.username.toLowerCase().includes(term)),
  )
  const publicMatches = results.filter(
    (r) => r.friendshipStatus !== 'friends' && r.visibility === 'Public' && !taken.has(r.username.toLowerCase()),
  )

  const invite = (username: string) => act(() => inviteToSession(session.id, username)).then(() => setQuery(''))

  return (
    <div>
      <h3 className="section-title">Invite players</h3>
      <form
        className="invite-row"
        onSubmit={(e) => {
          e.preventDefault()
          const name = query.trim()
          if (name) void invite(name)
        }}
      >
        <input
          className="search-box"
          placeholder="Search by username…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          autoCapitalize="off"
          autoComplete="off"
        />
        <button className="btn" type="submit">Invite</button>
      </form>

      {friendMatches.length > 0 && (
        <>
          <div className="suggest-head">Friends</div>
          <ul className="people-list">
            {friendMatches.map((f) => (
              <InviteRow key={f.id} username={f.username} onInvite={() => void invite(f.username)} />
            ))}
          </ul>
        </>
      )}

      {publicMatches.length > 0 && (
        <>
          <div className="suggest-head">Public players</div>
          <ul className="people-list">
            {publicMatches.map((r) => (
              <InviteRow key={r.id} username={r.username} onInvite={() => void invite(r.username)} />
            ))}
          </ul>
        </>
      )}

      {term.length > 0 && friendMatches.length === 0 && publicMatches.length === 0 && (
        <p className="empty-note">No players found — press Invite to send to that exact username.</p>
      )}
    </div>
  )
}

function InviteRow({ username, onInvite }: { username: string; onInvite: () => void }) {
  return (
    <li className="person-row">
      <span className="person-tap" style={{ cursor: 'default' }}>
        <span className="avatar">{username.slice(0, 2)}</span>
        <span className="person-main">
          <span className="person-name">{username}</span>
        </span>
      </span>
      <div className="person-actions">
        <button type="button" className="btn-sm primary" onClick={onInvite}>Invite</button>
      </div>
    </li>
  )
}

// ---------- Active ----------

function ActiveView({
  session,
  me,
  act,
  setError,
}: {
  session: GameSession
  me: SessionParticipant
  act: (fn: () => Promise<GameSession | void>) => Promise<void>
  setError: (m: string | null) => void
}) {
  const [round, setRound] = useState(session.currentRound)
  useEffect(() => {
    setRound(session.currentRound)
  }, [session.currentRound])

  const opponents = session.participants.filter((p) => !p.isMe && p.status === 'Accepted')

  function payload(n: number) {
    const r = me.rounds.find((x) => x.roundNumber === n)
    return {
      primaryScore: r?.primaryScore ?? 0,
      cpGained: r?.cpGained ?? 0,
      cpSpent: r?.cpSpent ?? 0,
      secondaries: (r?.secondaries ?? []).map((s) => ({ name: s.name, score: s.score })),
    }
  }
  const commit = (n: number, next: ReturnType<typeof payload>) => act(() => setSessionRound(session.id, n, next))

  const current = payload(round)
  const myRound = me.rounds.find((r) => r.roundNumber === round)

  return (
    <div className="session-body">
      <div className="round-bar">
        <div className="round-chips">
          {Array.from({ length: session.currentRound }, (_, i) => i + 1).map((n) => (
            <button key={n} className={`round-chip ${n === round ? 'active' : ''}`} onClick={() => setRound(n)}>R{n}</button>
          ))}
        </div>
        {session.isHost && (
          <div className="lists-actions">
            <button className="btn-sm" onClick={() => act(() => advanceSession(session.id))}>Next round</button>
            <button className="btn-sm primary" onClick={() => act(() => finishSession(session.id))}>Finish</button>
          </div>
        )}
      </div>

      <Scoreboard session={session} />

      <div className="my-panel">
        <h3 className="section-title">Your round {round}</h3>

        <div className="score-grid">
          <div className="score-cell">
            <span className="cell-label">Primary</span>
            <Stepper value={current.primaryScore} min={0} onChange={(v) => commit(round, { ...current, primaryScore: v })} />
          </div>
          <div className="score-cell">
            <span className="cell-label">CP gained</span>
            <Stepper value={current.cpGained} min={0} onChange={(v) => commit(round, { ...current, cpGained: v })} />
          </div>
          <div className="score-cell">
            <span className="cell-label">CP spent</span>
            <Stepper value={current.cpSpent} min={0} onChange={(v) => commit(round, { ...current, cpSpent: v })} />
          </div>
        </div>

        <div className="sub-head">
          <span className="cell-label">Secondaries</span>
          <span className="round-total">Round total: {myRound?.roundTotal ?? 0} · Running CP: {me.currentCp}</span>
        </div>
        <ul className="secondary-list">
          {current.secondaries.map((s, idx) => (
            <li className="secondary-row" key={idx}>
              <span className="unit-name">{s.name}</span>
              <Stepper
                value={s.score}
                min={0}
                onChange={(v) => {
                  const next = { ...current, secondaries: current.secondaries.map((x, i) => (i === idx ? { ...x, score: v } : x)) }
                  commit(round, next)
                }}
              />
              <button className="icon-btn" aria-label="Remove" onClick={() => commit(round, { ...current, secondaries: current.secondaries.filter((_, i) => i !== idx) })}>×</button>
            </li>
          ))}
        </ul>
        <AddSecondary onAdd={(name) => commit(round, { ...current, secondaries: [...current.secondaries, { name, score: 0 }] })} />

        <UnitTracker session={session} me={me} act={act} />

        <ActionPanel session={session} me={me} act={act} setError={setError} />
      </div>

      {opponents.length > 0 && (
        <div>
          <h3 className="section-title">Opponents</h3>
          <div className="opponent-cards">
            {opponents.map((o) => <OpponentCard key={o.id} p={o} />)}
          </div>
        </div>
      )}

      <div className="create-actions">
        <button className="btn" onClick={() => act(() => leaveSession(session.id).then(() => undefined))}>Leave game</button>
      </div>
    </div>
  )
}

function Scoreboard({ session }: { session: GameSession }) {
  const players = [...session.participants.filter((p) => p.status === 'Accepted')].sort((a, b) => b.totalScore - a.totalScore)
  return (
    <ul className="scoreboard">
      {players.map((p) => (
        <li className="score-row" key={p.id}>
          <span className="score-name">{p.username}{p.isMe ? ' (you)' : ''}</span>
          <span className="score-bits">
            <b>{p.totalScore}</b> vp · {p.currentCp} cp · {p.unitsDestroyed}/{p.unitsTotal} lost
          </span>
        </li>
      ))}
    </ul>
  )
}

function UnitTracker({
  session,
  me,
  act,
}: {
  session: GameSession
  me: SessionParticipant
  act: (fn: () => Promise<GameSession | void>) => Promise<void>
}) {
  if (me.units.length === 0) return null
  return (
    <>
      <div className="sub-head"><span className="cell-label">Your units</span></div>
      <ul className="unit-list">
        {me.units.map((u) => (
          <li className={`unit-row ${u.destroyed ? 'dead' : ''}`} key={u.id}>
            <span className="unit-name">{u.name}</span>
            <div className="unit-controls">
              <Stepper
                value={u.currentModels}
                min={0}
                max={u.startingModels}
                suffix={`/ ${u.startingModels}`}
                onChange={(v) => act(() => updateSessionUnit(session.id, u.id, { currentModels: v }))}
              />
              <button
                className={`btn-sm ${u.destroyed ? 'danger' : 'ghost'}`}
                onClick={() => act(() => updateSessionUnit(session.id, u.id, { destroyed: !u.destroyed }))}
              >
                {u.destroyed ? 'Dead' : 'Kill'}
              </button>
            </div>
          </li>
        ))}
      </ul>
    </>
  )
}

function ActionPanel({
  session,
  me,
  act,
  setError,
}: {
  session: GameSession
  me: SessionParticipant
  act: (fn: () => Promise<GameSession | void>) => Promise<void>
  setError: (m: string | null) => void
}) {
  const [kind, setKind] = useState<ActionKind>('Stratagem')
  const [name, setName] = useState('')
  const [cp, setCp] = useState('')

  function add() {
    const n = name.trim()
    if (!n) return
    const cpCost = cp.trim() === '' ? null : Number(cp)
    act(() =>
      logSessionAction(session.id, {
        roundNumber: session.currentRound,
        kind,
        name: n,
        cpCost: Number.isNaN(cpCost as number) ? null : cpCost,
        note: null,
      }),
    ).then(() => {
      setName('')
      setCp('')
    })
    setError(null)
  }

  return (
    <>
      <div className="sub-head"><span className="cell-label">Abilities & stratagems used</span></div>
      {me.actions.length > 0 && (
        <ul className="action-list">
          {me.actions.map((a) => (
            <li className="action-row" key={a.id}>
              <span className={`kind-tag ${a.kind.toLowerCase()}`}>{a.kind === 'Stratagem' ? 'STR' : 'ABL'}</span>
              <span className="action-main">
                <span className="unit-name">{a.name}</span>
                <span className="person-sub">R{a.roundNumber}{a.cpCost != null ? ` · ${a.cpCost} CP` : ''}</span>
              </span>
              <button className="icon-btn" aria-label="Remove" onClick={() => act(() => deleteSessionAction(session.id, a.id))}>×</button>
            </li>
          ))}
        </ul>
      )}
      <div className="action-add">
        <div className="segmented sm">
          <button className={kind === 'Stratagem' ? 'active' : ''} onClick={() => setKind('Stratagem')}>Stratagem</button>
          <button className={kind === 'Ability' ? 'active' : ''} onClick={() => setKind('Ability')}>Ability</button>
        </div>
        <div className="action-inputs">
          <input className="search-box" placeholder={kind === 'Stratagem' ? 'Stratagem name…' : 'Ability name…'} value={name} onChange={(e) => setName(e.target.value)} />
          <input className="cp-input" inputMode="numeric" placeholder="CP" value={cp} onChange={(e) => setCp(e.target.value.replace(/[^0-9]/g, ''))} />
          <button className="btn-sm primary" onClick={add}>Log</button>
        </div>
      </div>
    </>
  )
}

function OpponentCard({ p }: { p: SessionParticipant }) {
  return (
    <div className="opponent-card">
      <div className="opp-head">
        <span className="person-name">{p.username}</span>
        <span className="person-sub">{p.factionName ?? '—'}</span>
      </div>
      <div className="opp-stats">
        <span><b>{p.totalScore}</b> vp</span>
        <span><b>{p.currentCp}</b> cp</span>
        <span><b>{p.unitsDestroyed}/{p.unitsTotal}</b> lost</span>
      </div>
      {p.rounds.length > 0 && (
        <div className="opp-rounds">
          {p.rounds.map((r) => (
            <span key={r.roundNumber} className="opp-round">R{r.roundNumber}: {r.roundTotal}</span>
          ))}
        </div>
      )}
      {p.actions.length > 0 && (
        <div className="opp-actions">{p.actions.length} ability/stratagem use{p.actions.length === 1 ? '' : 's'}</div>
      )}
    </div>
  )
}

// ---------- Finished ----------

function FinishedView({ session }: { session: GameSession }) {
  const players = [...session.participants.filter((p) => p.status === 'Accepted')].sort((a, b) => b.totalScore - a.totalScore)
  const best = players[0]?.totalScore ?? 0
  return (
    <div className="session-body">
      <h3 className="section-title">{session.status === 'Abandoned' ? 'Game abandoned' : 'Final standings'}</h3>
      <ol className="standings">
        {players.map((p) => (
          <li key={p.id} className={`standing-row ${p.totalScore === best ? 'winner' : ''}`}>
            <span className="score-name">{p.username}{p.isMe ? ' (you)' : ''}</span>
            <span className="score-bits"><b>{p.totalScore}</b> vp · {p.factionName ?? '—'}</span>
          </li>
        ))}
      </ol>
      <p className="empty-note">This game has been recorded in each player's history.</p>
    </div>
  )
}

// ---------- shared ----------

function Stepper({
  value,
  onChange,
  min = 0,
  max,
  suffix,
}: {
  value: number
  onChange: (v: number) => void
  min?: number
  max?: number
  suffix?: string
}) {
  const dec = () => onChange(Math.max(min, value - 1))
  const inc = () => onChange(max != null ? Math.min(max, value + 1) : value + 1)
  return (
    <span className="stepper">
      <button className="step-btn" aria-label="Decrease" onClick={dec} disabled={value <= min}>−</button>
      <span className="step-value">{value}{suffix ? ` ${suffix}` : ''}</span>
      <button className="step-btn" aria-label="Increase" onClick={inc} disabled={max != null && value >= max}>+</button>
    </span>
  )
}

function AddSecondary({ onAdd }: { onAdd: (name: string) => void }) {
  const [name, setName] = useState('')
  return (
    <form
      className="invite-row"
      onSubmit={(e) => {
        e.preventDefault()
        const n = name.trim()
        if (n) {
          onAdd(n)
          setName('')
        }
      }}
    >
      <input className="search-box" placeholder="Add a secondary…" value={name} onChange={(e) => setName(e.target.value)} maxLength={80} />
      <button className="btn" type="submit">Add</button>
    </form>
  )
}
