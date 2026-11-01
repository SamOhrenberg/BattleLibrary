import { ApiError, authFetch } from './api'

// Site-admin API (registration toggle + invites). All calls require an admin
// session; the server authorizes by the admin flag on the user.

export interface AdminSettings {
  registrationOpen: boolean
}

export interface Invite {
  id: string
  token: string
  email: string | null
  expiresAt: string | null
  maxUses: number
  useCount: number
  revoked: boolean
  usable: boolean
  createdAt: string
}

export interface CreateInviteInput {
  email?: string | null
  maxUses?: number
  expiresInDays?: number
}

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
      if (data?.errors && typeof data.errors === 'object') {
        const messages = Object.values(data.errors as Record<string, string[]>).flat()
        if (messages.length > 0) message = messages.join(' ')
      } else if (typeof data?.error === 'string') {
        message = data.error
      }
    } catch {
      // keep the generic message
    }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export function getAdminSettings(): Promise<AdminSettings> {
  return send<AdminSettings>('/api/admin/settings', 'GET')
}

export function updateAdminSettings(registrationOpen: boolean): Promise<AdminSettings> {
  return send<AdminSettings>('/api/admin/settings', 'PUT', { registrationOpen })
}

export function getInvites(): Promise<Invite[]> {
  return send<Invite[]>('/api/admin/invites', 'GET')
}

export function createInvite(input: CreateInviteInput): Promise<Invite> {
  return send<Invite>('/api/admin/invites', 'POST', input)
}

export function revokeInvite(id: string): Promise<void> {
  return send<void>(`/api/admin/invites/${id}`, 'DELETE')
}

// The shareable link for an invite, based on the current site origin.
export function inviteLink(token: string): string {
  return `${window.location.origin}${import.meta.env.BASE_URL}?invite=${token}`
}
