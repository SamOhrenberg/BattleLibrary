import { ApiError, authFetch } from './api'

export interface ConsentInfo {
  clientName: string
  redirectHost: string
  registeredAt: string
}

export interface ConsentRequest {
  clientId: string
  redirectUri: string
  codeChallenge: string
  state: string | null
}

export interface ConnectedApp {
  id: string
  clientName: string
  connectedAt: string
  lastUsedAt: string | null
}

// The MCP endpoint people paste into Claude or another MCP client.
export const mcpServerUrl = `${import.meta.env.VITE_API_BASE_URL || window.location.origin}/mcp`

// An OAuth authorization request handed over by the API's /oauth/authorize, or null.
export function readConsentRequest(): ConsentRequest | null {
  const params = new URLSearchParams(window.location.search)
  if (params.get('oauth_authorize') !== '1') return null
  const clientId = params.get('client_id')
  const redirectUri = params.get('redirect_uri')
  const codeChallenge = params.get('code_challenge')
  if (!clientId || !redirectUri || !codeChallenge) return null
  return { clientId, redirectUri, codeChallenge, state: params.get('state') }
}

export const getConsentInfo = (r: ConsentRequest) =>
  send<ConsentInfo>(
    `/api/oauth/consent?clientId=${encodeURIComponent(r.clientId)}&redirectUri=${encodeURIComponent(r.redirectUri)}`,
    'GET',
  )

// Returns the URL to send the browser back to (with a code, or an error).
export const decideConsent = (r: ConsentRequest, approve: boolean) =>
  send<{ redirectUrl: string }>('/api/oauth/consent', 'POST', { ...r, approve })

export const getConnectedApps = () => send<ConnectedApp[]>('/api/oauth/apps', 'GET')

export const disconnectApp = (id: string) => send<void>(`/api/oauth/apps/${id}`, 'DELETE')

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await authFetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      const data = await res.json()
      if (typeof data?.error === 'string') message = data.error
    } catch {
      // keep generic message
    }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
