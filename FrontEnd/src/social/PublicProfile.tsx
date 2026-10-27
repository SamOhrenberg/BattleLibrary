import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import {
  getPublicProfile,
  removeFriend,
  sendFriendRequest,
  type PublicProfile as Profile,
} from '../lib/social'

interface Props {
  username: string
  onBack: () => void
  onChanged: () => void
}

export function PublicProfile({ username, onBack, onChanged }: Props) {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    getPublicProfile(username)
      .then(setProfile)
      .catch(() => setError('Could not load this profile.'))
  }, [username])

  useEffect(() => {
    setProfile(null)
    setError(null)
    load()
  }, [load])

  async function act(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      load()
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card">
      <button type="button" className="back-link" onClick={onBack}>
        ← Back
      </button>

      {error && <p className="builder-error">{error}</p>}
      {!profile ? (
        <p className="hint">Loading…</p>
      ) : (
        <>
          <div className="pub-head">
            <div className="avatar">{profile.username.slice(0, 2)}</div>
            <div style={{ minWidth: 0, flex: 1 }}>
              <div className="pub-name">{profile.username}</div>
              <div className="person-sub">{profile.visibility} profile</div>
            </div>
            <FriendAction profile={profile} busy={busy} act={act} />
          </div>

          {profile.friendshipStatus !== 'self' && profile.bio && <p className="pub-bio">{profile.bio}</p>}

          {!profile.canViewLists ? (
            <div className="pub-private">
              This profile is private. Add {profile.username} as a friend to see their lists.
            </div>
          ) : (
            <div style={{ marginTop: 18 }}>
              <h3 className="section-title">Army lists</h3>
              {profile.lists.length === 0 ? (
                <p className="empty-note">No lists yet.</p>
              ) : (
                <ul className="list-index">
                  {profile.lists.map((l) => (
                    <li key={l.id} style={{ cursor: 'default' }}>
                      <div>
                        <div className="li-name">
                          {l.name}
                          {l.featured && <span className="li-star" aria-label="Highlighted" title="Highlighted">★</span>}
                        </div>
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
              )}
            </div>
          )}
        </>
      )}
    </section>
  )
}

function FriendAction({
  profile,
  busy,
  act,
}: {
  profile: Profile
  busy: boolean
  act: (action: () => Promise<unknown>) => void
}) {
  if (profile.friendshipStatus === 'self') return null
  if (profile.friendshipStatus === 'friends') {
    return (
      <button type="button" className="btn-sm" disabled={busy} onClick={() => act(() => removeFriend(profile.id))}>
        Friends ✓
      </button>
    )
  }
  if (profile.friendshipStatus === 'pending_outgoing') {
    return <span className="status-pill">Requested</span>
  }
  const label = profile.friendshipStatus === 'pending_incoming' ? 'Accept' : 'Add friend'
  return (
    <button
      type="button"
      className="btn-sm primary"
      disabled={busy}
      onClick={() => act(() => sendFriendRequest(profile.username))}
    >
      {label}
    </button>
  )
}
