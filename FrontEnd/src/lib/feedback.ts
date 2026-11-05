import { ApiError, authFetch } from './api'

export type FeedbackKind = 'Bug' | 'Idea' | 'Data' | 'Other'

export type FeedbackStatus = 'open' | 'done' | 'not_planned' | 'duplicate'

// Mirrors FeedbackEndpoints.Areas on the server.
export const FEEDBACK_AREAS = [
  'List building (40k)',
  'Kill Team rosters',
  'Live games',
  'Game history',
  'Friends and profiles',
  'Sign in / registration',
  'Import / export',
  'Rule search',
  'Other',
] as const

export interface FeedbackSummary {
  id: string
  kind: FeedbackKind
  title: string
  area: string | null
  issueNumber: number
  issueUrl: string
  status: FeedbackStatus
  labels: string[]
  commentCount: number
  createdAt: string
  updatedAt: string
}

export interface FeedbackList {
  enabled: boolean
  reports: FeedbackSummary[]
}

export interface FeedbackComment {
  id: number
  author: string
  isMaintainer: boolean
  body: string
  createdAt: string
}

export interface FeedbackDetail {
  report: FeedbackSummary
  description: string | null
  comments: FeedbackComment[]
  live: boolean
}

export interface SubmitFeedback {
  kind: FeedbackKind
  title: string
  description: string
  area: string | null
  device: string | null
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
  return res.json() as Promise<T>
}

export const getMyFeedback = () => send<FeedbackList>('/api/feedback', 'GET')

export const getFeedback = (id: string) => send<FeedbackDetail>(`/api/feedback/${id}`, 'GET')

export const submitFeedback = (request: SubmitFeedback) =>
  send<FeedbackSummary>('/api/feedback', 'POST', request)

async function toError(res: Response): Promise<ApiError> {
  let message = `Request failed (${res.status})`
  try {
    const data = await res.json()
    if (data?.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors as Record<string, string[]>).flat()
      if (messages.length > 0) message = messages.join(' ')
    } else if (typeof data?.detail === 'string') {
      message = data.detail
    } else if (typeof data?.title === 'string') {
      message = data.title
    }
  } catch {
    // keep generic message
  }
  return new ApiError(res.status, message)
}
