import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, getRegistrationStatus } from '../lib/api'
import { useAuth } from './AuthContext'

type Mode = 'login' | 'register'

// An invite link lands as ?invite=TOKEN; prefill the code and open registration.
const initialInvite = new URLSearchParams(window.location.search).get('invite') ?? ''

export function AuthPage() {
  const { login, register } = useAuth()
  const [mode, setMode] = useState<Mode>(initialInvite ? 'register' : 'login')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // null while loading; true/false once the public status is known.
  const [regOpen, setRegOpen] = useState<boolean | null>(null)

  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [invite, setInvite] = useState(initialInvite)

  useEffect(() => {
    getRegistrationStatus()
      .then((s) => setRegOpen(s.open))
      .catch(() => setRegOpen(null))
  }, [])

  function switchMode(next: Mode) {
    setMode(next)
    setError(null)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      if (mode === 'login') {
        await login({ usernameOrEmail, password })
      } else {
        await register({ username, email, password, inviteToken: invite.trim() || undefined })
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card auth">
      <div className="auth-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'login'}
          className={mode === 'login' ? 'active' : ''}
          onClick={() => switchMode('login')}
        >
          Sign in
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'register'}
          className={mode === 'register' ? 'active' : ''}
          onClick={() => switchMode('register')}
        >
          Create account
        </button>
      </div>

      <form className="auth-form" onSubmit={onSubmit}>
        {mode === 'login' ? (
          <label>
            Username or email
            <input
              id="login-identifier"
              value={usernameOrEmail}
              onChange={(e) => setUsernameOrEmail(e.target.value)}
              autoComplete="username"
              required
            />
          </label>
        ) : (
          <>
            <label>
              Username
              <input
                id="register-username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                minLength={3}
                maxLength={50}
                required
              />
            </label>
            <label>
              Email
              <input
                id="register-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="email"
                required
              />
            </label>
            {regOpen === false && (
              <p className="auth-note">
                Sign-ups are invite-only right now. Use your invite link, or paste your invite code below.
              </p>
            )}
            <label>
              Invite code {regOpen ? '(optional)' : ''}
              <input
                id="register-invite"
                value={invite}
                onChange={(e) => setInvite(e.target.value)}
                placeholder={regOpen ? 'Leave blank if you have none' : 'Required unless you were seeded as admin'}
                autoComplete="off"
              />
            </label>
          </>
        )}

        <label>
          Password
          <input
            id={`${mode}-password`}
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            minLength={8}
            required
          />
        </label>

        {error && <p className="auth-error" role="alert">{error}</p>}

        <button type="submit" className="auth-submit" disabled={busy}>
          {busy ? 'Working…' : mode === 'login' ? 'Sign in' : 'Create account'}
        </button>
      </form>
    </section>
  )
}
