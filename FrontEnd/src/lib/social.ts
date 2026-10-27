import { ApiError, authFetch } from './api'

export type FriendshipStatus =
  | 'self'
  | 'none'
  | 'pending_outgoing'
  | 'pending_incoming'
  | 'friends'

export interface ProfileSettings {
  visibility: string
  bio: string | null
}

export interface Friend {
  id: string
  username: string
  visibility: string
}

export interface FriendRequest {
  id: string
  userId: string
  username: string
  direction: 'incoming' | 'outgoing'
}

export interface FriendRequests {
  incoming: FriendRequest[]
  outgoing: FriendRequest[]
}

export interface UserSearchResult {
  id: string
  username: string
  visibility: string
  friendshipStatus: FriendshipStatus
}

export interface PublicList {
  id: string
  name: string
  gameSystemKey: string
  factionName: string
  pointsLimit: number | null
  total: number
  entryCount: number
  featured: boolean
  updatedAt: string
}

export interface PublicProfile {
  id: string
  username: string
  visibility: string
  bio: string | null
  friendshipStatus: FriendshipStatus
  canViewLists: boolean
  lists: PublicList[]
}

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await authFetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    throw await toError(res)
  }
  if (res.status === 204) {
    return undefined as T
  }
  return res.json() as Promise<T>
}

export const getProfile = () => send<ProfileSettings>('/api/profile', 'GET')

export const updateProfile = (visibility: string, bio: string | null) =>
  send<ProfileSettings>('/api/profile', 'PUT', { visibility, bio })

export const searchUsers = (q: string) =>
  send<UserSearchResult[]>(`/api/users/search?q=${encodeURIComponent(q)}`, 'GET')

export const getPublicProfile = (username: string) =>
  send<PublicProfile>(`/api/users/${encodeURIComponent(username)}`, 'GET')

export const getFriends = () => send<Friend[]>('/api/friends', 'GET')

export const getFriendRequests = () => send<FriendRequests>('/api/friends/requests', 'GET')

export const sendFriendRequest = (username: string) =>
  send<{ status: FriendshipStatus }>('/api/friends/requests', 'POST', { username })

export const acceptFriendRequest = (id: string) =>
  send<void>(`/api/friends/requests/${id}/accept`, 'POST')

export const cancelFriendRequest = (id: string) =>
  send<void>(`/api/friends/requests/${id}`, 'DELETE')

export const removeFriend = (userId: string) =>
  send<void>(`/api/friends/${userId}`, 'DELETE')

async function toError(res: Response): Promise<ApiError> {
  let message = `Request failed (${res.status})`
  try {
    const data = await res.json()
    if (data?.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors as Record<string, string[]>).flat()
      if (messages.length > 0) message = messages.join(' ')
    } else if (typeof data?.error === 'string') {
      message = data.error
    } else if (typeof data?.title === 'string') {
      message = data.title
    }
  } catch {
    // keep generic message
  }
  return new ApiError(res.status, message)
}
