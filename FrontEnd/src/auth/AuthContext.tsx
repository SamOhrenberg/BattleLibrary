import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import {
  login as apiLogin,
  logout as apiLogout,
  register as apiRegister,
  restoreSession,
  type LoginRequest,
  type RegisterRequest,
  type UserDto,
} from '../lib/api'

type Status = 'loading' | 'authenticated' | 'anonymous'

interface AuthContextValue {
  user: UserDto | null
  status: Status
  login: (request: LoginRequest) => Promise<void>
  register: (request: RegisterRequest) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null)
  const [status, setStatus] = useState<Status>('loading')

  useEffect(() => {
    let active = true
    restoreSession()
      .then((restored) => {
        if (!active) return
        setUser(restored)
        setStatus(restored ? 'authenticated' : 'anonymous')
      })
      .catch(() => {
        if (!active) return
        setUser(null)
        setStatus('anonymous')
      })
    return () => {
      active = false
    }
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      status,
      login: async (request) => {
        setUser(await apiLogin(request))
        setStatus('authenticated')
      },
      register: async (request) => {
        setUser(await apiRegister(request))
        setStatus('authenticated')
      },
      logout: async () => {
        await apiLogout()
        setUser(null)
        setStatus('anonymous')
      },
    }),
    [user, status],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return ctx
}
