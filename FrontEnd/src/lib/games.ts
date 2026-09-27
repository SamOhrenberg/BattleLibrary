import { ApiError, authFetch } from './api'

// Game tracking. The opponent is either a registered user (opponentUserId) or a
// free-text name (opponentName) for a stranger.

export type GameResult = 'Win' | 'Loss' | 'Draw'

export interface Game {
  id: string
  playedOn: string
  gameSystemKey: string
  result: GameResult
  opponentUserId: string | null
  opponentName: string
  playerFactionName: string | null
  opponentFactionName: string | null
  playerListId: string | null
  playerListName: string | null
  playerScore: number | null
  opponentScore: number | null
  notes: string | null
  gameSessionId: string | null
}

export interface GameStats {
  total: number
  wins: number
  losses: number
  draws: number
  winRate: number
}

export interface RecordGameInput {
  playedOn: string | null
  gameSystemKey: string | null
  result: GameResult
  opponentUserId: string | null
  opponentName: string | null
  playerFactionName: string | null
  opponentFactionName: string | null
  playerListId: string | null
  playerScore: number | null
  opponentScore: number | null
  notes: string | null
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

export const getGames = () => send<Game[]>('/api/games', 'GET')

export const getGameStats = () => send<GameStats>('/api/games/stats', 'GET')

export const recordGame = (input: RecordGameInput) => send<Game>('/api/games', 'POST', input)

export const updateGame = (id: string, input: RecordGameInput) =>
  send<Game>(`/api/games/${id}`, 'PUT', input)

export const deleteGame = (id: string) => send<void>(`/api/games/${id}`, 'DELETE')

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
