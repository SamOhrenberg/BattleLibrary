import { useEffect, useState } from 'react'
import { getMySessions } from '../lib/gameSessions'
import { connectHub, onSessionsChanged } from '../lib/gameHub'

// Count of live games the signed-in user is invited to but hasn't answered. Refreshes on hub pushes.
export function useGameInvites(enabled: boolean): number {
  const [count, setCount] = useState(0)

  useEffect(() => {
    if (!enabled) return
    let active = true
    const load = () =>
      getMySessions()
        .then((s) => active && setCount(s.filter((x) => x.myStatus === 'Invited').length))
        .catch(() => {})
    void load()
    connectHub()
    const off = onSessionsChanged(() => void load())
    return () => {
      active = false
      off()
    }
  }, [enabled])

  return enabled ? count : 0
}
