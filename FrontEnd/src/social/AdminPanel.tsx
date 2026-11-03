import { useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import {
  createInvite,
  getAdminSettings,
  getInvites,
  inviteLink,
  revokeInvite,
  updateAdminSettings,
  type Invite,
} from '../lib/admin'
import { useAuth } from '../auth/AuthContext'

// Site administration, shown on the Profile page for admins only: toggle open
// sign-ups and manage invites (email-bound or shareable links).
export function AdminPanel() {
  const { user } = useAuth()
  const [regOpen, setRegOpen] = useState<boolean | null>(null)
  const [invites, setInvites] = useState<Invite[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  // New-invite form.
  const [inviteEmail, setInviteEmail] = useState('')
  const [maxUses, setMaxUses] = useState('1')
  const [expiresDays, setExpiresDays] = useState('')

  const isAdmin = user?.isAdmin === true

  useEffect(() => {
    if (!isAdmin) return
    getAdminSettings()
      .then((s) => setRegOpen(s.registrationOpen))
      .catch(() => setError('Could not load admin settings.'))
    getInvites()
      .then(setInvites)
      .catch(() => setError('Could not load invites.'))
  }, [isAdmin])

  if (!isAdmin) return null

  async function toggleRegistration() {
    if (regOpen === null) return
    setBusy(true)
    setError(null)
    try {
      const next = await updateAdminSettings(!regOpen)
      setRegOpen(next.registrationOpen)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update registration.')
    } finally {
      setBusy(false)
    }
  }

  async function addInvite() {
    setBusy(true)
    setError(null)
    try {
      const created = await createInvite({
        email: inviteEmail.trim() || null,
        maxUses: maxUses.trim() === '' ? 1 : Number(maxUses),
        expiresInDays: expiresDays.trim() === '' ? undefined : Number(expiresDays),
      })
      setInvites((prev) => [created, ...prev])
      setInviteEmail('')
      setMaxUses('1')
      setExpiresDays('')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the invite.')
    } finally {
      setBusy(false)
    }
  }

  async function revoke(id: string) {
    setBusy(true)
    setError(null)
    try {
      await revokeInvite(id)
      setInvites((prev) => prev.map((i) => (i.id === id ? { ...i, revoked: true, usable: false } : i)))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not revoke the invite.')
    } finally {
      setBusy(false)
    }
  }

  async function copyLink(token: string, id: string) {
    try {
      await navigator.clipboard.writeText(inviteLink(token))
      setCopiedId(id)
      setTimeout(() => setCopiedId((c) => (c === id ? null : c)), 1600)
    } catch {
      setError('Could not copy the link.')
    }
  }

  return (
    <div className="admin-panel">
      <h3 className="section-title">Admin</h3>

      <div className="admin-row">
        <div>
          <div className="admin-row-title">Open sign-ups</div>
          <p className="vis-hint">
            {regOpen
              ? 'Anyone can create an account.'
              : 'Invite-only: only people with an invite (or the seeded admin) can register.'}
          </p>
        </div>
        <button
          type="button"
          className={`toggle${regOpen ? ' on' : ''}`}
          role="switch"
          aria-checked={regOpen === true}
          disabled={busy || regOpen === null}
          onClick={toggleRegistration}
        >
          <span className="toggle-knob" />
        </button>
      </div>

      <div className="admin-invite-new">
        <div className="admin-row-title">Create an invite</div>
        <p className="vis-hint">Leave the email blank for a shareable link anyone can use.</p>
        <div className="admin-invite-fields">
          <input
            className="search-box"
            type="email"
            placeholder="Email (optional)"
            value={inviteEmail}
            onChange={(e) => setInviteEmail(e.target.value)}
            autoComplete="off"
          />
          <label className="admin-mini">
            Uses (0 = unlimited)
            <input
              inputMode="numeric"
              value={maxUses}
              onChange={(e) => setMaxUses(e.target.value.replace(/[^0-9]/g, ''))}
            />
          </label>
          <label className="admin-mini">
            Expires (days)
            <input
              inputMode="numeric"
              placeholder="never"
              value={expiresDays}
              onChange={(e) => setExpiresDays(e.target.value.replace(/[^0-9]/g, ''))}
            />
          </label>
          <button type="button" className="btn btn-primary" onClick={addInvite} disabled={busy}>
            Create
          </button>
        </div>
      </div>

      {error && <p className="builder-error">{error}</p>}

      <div className="admin-invites">
        {invites.length === 0 ? (
          <p className="hint">No invites yet.</p>
        ) : (
          invites.map((i) => (
            <div className={`admin-invite${i.usable ? '' : ' spent'}`} key={i.id}>
              <div className="admin-invite-main">
                <div className="admin-invite-label">{i.email ?? 'Shareable link'}</div>
                <div className="admin-invite-meta">
                  {i.revoked
                    ? 'Revoked'
                    : i.usable
                      ? `${i.maxUses === 0 ? 'Unlimited' : `${i.useCount}/${i.maxUses} used`}${i.expiresAt ? ` · expires ${new Date(i.expiresAt).toLocaleDateString()}` : ''}`
                      : 'Used up or expired'}
                </div>
              </div>
              <div className="admin-invite-actions">
                <button type="button" className="btn-sm" onClick={() => copyLink(i.token, i.id)}>
                  {copiedId === i.id ? 'Copied ✓' : 'Copy link'}
                </button>
                {!i.revoked && (
                  <button type="button" className="btn-sm btn-danger" onClick={() => revoke(i.id)} disabled={busy}>
                    Revoke
                  </button>
                )}
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  )
}
