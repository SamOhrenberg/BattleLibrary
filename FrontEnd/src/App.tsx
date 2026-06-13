import { useEffect, useState } from 'react'
import { apiGet, type GameSystem, type HealthStatus } from './lib/api'
import './App.css'

type Health = { state: 'loading' | 'ok' | 'error'; detail?: string }

function App() {
  const [health, setHealth] = useState<Health>({ state: 'loading' })
  const [systems, setSystems] = useState<GameSystem[]>([])

  useEffect(() => {
    apiGet<HealthStatus>('/api/health')
      .then((h) => setHealth({ state: 'ok', detail: h.service }))
      .catch(() => setHealth({ state: 'error' }))

    apiGet<GameSystem[]>('/api/game-systems')
      .then(setSystems)
      .catch(() => setSystems([]))
  }, [])

  return (
    <main className="app">
      <header>
        <p className="eyebrow">Tabletop wargaming companion</p>
        <h1>The Battle Library</h1>
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

      <section className="card">
        <h2>Game systems</h2>
        {systems.length === 0 ? (
          <p className="hint">None loaded yet.</p>
        ) : (
          <ul className="systems">
            {systems.map((s) => (
              <li key={s.id}>
                <span>{s.name}</span>
                <span className={`badge badge-${s.status}`}>{s.status}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  )
}

export default App
