import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import { getLists, setListFeatured, type ArmyListSummary } from '../lib/lists'
import {
  acceptFriendRequest,
  cancelFriendRequest,
  getFriendRequests,
  getFriends,
  getProfile,
  removeFriend,
  searchUsers,
  sendFriendRequest,
  updateProfile,
  type Friend,
  type FriendRequests,
  type UserSearchResult,
} from '../lib/social'
import { PublicProfile } from './PublicProfile'
import './social.css'

type Tab = 'profile' | 'friends' | 'find'

export function ProfileView({ username, email }: { username: string; email: string }) {
  const [tab, setTab] = useState<Tab>('profile')
  const [viewing, setViewing] = useState<string | null>(null)
  const [friends, setFriends] = useState<Friend[]>([])
  const [requests, setRequests] = useState<FriendRequests>({ incoming: [], outgoing: [] })
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const [f, r] = await Promise.all([getFriends(), getFriendRequests()])
      setFriends(f)
      setRequests(r)
    } catch {
      setError('Could not load your community.')
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  if (viewing) {
    return <PublicProfile username={viewing} onBack={() => setViewing(null)} onChanged={reload} />
  }

  return (
    <section className="card">
      <h2>Profile</h2>
      <div className="segmented" role="tablist">
        <button role="tab" aria-selected={tab === 'profile'} className={tab === 'profile' ? 'active' : ''} onClick={() => setTab('profile')}>
          You
        </button>
        <button role="tab" aria-selected={tab === 'friends'} className={tab === 'friends' ? 'active' : ''} onClick={() => setTab('friends')}>
          Friends{friends.length > 0 ? ` (${friends.length})` : ''}{requests.incoming.length > 0 ? ' •' : ''}
        </button>
        <button role="tab" aria-selected={tab === 'find'} className={tab === 'find' ? 'active' : ''} onClick={() => setTab('find')}>
          Find
        </button>
      </div>

      {error && <p className="builder-error">{error}</p>}

      <div className="community-body">
        {tab === 'profile' && <ProfileTab username={username} email={email} onPreview={() => setViewing(username)} />}
        {tab === 'friends' && <FriendsTab friends={friends} requests={requests} onView={setViewing} reload={reload} />}
        {tab === 'find' && <FindTab onView={setViewing} reload={reload} />}
      </div>
    </section>
  )
}

