const REFRESH_KEY = 'bl.refreshToken'

// Access token lives only in memory: it is short-lived and never persisted,
// which limits exposure if storage is read by malicious script.
let accessToken: string | null = null

export const getAccessToken = (): string | null => accessToken
export const setAccessToken = (token: string | null): void => {
  accessToken = token
}

// Refresh token persists so a reload can restore the session. localStorage can
// throw or be unavailable (private windows, blocked storage), so guard access.
export function getRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_KEY)
  } catch {
    return null
  }
}

export function setRefreshToken(token: string | null): void {
  try {
    if (token) {
      localStorage.setItem(REFRESH_KEY, token)
    } else {
      localStorage.removeItem(REFRESH_KEY)
    }
  } catch {
    // Storage unavailable; the session simply won't survive a reload.
  }
}
