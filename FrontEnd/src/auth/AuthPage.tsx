import { useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import { useAuth } from './AuthContext'

type Mode = 'login' | 'register'

export function AuthPage() {
  const { login, register } = useAuth()
  const [mode, setMode] = useState<Mode>('login')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

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
        await register({ username, email, password })
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