function ProfileTab({ username, email, onPreview }: { username: string; email: string; onPreview: () => void }) {
  const [visibility, setVisibility] = useState('Private')
  const [bio, setBio] = useState('')
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getProfile()
      .then((p) => {
        setVisibility(p.visibility)
        setBio(p.bio ?? '')
      })
      .catch(() => setError('Could not load your profile.'))
      .finally(() => setLoaded(true))
  }, [])

  async function save() {
    setBusy(true)
    setError(null)
    try {
      const p = await updateProfile(visibility, bio.trim() || null)
      setVisibility(p.visibility)
      setBio(p.bio ?? '')
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  if (!loaded) return <p className="hint">Loading…</p>

  return (
    <div className="profile-form">
      <div className="pub-head">
        <div className="avatar">{username.slice(0, 2)}</div>
        <div style={{ minWidth: 0, flex: 1 }}>
          <div className="pub-name">{username}</div>
          <div className="person-sub">{visibility} profile</div>
        </div>
        <button type="button" className="btn-sm" onClick={onPreview}>
          Preview
        </button>
      </div>

      <div>
        <h3 className="section-title">Profile visibility</h3>
        <div className="segmented">
          <button className={visibility === 'Private' ? 'active' : ''} onClick={() => setVisibility('Private')}>
            Private
          </button>
          <button className={visibility === 'Public' ? 'active' : ''} onClick={() => setVisibility('Public')}>
            Public
          </button>
        </div>
        <p className="vis-hint">
          {visibility === 'Public'
            ? 'Anyone can view your profile and army lists.'
            : 'Only you and your friends can see your profile and lists.'}
        </p>
      </div>

      <label>
        Bio
        <textarea
          value={bio}
          onChange={(e) => setBio(e.target.value)}
          maxLength={500}
          placeholder="A line about you, your armies, your local scene…"
        />
      </label>

      <label>
        Email
        <input className="search-box" value={email} disabled />
      </label>

      {error && <p className="builder-error">{error}</p>}

      <button type="button" className="btn btn-primary" onClick={save} disabled={busy || saved}>
        {busy ? 'Saving…' : saved ? 'Saved ✓' : 'Save profile'}
      </button>

      <HighlightLists />
    </div>
  )
}

// Pick which army lists are highlighted (shown first) on the public profile.
function HighlightLists() {
  const [lists, setLists] = useState<ArmyListSummary[] | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getLists()
      .then(setLists)
      .catch(() => setError('Could not load your lists.'))
  }, [])

  async function toggle(list: ArmyListSummary) {
    setBusyId(list.id)
    setError(null)
    try {
      const updated = await setListFeatured(list.id, !list.featured)
      setLists((prev) => prev?.map((l) => (l.id === list.id ? { ...l, featured: updated.featured } : l)) ?? null)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update that list.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <h3 className="section-title">Highlighted lists</h3>
      <p className="vis-hint">Starred lists appear first on your public profile.</p>
      {error && <p className="builder-error">{error}</p>}
      {lists === null ? (
        <p className="hint">Loading…</p>
      ) : lists.length === 0 ? (
        <p className="empty-note">No lists yet. Build one in the Lists tab.</p>
      ) : (
        <ul className="list-index">
          {lists.map((l) => (
            <li key={l.id} style={{ cursor: 'default' }}>
              <div>
                <div className="li-name">{l.name}</div>
                <div className="li-meta">
                  {l.factionName} · {l.entryCount} {l.entryCount === 1 ? 'entry' : 'entries'}
                </div>
              </div>
              <button
                type="button"
                className={`star-btn${l.featured ? ' on' : ''}`}
                aria-pressed={l.featured}
                aria-label={l.featured ? 'Remove highlight' : 'Highlight on profile'}
                disabled={busyId === l.id}
                onClick={() => toggle(l)}
              >
                {l.featured ? '★' : '☆'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function FriendsTab({
  friends,
  requests,
  onView,
  reload,
}: {
  friends: Friend[]
  requests: FriendRequests
  onView: (username: string) => void
  reload: () => Promise<void>
}) {
  const [busy, setBusy] = useState(false)

  async function act(action: () => Promise<unknown>) {
    setBusy(true)
    try {
      await action()
      await reload()
    } finally {
      setBusy(false)
    }
  }

  const nothing = friends.length === 0 && requests.incoming.length === 0 && requests.outgoing.length === 0

  return (
    <>
      {requests.incoming.length > 0 && (
        <div>
          <h3 className="section-title">Requests</h3>
          <ul className="people-list">
            {requests.incoming.map((r) => (
              <li className="person-row" key={r.id}>
                <button className="person-tap" onClick={() => onView(r.username)}>
                  <span className="avatar">{r.username.slice(0, 2)}</span>
                  <span className="person-main">
                    <span className="person-name">{r.username}</span>
                    <span className="person-sub">wants to be friends</span>
                  </span>
                </button>
                <div className="person-actions">
                  <button className="btn-sm primary" disabled={busy} onClick={() => act(() => acceptFriendRequest(r.id))}>
                    Accept
                  </button>
                  <button className="btn-sm ghost" disabled={busy} onClick={() => act(() => cancelFriendRequest(r.id))}>
                    Decline
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}

      {requests.outgoing.length > 0 && (
        <div>
          <h3 className="section-title">Sent</h3>
          <ul className="people-list">
            {requests.outgoing.map((r) => (
              <li className="person-row" key={r.id}>
                <button className="person-tap" onClick={() => onView(r.username)}>
                  <span className="avatar">{r.username.slice(0, 2)}</span>
                  <span className="person-main">
                    <span className="person-name">{r.username}</span>
                    <span className="person-sub">request pending</span>
                  </span>
                </button>
                <div className="person-actions">
                  <button className="btn-sm ghost" disabled={busy} onClick={() => act(() => cancelFriendRequest(r.id))}>
                    Cancel
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div>
        <h3 className="section-title">Friends</h3>
        {friends.length === 0 ? (
          <p className="empty-note">
            {nothing ? 'No friends yet. Use Find to look someone up by username.' : 'No friends yet.'}
          </p>
        ) : (
          <ul className="people-list">
            {friends.map((f) => (
              <li className="person-row" key={f.id}>
                <button className="person-tap" onClick={() => onView(f.username)}>
                  <span className="avatar">{f.username.slice(0, 2)}</span>
                  <span className="person-main">
                    <span className="person-name">{f.username}</span>
                    <span className="person-sub">{f.visibility} profile</span>
                  </span>
                </button>
                <div className="person-actions">
                  <button className="btn-sm ghost" disabled={busy} onClick={() => act(() => removeFriend(f.id))}>
                    Remove
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </>
  )
}

function FindTab({ onView, reload }: { onView: (username: string) => void; reload: () => Promise<void> }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<UserSearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  useEffect(() => {
    const q = query.trim()
    if (q.length < 1) {
      setResults([])
      return
    }
    setSearching(true)
    const handle = setTimeout(() => {
      searchUsers(q)
        .then(setResults)
        .catch(() => setResults([]))
        .finally(() => setSearching(false))
    }, 250)
    return () => clearTimeout(handle)
  }, [query])

  async function addFriend(r: UserSearchResult) {
    setBusyId(r.id)
    try {
      await sendFriendRequest(r.username)
      setResults((prev) => prev.map((x) => (x.id === r.id ? { ...x, friendshipStatus: 'pending_outgoing' } : x)))
      await reload()
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <input
        className="search-box"
        placeholder="Search by username…"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        autoComplete="off"
        autoCapitalize="off"
      />
      <div style={{ marginTop: 12 }}>
        {query.trim().length === 0 ? (
          <p className="empty-note">Type a username to find other players.</p>
        ) : searching ? (
          <p className="empty-note">Searching…</p>
        ) : results.length === 0 ? (
          <p className="empty-note">No players found.</p>
        ) : (
          <ul className="people-list">
            {results.map((r) => (
              <li className="person-row" key={r.id}>
                <button className="person-tap" onClick={() => onView(r.username)}>
                  <span className="avatar">{r.username.slice(0, 2)}</span>
                  <span className="person-main">
                    <span className="person-name">{r.username}</span>
                    <span className="person-sub">{r.visibility} profile</span>
                  </span>
                </button>
                <div className="person-actions">
                  <FindAction result={r} busy={busyId === r.id} onAdd={() => addFriend(r)} />
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

function FindAction({ result, busy, onAdd }: { result: UserSearchResult; busy: boolean; onAdd: () => void }) {
  if (result.friendshipStatus === 'friends') return <span className="status-pill">Friends</span>
  if (result.friendshipStatus === 'pending_outgoing') return <span className="status-pill">Requested</span>
  const label = result.friendshipStatus === 'pending_incoming' ? 'Accept' : 'Add'
  return (
    <button className="btn-sm primary" disabled={busy} onClick={onAdd}>
      {label}
    </button>
  )
}
