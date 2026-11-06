import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import { useToast } from '../ui/Toast'
import { AdminPanel } from './AdminPanel'
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
import { THEMES, applyTheme, getTheme } from '../styles/theme'
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
          Friends{friends.length > 0 ? ` (${friends.length})` : ''}
          {requests.incoming.length > 0 && (
            <span className="tab-badge" aria-label={`${requests.incoming.length} pending requests`}>
              {requests.incoming.length}
            </span>
          )}
        </button>
        <button role="tab" aria-selected={tab === 'find'} className={tab === 'find' ? 'active' : ''} onClick={() => setTab('find')}>
          Find
        </button>
      </div>

      {error && <p className="builder-error">{error}</p>}

      <div className="community-body">
        {tab === 'profile' && <ProfileTab username={username} email={email} onPreview={() => setViewing(username)} />}
        {tab === 'friends' && (
          <FriendsTab friends={friends} requests={requests} onView={setViewing} reload={reload} onFind={() => setTab('find')} />
        )}
        {tab === 'find' && <FindTab onView={setViewing} reload={reload} />}
      </div>
    </section>
  )
}

function ProfileTab({ username, email, onPreview }: { username: string; email: string; onPreview: () => void }) {
  const [visibility, setVisibility] = useState('Private')
  const [bio, setBio] = useState('')
  const [savedState, setSavedState] = useState({ visibility: 'Private', bio: '' })
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getProfile()
      .then((p) => {
        setVisibility(p.visibility)
        setBio(p.bio ?? '')
        setSavedState({ visibility: p.visibility, bio: p.bio ?? '' })
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
      setSavedState({ visibility: p.visibility, bio: p.bio ?? '' })
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  const dirty = visibility !== savedState.visibility || bio.trim() !== savedState.bio.trim()

  function choosePublic() {
    if (visibility === 'Public') return
    const ok = window.confirm(
      'Make your profile public? Anyone signed in can then see your username, bio and army lists. Your email is never shown.',
    )
    if (ok) setVisibility('Public')
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

      <ThemeSelector />

      <div>
        <h3 className="section-title">Profile visibility</h3>
        <div className="segmented" role="radiogroup" aria-label="Profile visibility">
          <button
            role="radio"
            aria-checked={visibility === 'Private'}
            className={visibility === 'Private' ? 'active' : ''}
            onClick={() => setVisibility('Private')}
          >
            Private
          </button>
          <button
            role="radio"
            aria-checked={visibility === 'Public'}
            className={visibility === 'Public' ? 'active' : ''}
            onClick={choosePublic}
          >
            Public
          </button>
        </div>
        <p className="vis-hint">
          {visibility === 'Public'
            ? 'Anyone signed in can view your profile and army lists.'
            : 'Only you and your friends can see your profile and lists.'}
          {visibility !== savedState.visibility ? ' Press Save profile to apply.' : ''}
        </p>
      </div>

      <label>
        Bio
        <textarea
          value={bio}
          onChange={(e) => setBio(e.target.value)}
          maxLength={500}
          aria-describedby="bio-count"
          placeholder="A line about you, your armies, your local scene…"
        />
        <span className="char-count" id="bio-count">{bio.length} / 500</span>
      </label>

      <label>
        Email
        <input className="search-box" type="email" value={email} disabled />
        <span className="char-count">Never shown on your profile.</span>
      </label>

      {error && <p className="builder-error">{error}</p>}

      <button type="button" className="btn btn-primary" onClick={save} disabled={busy || saved || !dirty}>
        {busy ? 'Saving…' : saved ? 'Saved ✓' : dirty ? 'Save profile' : 'No changes'}
      </button>

      <HighlightLists isPublic={savedState.visibility === 'Public'} />
      <AdminPanel />
    </div>
  )
}

// Theme picker. Applies instantly and remembers the choice on this device.
function ThemeSelector() {
  const [theme, setTheme] = useState(getTheme)

  function pick(id: string) {
    applyTheme(id)
    setTheme(id)
  }

  return (
    <div>
      <h3 className="section-title">Appearance</h3>
      <p className="vis-hint">Applies instantly. Saved on this device only.</p>
      <div className="theme-grid" role="radiogroup" aria-label="Theme">
        {THEMES.map((t) => (
          <button
            key={t.id}
            type="button"
            role="radio"
            className={`theme-card${theme === t.id ? ' on' : ''}`}
            aria-checked={theme === t.id}
            onClick={() => pick(t.id)}
          >
            <span className="theme-sw" style={{ background: t.swatch }} aria-hidden="true" />
            <span className="theme-meta">
              <span className="theme-name">{t.name}</span>
              <span className="theme-blurb">{t.blurb}</span>
            </span>
            {theme === t.id && <span className="theme-check" aria-hidden="true">✓</span>}
          </button>
        ))}
      </div>
    </div>
  )
}

// Pick which army lists are highlighted (shown first) on the public profile.
function HighlightLists({ isPublic }: { isPublic: boolean }) {
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
      <p className="vis-hint">
        {isPublic
          ? 'Starred lists appear first on your profile.'
          : 'Starred lists appear first on your profile for friends, and for everyone if you go public.'}
      </p>
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
  onFind,
}: {
  friends: Friend[]
  requests: FriendRequests
  onView: (username: string) => void
  reload: () => Promise<void>
  onFind: () => void
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
          <div className="empty-note">
            <p>{nothing ? 'No friends yet. Look someone up by username.' : 'No friends yet.'}</p>
            <button type="button" className="btn btn-primary" onClick={onFind}>Find players</button>
          </div>
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
  const toast = useToast()

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
      const accepted = r.friendshipStatus === 'pending_incoming'
      setResults((prev) =>
        prev.map((x) => (x.id === r.id ? { ...x, friendshipStatus: accepted ? 'friends' : 'pending_outgoing' } : x)),
      )
      toast.push(
        accepted ? `You and ${r.username} are now friends.` : `Request sent to ${r.username}. Cancel it from Friends > Sent.`,
        'success',
      )
      await reload()
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not send the request.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <div className="search-wrap">
        <span className="search-icon" aria-hidden="true">⌕</span>
        <input
          className="search-box"
          type="search"
          enterKeyHint="search"
          aria-label="Search players by username"
          placeholder="Search by username"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          autoComplete="off"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
        />
        {query && (
          <button type="button" className="search-clear" aria-label="Clear search" onClick={() => setQuery('')}>
            ×
          </button>
        )}
      </div>
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
  if (result.friendshipStatus === 'pending_outgoing') return <span className="status-pill pending">Requested</span>
  const label = result.friendshipStatus === 'pending_incoming' ? 'Accept' : 'Add friend'
  return (
    <button className="btn-sm primary" disabled={busy} onClick={onAdd}>
      {label}
    </button>
  )
}
