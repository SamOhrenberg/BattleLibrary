import { useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import { decideConsent, getConsentInfo, type ConsentInfo, type ConsentRequest } from '../lib/oauth'

const READ_ACCESS = [
  'Your army lists, with units, wargear and points',
  'Your logged games and win/loss stats',
  'Live games you are part of',
  'Your friends and profiles you can already see',
  'The unit and rules catalogue',
]

// Consent screen for an app (an MCP client like Claude) asking to read this account.
export function OAuthConsent({ request, username }: { request: ConsentRequest; username: string }) {
  const [info, setInfo] = useState<ConsentInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // Refuse to render inside a frame, so the Allow button can't be clickjacked.
  const framed = window.top !== window.self

  useEffect(() => {
    if (framed) return
    getConsentInfo(request)
      .then(setInfo)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'Could not load this request.'))
  }, [request, framed])

  async function decide(approve: boolean) {
    setBusy(true)
    setError(null)
    try {
      const { redirectUrl } = await decideConsent(request, approve)
      window.location.assign(redirectUrl)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong. Try again.')
      setBusy(false)
    }
  }

  if (framed) {
    return (
      <section className="card auth">
        <p className="auth-error">Open this page directly in your browser to connect an app.</p>
      </section>
    )
  }

  return (
    <section className="card auth consent">
      <h2>Connect an app</h2>
      {!info && !error && <p className="hint">Loading…</p>}
      {info && (
        <>
          <p>
            <strong>{info.clientName}</strong> wants read-only access to your Battle Library account,{' '}
            <strong>{username}</strong>.
          </p>
          <p className="hint">It will be able to see:</p>
          <ul className="consent-scopes">
            {READ_ACCESS.map((s) => (
              <li key={s}>{s}</li>
            ))}
          </ul>
          <p className="hint">
            It cannot change anything. You will return to <strong>{info.redirectHost}</strong>. You can disconnect it
            any time from your profile.
          </p>
        </>
      )}
      {error && (
        <p className="auth-error" role="alert">
          {error}
        </p>
      )}
      <div className="consent-actions">
        <button type="button" className="btn" disabled={busy || !info} onClick={() => void decide(false)}>
          Deny
        </button>
        <button type="button" className="auth-submit" disabled={busy || !info} onClick={() => void decide(true)}>
          {busy ? 'Connecting…' : 'Allow'}
        </button>
      </div>
    </section>
  )
}
