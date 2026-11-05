import { useEffect, useState, type ReactNode } from 'react'
import { apiGet, type HealthStatus } from './lib/api'
import { useAuth } from './auth/AuthContext'
import { AuthPage } from './auth/AuthPage'
import { ListsView } from './lists/ListsView'
import { GamesView } from './games/GamesView'
import { ProfileView } from './social/ProfileView'
import { FeedbackView } from './feedback/FeedbackView'
import { TourOverlay } from './onboarding/TourOverlay'
import './App.css'

type Health = { state: 'loading' | 'ok' | 'error' }
type MainView = 'lists' | 'games' | 'profile' | 'feedback'

const NAV: { id: MainView; label: string; icon: ReactNode }[] = [
  {
    id: 'lists',
    label: 'Lists',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <path d="M4 6h16M4 12h16M4 18h10" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    id: 'games',
    label: 'Games',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <rect x="4" y="4" width="16" height="16" rx="3" />
        <circle cx="9" cy="9" r="1.3" fill="currentColor" stroke="none" />
        <circle cx="15" cy="15" r="1.3" fill="currentColor" stroke="none" />
        <circle cx="15" cy="9" r="1.3" fill="currentColor" stroke="none" />
        <circle cx="9" cy="15" r="1.3" fill="currentColor" stroke="none" />
      </svg>
    ),
  },
  {
    id: 'profile',
    label: 'Profile',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <circle cx="12" cy="8" r="3.5" />
        <path d="M5 20a7 7 0 0114 0" strokeLinecap="round" />
      </svg>
    ),
  },
]

function App() {
  const { user, status, logout } = useAuth()
  const [health, setHealth] = useState<Health>({ state: 'loading' })
  const [mainView, setMainView] = useState<MainView>('lists')

  useEffect(() => {
    apiGet<HealthStatus>('/api/health')
      .then(() => setHealth({ state: 'ok' }))
      .catch(() => setHealth({ state: 'error' }))
  }, [])

  return (
    <main className="app">
      <header className="app-header">
        <div>
          <p className="eyebrow">Tabletop wargaming companion</p>
          <h1>The Battle Library</h1>
        </div>
        {status === 'authenticated' && user && (
          <div className="account-bar">
            <button
              type="button"
              className="account-name"
              onClick={() => setMainView('profile')}
              title="View your profile"
            >
              {user.username}
            </button>
            <button type="button" className="link-button" onClick={() => setMainView('feedback')}>
              Feedback
            </button>
            <button type="button" className="link-button" onClick={() => void logout()}>
              Sign out
            </button>
          </div>
        )}
      </header>

      {health.state === 'error' && (
        <div className="conn-banner" role="alert">
          <span className="dot" />
          <div>
            Can’t reach the Battle Library server right now - your lists and games may not load.
            This usually clears up on its own; try again in a moment.
            {import.meta.env.DEV && (
              <p className="hint">
                Dev: start the API with <code>dotnet run</code> and check{' '}
                <code>VITE_API_BASE_URL</code>.
              </p>
            )}
          </div>
        </div>
      )}

      {status === 'loading' && <p className="hint">Restoring session…</p>}

      {status === 'anonymous' && <AuthPage />}

      {status === 'authenticated' && user && (
        <>
          <nav className="segmented main-nav top" aria-label="Sections">
            {NAV.map((n) => (
              <button
                key={n.id}
                aria-selected={mainView === n.id}
                className={mainView === n.id ? 'active' : ''}
                onClick={() => setMainView(n.id)}
              >
                {n.label}
              </button>
            ))}
          </nav>

          {mainView === 'lists' && <ListsView />}
          {mainView === 'games' && <GamesView />}
          {mainView === 'profile' && <ProfileView username={user.username} email={user.email} />}
          {mainView === 'feedback' && <FeedbackView />}

          <nav className="bottom-nav" aria-label="Sections">
            <div className="bottom-nav-in">
              {NAV.map((n) => (
                <button
                  key={n.id}
                  aria-current={mainView === n.id ? 'page' : undefined}
                  className={mainView === n.id ? 'active' : ''}
                  onClick={() => setMainView(n.id)}
                >
                  {n.icon}
                  <span>{n.label}</span>
                </button>
              ))}
            </div>
          </nav>
        </>
      )}

      <TourOverlay />
    </main>
  )
}

export default App
