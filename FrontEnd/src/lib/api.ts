const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`)
  if (!res.ok) {
    throw new Error(`Request failed (${res.status})`)
  }
  return res.json() as Promise<T>
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
