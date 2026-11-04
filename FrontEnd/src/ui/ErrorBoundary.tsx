import { Component, type ErrorInfo, type ReactNode } from 'react'

interface State {
  failed: boolean
}

export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { failed: false }

  static getDerivedStateFromError(): State {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Render error', error, info.componentStack)
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <div role="alert" style={{ padding: '2rem 1rem', textAlign: 'center', color: 'var(--fg)' }}>
        <h1 style={{ fontSize: '1.25rem' }}>Something went wrong</h1>
        <p>The page hit an unexpected error.</p>
        <button type="button" onClick={() => window.location.reload()} style={{ minHeight: 44, padding: '0 1.25rem' }}>
          Reload
        </button>
      </div>
    )
  }
}
