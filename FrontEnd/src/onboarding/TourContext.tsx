import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

// Milestones are fired by the app when the user completes a tour action, so a
// step can advance the moment the real task is done rather than on "Next".
export type TourMilestone = 'open-create' | 'list-created' | 'entry-added' | 'saved'

export interface TourStep {
  target: string
  title: string
  body: string
  placement?: 'top' | 'bottom' | 'left' | 'right'
  advanceOn?: TourMilestone
}

export const TOUR_STEPS: TourStep[] = [
  {
    target: 'new-list',
    title: 'Start a list',
    body: 'Every army begins here. This builds a list tied to your account that you can save and come back to.',
    placement: 'bottom',
    advanceOn: 'open-create',
  },
  {
    target: 'create-name',
    title: 'Name it',
    body: 'Give the list a name you will recognise — your tournament list, a casual roster, a work in progress.',
    placement: 'bottom',
  },
  {
    target: 'create-faction',
    title: 'Pick a faction',
    body: 'Choose a game system, then the faction to build. Units are drawn from that faction’s catalogue.',
    placement: 'bottom',
  },
  {
    target: 'create-limit',
    title: 'Set a points limit',
    body: 'The limit is the ceiling to build toward. The total turns red if your list goes over it. 2000 is a common 40k game size.',
    placement: 'bottom',
  },
  {
    target: 'create-submit',
    title: 'Create the list',
    body: 'This saves the empty list and opens the builder, where units get added.',
    placement: 'top',
    advanceOn: 'list-created',
  },
  {
    target: 'catalogue',
    title: 'Browse your units',
    body: 'Here is your faction’s catalogue. Search by name, and click any unit to see its stat line, weapons, and abilities.',
    placement: 'right',
  },
  {
    target: 'catalogue-add',
    title: 'Add a unit',
    body: 'Press Add to drop a unit into the list. Its name and points are snapshotted at the moment you add it.',
    placement: 'left',
    advanceOn: 'entry-added',
  },
  {
    target: 'list-total',
    title: 'Watch the total',
    body: 'Your running points total lives here. Adjust quantities on the right; the total — and the over-limit warning — update live.',
    placement: 'bottom',
  },
  {
    target: 'save-list',
    title: 'You’re set',
    body: 'Changes save as you go, but Save forces it and timestamps the list. That’s the whole loop — build away!',
    placement: 'top',
  },
]

const STORAGE_KEY = 'bl.tour.v1'

function readDone(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'done'
  } catch {
    return false
  }
}

function writeDone() {
  try {
    localStorage.setItem(STORAGE_KEY, 'done')
  } catch {
    // Ignore; the tour just may offer itself again next visit.
  }
}

interface TourContextValue {
  active: boolean
  stepIndex: number
  step: TourStep | null
  steps: TourStep[]
  start: () => void
  stop: (markDone?: boolean) => void
  next: () => void
  back: () => void
  notify: (milestone: TourMilestone) => void
  hasRun: boolean
}

const TourContext = createContext<TourContextValue | undefined>(undefined)

export function TourProvider({ children }: { children: ReactNode }) {
  const [active, setActive] = useState(false)
  const [stepIndex, setStepIndex] = useState(0)
  const [hasRun, setHasRun] = useState(readDone)

  const stop = useCallback((markDone = true) => {
    setActive(false)
    if (markDone) {
      writeDone()
      setHasRun(true)
    }
  }, [])

  const start = useCallback(() => {
    setStepIndex(0)
    setActive(true)
  }, [])

  const next = useCallback(() => {
    setStepIndex((i) => {
      if (i >= TOUR_STEPS.length - 1) {
        setActive(false)
        writeDone()
        setHasRun(true)
        return i
      }
      return i + 1
    })
  }, [])

  const back = useCallback(() => {
    setStepIndex((i) => Math.max(0, i - 1))
  }, [])

  const notify = useCallback((milestone: TourMilestone) => {
    if (!active) return
    if (TOUR_STEPS[stepIndex]?.advanceOn === milestone) {
      // Defer so the triggering view can render its next-step target first.
      setTimeout(() => {
        setStepIndex((i) => (i < TOUR_STEPS.length - 1 ? i + 1 : i))
      }, 120)
    }
  }, [active, stepIndex])

  const value = useMemo<TourContextValue>(
    () => ({
      active,
      stepIndex,
      step: active ? TOUR_STEPS[stepIndex] ?? null : null,
      steps: TOUR_STEPS,
      start,
      stop,
      next,
      back,
      notify,
      hasRun,
    }),
    [active, stepIndex, start, stop, next, back, notify, hasRun],
  )

  return <TourContext.Provider value={value}>{children}</TourContext.Provider>
}

export function useTour(): TourContextValue {
  const ctx = useContext(TourContext)
  if (!ctx) {
    throw new Error('useTour must be used within a TourProvider')
  }
  return ctx
}
