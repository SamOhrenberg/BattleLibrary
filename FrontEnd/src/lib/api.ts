import {
  getAccessToken,
  getRefreshToken,
  setAccessToken,
  setRefreshToken,
} from './tokens'

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
    this.name = 'ApiError'
  }
}

export interface HealthStatus {
  status: string
  service: string
  timeUtc: string
}

export interface GameSystem {
  id: string
  name: string
  status: string
}

export interface UserDto {
  id: string
  username: string
  email: string
  visibility: string
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: UserDto
}

export interface RegisterRequest {
  username: string
  email: string
  password: string
}

export interface LoginRequest {
  usernameOrEmail: string
  password: string
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`)
  if (!res.ok) {
    throw new ApiError(res.status, `Request failed (${res.status})`)
  }
  return res.json() as Promise<T>
}

export async function register(request: RegisterRequest): Promise<UserDto> {
  return applySession(await postJson<AuthResponse>('/api/auth/register', request))
}

export async function login(request: LoginRequest): Promise<UserDto> {
  return applySession(await postJson<AuthResponse>('/api/auth/login', request))
}

export async function logout(): Promise<void> {
  const refreshToken = getRefreshToken()
  setAccessToken(null)
  setRefreshToken(null)
  if (!refreshToken) {
    return
  }
  try {
    await fetch(`${baseUrl}/api/auth/logout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
  } catch {
    // Best effort; tokens are already cleared locally.
  }
}

export async function restoreSession(): Promise<UserDto | null> {
  if (!getRefreshToken()) {
    return null
  }
  return refreshSession()
}

// Authenticated request helper. On a 401 it refreshes once and retries.
export async function authFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const res = await fetch(`${baseUrl}${path}`, withAuth(init))
  if (res.status !== 401 || !getRefreshToken()) {
    return res
  }
  const user = await refreshSession()
  if (!user) {
    return res
  }
  return fetch(`${baseUrl}${path}`, withAuth(init))
}

function withAuth(init: RequestInit): RequestInit {
  const headers = new Headers(init.headers)
  const token = getAccessToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  return { ...init, headers }
}

function applySession(data: AuthResponse): UserDto {
  setAccessToken(data.accessToken)
  setRefreshToken(data.refreshToken)
  return data.user
}

// One in-flight refresh at a time so concurrent 401s share a single call.
let refreshInFlight: Promise<UserDto | null> | null = null

function refreshSession(): Promise<UserDto | null> {
  refreshInFlight ??= doRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function doRefresh(): Promise<UserDto | null> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) {
    return null
  }
  try {
    const res = await fetch(`${baseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
    if (!res.ok) {
      setAccessToken(null)
      setRefreshToken(null)
      return null
    }
    return applySession((await res.json()) as AuthResponse)
  } catch {
    return null
  }
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    throw await toApiError(res)
  }
  return res.json() as Promise<T>
}

async function toApiError(res: Response): Promise<ApiError> {
  if (res.status === 401) {
    return new ApiError(401, 'Incorrect username or password.')
  }

  let message = `Request failed (${res.status})`
  try {
    const data = await res.json()
    if (data?.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors as Record<string, string[]>).flat()
      if (messages.length > 0) {
        message = messages.join(' ')
      }
    } else if (typeof data?.error === 'string') {
      message = data.error
    } else if (typeof data?.title === 'string') {
      message = data.title
    }
  } catch {
    // No or invalid JSON body; keep the generic message.
  }
  return new ApiError(res.status, message)
}
