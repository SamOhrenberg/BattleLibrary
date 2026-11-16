import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'

// Milestones are fired by the app when the user completes a tour action, so a
// step can advance the moment the real task is done rather than on "Next".
export type TourMilestone = 'open-create' | 'list-created' | 'unit-form-opened' | 'entry-added'

// How a step may be advanced. When the target is interactive the overlay works
// this out from the DOM, so 'none' is only set to opt an informational step
// (one that points at a container) out of requiring interaction.
export type TourInteraction = 'click' | 'input' | 'none'

export interface TourStep {
  target: string
  title: string
  body: string
  placement?: 'top' | 'bottom' | 'left' | 'right'
  advanceOn?: TourMilestone
  // Override auto-detection of the target's interaction requirement.
  interact?: TourInteraction
  // A later milestone may skip past this step.
  optional?: boolean
  // The target only exists for some units; the step is passed over when it is absent.
  ifPresent?: boolean
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
    body: 'Give the list a name you will recognise - your tournament list, a casual roster, a work in progress.',
    placement: 'bottom',
  },
  {
    target: 'create-faction',
    title: 'Game and faction',
    body: 'Pick the game, and type your faction if you like. Everything except the game is optional.',
    placement: 'bottom',
  },
  {
    target: 'create-limit',
    title: 'Detachment and points',
    body: 'Write in a detachment and a points limit if your game uses them. Nothing here is checked against the rules.',
    placement: 'bottom',
  },
  {
    target: 'create-submit',
    title: 'Create the list',
    body: 'This saves the empty list and opens it, ready for units.',
    placement: 'top',
    advanceOn: 'list-created',
  },
  {
    target: 'add-unit',
    title: 'Add a unit',
    body: 'Write in each unit you are bringing, like a paper list.',
    placement: 'top',
    advanceOn: 'unit-form-opened',
  },
  {
    target: 'unit-name',
    title: 'Fill in what you want',
    body: 'Only the name matters. Models, points, wargear, enhancement and notes are all optional.',
    placement: 'bottom',
  },
  {
    target: 'unit-save',
    title: 'Save it',
    body: 'Add puts the unit in your list. Add another keeps the form open for the next one.',
    placement: 'top',
    advanceOn: 'entry-added',
  },
  {
    target: 'list-total',
    title: 'The running total',
    body: 'Points you enter add up here. It is a tally, not a rule check. Changes save as you go, so that is the whole loop.',
    placement: 'bottom',
    interact: 'none',
  },
]

const STORAGE_KEY = 'bl.tour.v2'

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
  // Pass over the current step in the direction last moved (its target is absent).
  skipMissing: () => void
  notify: (milestone: TourMilestone) => void
  hasRun: boolean
}

const TourContext = createContext<TourContextValue | undefined>(undefined)

export function TourProvider({ children }: { children: ReactNode }) {
  const [active, setActive] = useState(false)
  const [stepIndex, setStepIndex] = useState(0)
  const [hasRun, setHasRun] = useState(readDone)
  const lastDir = useRef<1 | -1>(1)

  const stop = useCallback((markDone = true) => {
    setActive(false)
    if (markDone) {
      writeDone()
      setHasRun(true)
    }
  }, [])

  const start = useCallback(() => {
    lastDir.current = 1
    setStepIndex(0)
    setActive(true)
  }, [])

  const next = useCallback(() => {
    lastDir.current = 1
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
    lastDir.current = -1
    setStepIndex((i) => Math.max(0, i - 1))
  }, [])

  const skipMissing = useCallback(() => {
    setStepIndex((i) => Math.min(TOUR_STEPS.length - 1, Math.max(0, i + lastDir.current)))
  }, [])

  const notify = useCallback((milestone: TourMilestone) => {
    if (!active) return
    // The step waiting on this milestone, looking ahead past optional steps so
    // doing a task early (e.g. adding a unit while browsing) skips its step.
    let target = stepIndex
    while (target < TOUR_STEPS.length && TOUR_STEPS[target].advanceOn !== milestone) {
      if (!TOUR_STEPS[target].optional) return
      target++
    }
    if (target >= TOUR_STEPS.length) return
    // Defer so the triggering view can render its next-step target first.
    setTimeout(() => {
      lastDir.current = 1
      setStepIndex(Math.min(target + 1, TOUR_STEPS.length - 1))
    }, 120)
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
      skipMissing,
      notify,
      hasRun,
    }),
    [active, stepIndex, start, stop, next, back, skipMissing, notify, hasRun],
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
