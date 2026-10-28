import { useEffect, useState } from 'react'
import { apiGet, type HealthStatus } from './lib/api'
import { useAuth } from './auth/AuthContext'
import { AuthPage } from './auth/AuthPage'
import { ListsView } from './lists/ListsView'
import { GamesView } from './games/GamesView'
import { ProfileView } from './social/ProfileView'
import { TourOverlay } from './onboarding/TourOverlay'
import './App.css'

type Health = { state: 'loading' | 'ok' | 'error' }

function App() {
  const { user, status, logout } = useAuth()
  const [health, setHealth] = useState<Health>({ state: 'loading' })
  const [mainView, setMainView] = useState<'lists' | 'games' | 'profile'>('lists')

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
            Can’t reach the Battle Library server right now — your lists and games may not load.
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
          <nav className="segmented main-nav" aria-label="Sections">
            <button
              aria-selected={mainView === 'lists'}
              className={mainView === 'lists' ? 'active' : ''}
              onClick={() => setMainView('lists')}
            >
              Lists
            </button>
            <button
              aria-selected={mainView === 'games'}
              className={mainView === 'games' ? 'active' : ''}
              onClick={() => setMainView('games')}
            >
              Games
            </button>
          </nav>

          {mainView === 'lists' && <ListsView />}
          {mainView === 'games' && <GamesView />}
          {mainView === 'profile' && <ProfileView username={user.username} email={user.email} />}
        </>
      )}

      <TourOverlay />
    </main>
  )
}

export default App
