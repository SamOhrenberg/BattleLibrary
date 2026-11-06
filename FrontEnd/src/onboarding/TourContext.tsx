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
export type TourMilestone = 'open-create' | 'list-created' | 'datasheet-opened' | 'entry-added' | 'saved'

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
  // A later milestone may skip past this step (e.g. adding a unit straight from the catalogue).
  optional?: boolean
  // The target only exists for some units; the step is passed over when it is absent.
  ifPresent?: boolean
}

// Read-only walkthrough steps inside the open datasheet. Adding a unit early skips them.
function datasheetSteps(steps: Pick<TourStep, 'target' | 'title' | 'body' | 'ifPresent'>[]): TourStep[] {
  return steps.map((s) => ({ ...s, placement: 'bottom', interact: 'none', optional: true }))
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
    target: 'catalogue-unit',
    title: 'Open a datasheet',
    body: 'Your faction’s units are listed by battlefield role; search or filter to find one. Tap a unit’s name to open its datasheet. The Add button is a shortcut for later.',
    placement: 'bottom',
    advanceOn: 'datasheet-opened',
    optional: true,
  },
  ...datasheetSteps([
    {
      target: 'datasheet-head',
      title: 'The datasheet',
      body: 'Every unit has a datasheet: its rules card. The number by the name is its points cost. Armies are built to a points limit, so each unit spends part of that budget.',
    },
    {
      target: 'datasheet-keywords',
      title: 'Keywords',
      body: 'Keywords say what a unit is: Infantry, Vehicle, Character, its faction and more. Rules often target units "with" a keyword, so these decide what affects it. Tap +N to see them all.',
    },
    {
      target: 'datasheet-size',
      title: 'Unit size',
      body: 'Many units can be fielded at more than one size. More models means a tougher unit, at a higher points cost.',
      ifPresent: true,
    },
    {
      target: 'datasheet-stats',
      title: 'The stat line',
      body: 'M: Movement in inches. T: Toughness, how hard it is to wound. Sv: Save, the roll needed to ignore a hit. W: Wounds per model. Ld: Leadership, for Battle-shock tests. OC: Objective Control, its weight when holding objectives. InSv: an invulnerable save that AP can’t worsen. For an X+ value, lower is better.',
    },
    {
      target: 'datasheet-weapons',
      title: 'Weapons',
      body: 'Range is in inches (Melee means close combat). A: Attacks, dice rolled. BS/WS: the roll needed to hit. S: Strength, compared to Toughness to wound. AP: makes the target’s save worse. D: Damage per failed save. Tap an underlined keyword for its rule.',
    },
    {
      target: 'datasheet-abilities',
      title: 'Abilities and rules',
      body: 'What makes the unit special: its own abilities, plus shared core and faction rules. Tap a heading to expand it.',
    },
    {
      target: 'datasheet-leads',
      title: 'Leaders',
      body: 'A Character can lead a unit, joining it so they fight as one. This lists the units this Character can attach to.',
      ifPresent: true,
    },
    {
      target: 'datasheet-loadouts',
      title: 'Loadouts',
      body: 'This unit splits its models between loadouts. Assign each model until the count matches the unit size.',
      ifPresent: true,
    },
    {
      target: 'datasheet-options',
      title: 'Wargear options',
      body: 'Swap or add weapons and gear. Default is the standard loadout, Fixed can’t change, and +N is the extra points cost.',
      ifPresent: true,
    },
    {
      target: 'datasheet-presets',
      title: 'Saved loadouts',
      body: 'Save a wargear setup you like, then apply it in one tap the next time you add this unit.',
      ifPresent: true,
    },
  ]),
  {
    target: 'datasheet-add',
    title: 'Add it to your list',
    body: 'The subtotal includes your choices. Press Add to list to drop the unit into your army.',
    placement: 'top',
    advanceOn: 'entry-added',
  },
  {
    target: 'list-total',
    title: 'Watch the total',
    body: 'Your running points total lives here. Adjust quantities on the right; the total - and the over-limit warning - update live.',
    placement: 'bottom',
    interact: 'none',
  },
  {
    target: 'save-list',
    title: 'You’re set',
    body: 'Changes save as you go, but Save forces it and timestamps the list. That’s the whole loop - build away!',
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
