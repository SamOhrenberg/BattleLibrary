import { useEffect, useState } from 'react'
import { apiGet, type HealthStatus } from './lib/api'
import { useAuth } from './auth/AuthContext'
import { AuthPage } from './auth/AuthPage'
import { ListsView } from './lists/ListsView'
import { GamesView } from './games/GamesView'
import { CommunityView } from './social/CommunityView'
import { TourOverlay } from './onboarding/TourOverlay'
import './App.css'

type Health = { state: 'loading' | 'ok' | 'error'; detail?: string }

function App() {
  const { user, status, logout } = useAuth()
  const [health, setHealth] = useState<Health>({ state: 'loading' })
  const [mainView, setMainView] = useState<'lists' | 'games' | 'community'>('lists')

  useEffect(() => {
    apiGet<HealthStatus>('/api/health')
      .then((h) => setHealth({ state: 'ok', detail: h.service }))
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
            <span className="account-name">{user.username}</span>
            <button type="button" className="link-button" onClick={() => void logout()}>
              Sign out
            </button>
          </div>
        )}
      </header>

      <section className="card">
        <h2>API status</h2>
        <p className={`status status-${health.state}`}>
          <span className="dot" />
          {health.state === 'loading' && 'Checking…'}
          {health.state === 'ok' && `Connected — ${health.detail}`}
          {health.state === 'error' && 'Unreachable'}
        </p>
        {health.state === 'error' && (
          <p className="hint">
            Start the API with <code>dotnet run</code> and check{' '}
            <code>VITE_API_BASE_URL</code>.
          </p>
        )}
      </section>

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
            <button
              aria-selected={mainView === 'community'}
              className={mainView === 'community' ? 'active' : ''}
              onClick={() => setMainView('community')}
            >
              Community
            </button>
          </nav>

          {mainView === 'lists' && <ListsView />}
          {mainView === 'games' && <GamesView />}
          {mainView === 'community' && <CommunityView email={user.email} />}
        </>
      )}

      <TourOverlay />
    </main>
  )
}

export default App
