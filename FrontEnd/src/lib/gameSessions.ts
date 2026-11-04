import { ApiError, authFetch } from './api'

// Live multiplayer game sessions. Every mutating call returns the caller's view
// of the full session; real-time pushes (see gameHub) tell clients to refetch.

export type SessionStatus = 'Setup' | 'Active' | 'Finished' | 'Abandoned'
export type ParticipantStatus = 'Invited' | 'Accepted' | 'Declined' | 'Left'
export type ActionKind = 'Ability' | 'Stratagem'

export interface SessionUnit {
  id: string
  datasheetId: string
  name: string
  maxModels: number
  startingModels: number
  currentModels: number
  destroyed: boolean
  keywords: string[]
  isWarlord: boolean
  enhancementName: string | null
  points: number
  wargear: string[]
}

export interface SessionSecondary {
  name: string
  score: number
}

export interface SessionRound {
  roundNumber: number
  primaryScore: number
  cpGained: number
  cpSpent: number
  secondaries: SessionSecondary[]
  secondaryTotal: number
  roundTotal: number
}

export interface SessionAction {
  id: string
  roundNumber: number
  kind: ActionKind
  name: string
  cpCost: number | null
  note: string | null
}

export interface SessionParticipant {
  id: string
  userId: string
  username: string
  status: ParticipantStatus
  seat: number
  armyListId: string | null
  factionName: string | null
  isMe: boolean
  totalScore: number
  currentCp: number
  unitsTotal: number
  unitsDestroyed: number
  units: SessionUnit[]
  rounds: SessionRound[]
  actions: SessionAction[]
  // Setup-only: why this player's chosen list can't be used, else null.
  listIssue: string | null
}

export interface GameSession {
  id: string
  name: string | null
  gameSystemKey: string
  pointsLimit: number | null
  battleSize: string | null
  startingCp: number
  status: SessionStatus
  currentRound: number
  // The standard battle-round count this game runs to (4 for Kill Team, 5 for
  // 40k). The host advances up to this cap and can finish earlier to end short.
  maxRounds: number
  createdByUserId: string
  isHost: boolean
  maxParticipants: number
  startedAt: string | null
  finishedAt: string | null
  participants: SessionParticipant[]
}

export interface SessionSummary {
  id: string
  name: string | null
  gameSystemKey: string
  pointsLimit: number | null
  status: SessionStatus
  currentRound: number
  isHost: boolean
  myStatus: ParticipantStatus
  participantCount: number
  createdAt: string
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

const base = '/api/game-sessions'

export const getMySessions = () => send<SessionSummary[]>(base, 'GET')

export const getSession = (id: string) => send<GameSession>(`${base}/${id}`, 'GET')

export const createSession = (body: {
  name: string | null
  gameSystemKey: string | null
  pointsLimit: number | null
  battleSize: string | null
  startingCp: number
}) => send<GameSession>(base, 'POST', body)

export const inviteToSession = (id: string, username: string) =>
  send<GameSession>(`${base}/${id}/invite`, 'POST', { username })

export const respondToSession = (id: string, accept: boolean) =>
  send<GameSession>(`${base}/${id}/respond`, 'POST', { accept })

export const setSessionList = (id: string, listId: string | null) =>
  send<GameSession>(`${base}/${id}/list`, 'POST', { listId })

export const leaveSession = (id: string) => send<void>(`${base}/${id}/leave`, 'POST')

export const startSession = (id: string) => send<GameSession>(`${base}/${id}/start`, 'POST')

export const advanceSession = (id: string) => send<GameSession>(`${base}/${id}/advance`, 'POST')

export const setSessionRound = (
  id: string,
  round: number,
  body: { primaryScore: number; cpGained: number; secondaries: SessionSecondary[] },
) => send<GameSession>(`${base}/${id}/rounds/${round}`, 'PUT', body)

export const updateSessionUnit = (
  id: string,
  unitId: string,
  body: { startingModels?: number; currentModels?: number; destroyed?: boolean },
) => send<GameSession>(`${base}/${id}/units/${unitId}`, 'PUT', body)

export const logSessionAction = (
  id: string,
  body: { roundNumber: number | null; kind: ActionKind; name: string; cpCost: number | null; note: string | null },
) => send<GameSession>(`${base}/${id}/actions`, 'POST', body)

export const deleteSessionAction = (id: string, actionId: string) =>
  send<GameSession>(`${base}/${id}/actions/${actionId}`, 'DELETE')

export const finishSession = (id: string) => send<GameSession>(`${base}/${id}/finish`, 'POST')

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
