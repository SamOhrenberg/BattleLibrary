import { useEffect, useState } from 'react'
import { disconnectApp, getConnectedApps, mcpServerUrl, type ConnectedApp } from '../lib/oauth'
import { useToast } from '../ui/Toast'

// MCP server URL to copy, plus the apps this account has authorized.
export function ConnectedApps() {
  const [apps, setApps] = useState<ConnectedApp[] | null>(null)
  const [busy, setBusy] = useState(false)
  const toast = useToast()

  useEffect(() => {
    getConnectedApps()
      .then(setApps)
      .catch(() => setApps([]))
  }, [])

  async function copyUrl() {
    try {
      await navigator.clipboard.writeText(mcpServerUrl)
      toast.push('MCP server URL copied')
    } catch {
      toast.push('Could not copy. Select the URL and copy it instead.')
    }
  }

  async function disconnect(app: ConnectedApp) {
    if (!window.confirm(`Disconnect ${app.clientName}? It loses access right away.`)) return
    setBusy(true)
    try {
      await disconnectApp(app.id)
      setApps((list) => (list ?? []).filter((a) => a.id !== app.id))
    } catch {
      toast.push('Could not disconnect. Try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <h3 className="section-title">Connected apps</h3>
      <p className="vis-hint">
        Let an AI assistant like Claude read your lists, games and stats. Add this URL as a custom connector (MCP
        server); you will be asked to approve it here. Access is read-only.
      </p>
      <div className="mcp-url">
        <input className="search-box" readOnly value={mcpServerUrl} aria-label="MCP server URL" onFocus={(e) => e.target.select()} />
        <button type="button" className="btn-sm" onClick={() => void copyUrl()}>
          Copy
        </button>
      </div>
      {apps && apps.length > 0 && (
        <ul className="people-list">
          {apps.map((a) => (
            <li className="person-row" key={a.id}>
              <span className="person-main">
                <span className="person-name">{a.clientName}</span>
                <span className="person-sub">
                  Connected {new Date(a.connectedAt).toLocaleDateString()}
                  {a.lastUsedAt ? `, last used ${new Date(a.lastUsedAt).toLocaleDateString()}` : ''}
                </span>
              </span>
              <div className="person-actions">
                <button type="button" className="btn-sm ghost" disabled={busy} onClick={() => void disconnect(a)}>
                  Disconnect
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
