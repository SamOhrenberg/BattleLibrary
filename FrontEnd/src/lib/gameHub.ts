import * as signalR from '@microsoft/signalr'
import { getAccessToken } from './tokens'

// Single shared SignalR connection to the game hub. The access token is read
// fresh on every (re)connect; joined session groups are re-joined after a drop.

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

let connection: signalR.HubConnection | null = null
let starting: Promise<void> | null = null
const joined = new Set<string>()

function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/game`, { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .build()
    connection.onreconnected(() => {
      joined.forEach((id) => connection?.invoke('JoinSession', id).catch(() => {}))
    })
  }
  return connection
}

async function ensureConnected(): Promise<signalR.HubConnection> {
  const conn = getConnection()
  if (conn.state === signalR.HubConnectionState.Connected) {
    return conn
  }
  starting ??= conn.start().finally(() => {
    starting = null
  })
  await starting
  return conn
}

export function onSessionUpdated(cb: (sessionId: string) => void): () => void {
  const conn = getConnection()
  conn.on('sessionUpdated', cb)
  return () => conn.off('sessionUpdated', cb)
}

export function onSessionsChanged(cb: (sessionId: string) => void): () => void {
  const conn = getConnection()
  conn.on('sessionsChanged', cb)
  return () => conn.off('sessionsChanged', cb)
}

export async function joinSessionGroup(id: string): Promise<void> {
  const conn = await ensureConnected()
  joined.add(id)
  await conn.invoke('JoinSession', id)
}

export async function leaveSessionGroup(id: string): Promise<void> {
  joined.delete(id)
  if (connection?.state === signalR.HubConnectionState.Connected) {
    try {
      await connection.invoke('LeaveSession', id)
    } catch {
      // best effort
    }
  }
}

// Connect so index-level pushes (invites, status changes) arrive without being
// inside a specific session.
export function connectHub(): void {
  void ensureConnected().catch(() => {})
}
