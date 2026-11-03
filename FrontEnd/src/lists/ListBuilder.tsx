import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../lib/api'
import { getDatasheets, getFactions, type CatalogueDatasheet, type CatalogueDetachment, type CatalogueFaction, type DatasheetDetail, type FactionDatasheets } from '../lib/catalogue'
import {
  addEntry,
  addForce,
  attachEntry,
  deleteList,
  removeEntry,
  removeForce,
  reorderEntries,
  setBattleSize,
  setDetachments,
  setEnhancement,
  setForceDisposition,
  setModelCount,
  setWarlord,
  updateEntry,
  updateList,
  BATTLE_SIZES,
  type ArmyList,
  type ListEntry,
  type VariantQuantity,
} from '../lib/lists'
import { getPresets, createPreset, deletePreset, type LoadoutPreset } from '../lib/presets'
import { validateList, variantPoolWarnings, isCharacter, BATTLE_SIZE_RULES, type VariantInfo } from './validation'
import { groupByRole } from './roles'
import { isLegends, isCrucible } from './tiers'
import { useTour } from '../onboarding/TourContext'
import { useToast } from '../ui/Toast'
import { UnitDetailModal } from './UnitDetailModal'
import { RuleSearch } from './RuleSearch'
import { ListAssistant } from './ListAssistant'
import { RuleText } from './RuleText'
import { listToText } from './exportList'
import { CompositionBar } from './CompositionBar'
import { PlayView } from './PlayView'

interface Props {
  initial: ArmyList
  onBack: () => void
  onDeleted: () => void
}

export function ListBuilder({ initial, onBack, onDeleted }: Props) {
  const { notify } = useTour()
  const [list, setList] = useState<ArmyList>(initial)
  // Catalogue data cached per faction, so each force shows its own units.
  const [factionData, setFactionData] = useState<Record<string, FactionDatasheets>>({})
  // Which force the catalogue + "Add" target. Defaults to the primary force.
  const [activeForceId, setActiveForceId] = useState(initial.forces[0]?.id ?? '')
  // Factions available to add as an allied force (loaded on demand).
  const [allFactions, setAllFactions] = useState<CatalogueFaction[] | null>(null)
  const [addingForce, setAddingForce] = useState(false)
  const [search, setSearch] = useState('')
  // Catalogue ordering: by role (grouped), name, or points (cheapest first).
  const [sort, setSort] = useState<'role' | 'name' | 'points'>('role')
  // Legends is extra, non-standard content, so hide it by default; Crucible is
  // current, so show it. Toggles below let either be flipped.
  const [showLegends, setShowLegends] = useState(false)
  const [showCrucible, setShowCrucible] = useState(true)
  // "Show X" opt-in content (allied daemons, Chaos Knights, Titans, ...) is off
  // by default; a unit gated on one appears only when its toggle is enabled here.
  const [enabledToggles, setEnabledToggles] = useState<Set<string>>(new Set())
  const [tab, setTab] = useState<'catalogue' | 'list'>('catalogue')
  // How the list panel groups entries: by role (default) or the owner's custom order.
  const [listView, setListView] = useState<'role' | 'custom'>('role')
  // Role groups collapsed in the catalogue panel.
  const [catCollapsed, setCatCollapsed] = useState<Set<string>>(new Set())
  const [detailId, setDetailId] = useState<string | null>(null)
  const [editEntry, setEditEntry] = useState<ListEntry | null>(null)
  // Datasheet details seen so far, so the loadout-pool warning knows each unit's
  // fixed model count and variant ids. Populated as the detail modal loads them.
  const [detailCache, setDetailCache] = useState<Record<string, DatasheetDetail>>({})
  // The owner's saved loadouts for this game system, indexed by datasheet.
  const [presets, setPresets] = useState<LoadoutPreset[]>([])
  // Inline custom-name / notes editors, keyed by the entry being edited.
  const [renameId, setRenameId] = useState<string | null>(null)
  const [nameDraft, setNameDraft] = useState('')
  const [notesId, setNotesId] = useState<string | null>(null)
  const [notesDraft, setNotesDraft] = useState('')
  // Set by Escape so the blur that follows discards the edit instead of saving.
  const cancelEdit = useRef(false)
  const [showAssistant, setShowAssistant] = useState(false)
  const [copied, setCopied] = useState(false)
  const [showPlay, setShowPlay] = useState(false)
  const [showSearch, setShowSearch] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const exportRef = useRef<HTMLDivElement>(null)

  // Close the export menu on an outside click or Escape.
  useEffect(() => {
    if (!exportOpen) return
    const onDown = (e: MouseEvent) => {
      if (exportRef.current && !exportRef.current.contains(e.target as Node)) setExportOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setExportOpen(false)
    window.addEventListener('mousedown', onDown)
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('mousedown', onDown)
      window.removeEventListener('keydown', onKey)
    }
  }, [exportOpen])
  const [name, setName] = useState(initial.name)
  const [limit, setLimit] = useState(initial.pointsLimit?.toString() ?? '')
  const [disposition, setDisposition] = useState(initial.forceDisposition ?? '')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const toast = useToast()
  // Fire the 'entry-added' tour milestone only on the first add.
  const addedOnce = useRef(initial.entries.length > 0)

  // The active force (falls back to the primary) and the factions in play.
  const activeForce = list.forces.find((f) => f.id === activeForceId) ?? list.forces[0]
  const activeFactionId = activeForce?.factionId ?? list.factionId
  const primaryFactionId = list.forces[0]?.factionId ?? list.factionId

  // Load catalogue data for the active and primary factions when not cached.
  useEffect(() => {
    const needed = [activeFactionId, primaryFactionId].filter((f) => f && !factionData[f])
    if (needed.length === 0) return
    let active = true
    Promise.all(needed.map((f) => getDatasheets(f).then((d) => [f, d] as const)))
      .then((pairs) => {
        if (!active) return
        setFactionData((prev) => {
          const next = { ...prev }
          for (const [f, d] of pairs) next[f] = d
          return next
        })
      })
      .catch(() => active && toast.push('Could not load the faction catalogue.', 'error'))
    return () => {
      active = false
    }
  }, [activeFactionId, primaryFactionId, factionData, toast])

  // Catalogue from the active force's faction; detachments from the primary.
  const datasheets: CatalogueDatasheet[] = factionData[activeFactionId]?.datasheets ?? []
  const equipment: string[] = factionData[activeFactionId]?.faction.equipment ?? []
  const detachments: CatalogueDetachment[] = factionData[primaryFactionId]?.faction.detachments ?? []

  // Load the owner's saved loadouts for this system (presets are per datasheet).
  useEffect(() => {
    let active = true
    getPresets(list.gameSystemKey)
      .then((p) => active && setPresets(p))
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [list.gameSystemKey])

  const presetsBySheet = useMemo(() => {
    const map = new Map<string, LoadoutPreset[]>()
    for (const p of presets) {
      const arr = map.get(p.datasheetId)
      if (arr) arr.push(p)
      else map.set(p.datasheetId, [p])
    }
    return map
  }, [presets])

  async function refreshPresets() {
    try {
      setPresets(await getPresets(list.gameSystemKey))
    } catch {
      // Keep the current presets if the refresh fails.
    }
  }

  const cacheDetail = (d: DatasheetDetail) =>
    setDetailCache((prev) => (prev[d.id] === d ? prev : { ...prev, [d.id]: d }))

  // Save a datasheet's current wargear + size + loadout as a named, reusable preset.
  async function savePreset(
    datasheetId: string,
    name: string,
    optionIds: string[],
    models?: number,
    variants?: VariantQuantity[],
  ) {
    try {
      await createPreset({ gameSystemKey: list.gameSystemKey, datasheetId, name, models, selectedOptionIds: optionIds, variants })
      await refreshPresets()
      toast.push(`Saved loadout "${name}".`, 'success')
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not save the preset.', 'error')
    }
  }

  async function removePreset(id: string) {
    try {
      await deletePreset(id)
      await refreshPresets()
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not delete the preset.', 'error')
    }
  }

  // A point-less system (e.g. Kill Team 2024): operatives carry no points, so
  // show operative counts instead of points and drop the limit controls.
  const pointless = datasheets.length > 0 && datasheets.every((d) => d.points == null)
  const unitWord = pointless ? 'operatives' : 'units'

  const legendsCount = useMemo(() => datasheets.filter((d) => isLegends(d.name)).length, [datasheets])
  const crucibleCount = useMemo(
    () => datasheets.filter((d) => isCrucible(d.name, d.keywords)).length,
    [datasheets],
  )
  // Selected detachment ids (also used to gate detachment-locked units below).
  const chosenIds = useMemo(() => new Set(list.detachments.map((d) => d.id)), [list.detachments])

  // "Show X" content groups present in this faction, with how many units each gates.
  const contentToggles = useMemo(() => {
    const counts = new Map<string, number>()
    for (const d of datasheets) {
      for (const t of d.availability?.requiredToggles ?? []) counts.set(t, (counts.get(t) ?? 0) + 1)
    }
    return [...counts.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  }, [datasheets])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return datasheets.filter((d) => {
      if (q && !d.name.toLowerCase().includes(q)) return false
      if (!showLegends && isLegends(d.name)) return false
      if (!showCrucible && isCrucible(d.name, d.keywords)) return false
      const av = d.availability
      if (av) {
        // Opt-in content stays hidden until its "Show X" toggle is enabled.
        if (av.requiredToggles.length > 0 && !av.requiredToggles.every((t) => enabledToggles.has(t))) return false
        // Detachment-locked units appear only with one of their detachments chosen.
        if (av.requiredDetachmentIds.length > 0 && !av.requiredDetachmentIds.some((id) => chosenIds.has(id))) return false
      }
      return true
    })
  }, [datasheets, search, showLegends, showCrucible, enabledToggles, chosenIds])

  const catalogueGroups = useMemo(() => groupByRole(filtered, (d) => d.keywords), [filtered])
  // Flat ordering for the name/points sorts (role uses the grouped view).
  const sortedFlat = useMemo(() => {
    const arr = [...filtered]
    if (sort === 'name') {
      arr.sort((a, b) => a.name.localeCompare(b.name))
    } else if (sort === 'points') {
      arr.sort((a, b) => (a.points ?? Infinity) - (b.points ?? Infinity) || a.name.localeCompare(b.name))
    }
    return arr
  }, [filtered, sort])
  const allCollapsed = catalogueGroups.length > 0 && catalogueGroups.every((g) => catCollapsed.has(g.role))
  const toggleAllGroups = () =>
    setCatCollapsed(allCollapsed ? new Set() : new Set(catalogueGroups.map((g) => g.role)))

  async function run(action: () => Promise<ArmyList>) {
    setBusy(true)
    try {
      setList(await action())
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Something went wrong.', 'error')
    } finally {
      setBusy(false)
    }
  }

  // Applies an entry change and keeps the open edit modal in sync.
  async function runEntry(entryId: string, action: () => Promise<ArmyList>) {
    setBusy(true)
    try {
      const next = await action()
      setList(next)
      setEditEntry(next.entries.find((x) => x.id === entryId) ?? null)
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Something went wrong.', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function add(datasheetId: string, optionIds: string[] = [], models?: number, variants?: VariantQuantity[]) {
    await run(() => addEntry(list.id, datasheetId, optionIds, models, activeForce?.id, variants))
    if (!addedOnce.current) {
      addedOnce.current = true
      notify('entry-added')
    }
  }

  // Add an allied force (a faction) and switch the catalogue to it.
  async function addAlliedForce(factionId: string) {
    setBusy(true)
    try {
      const before = new Set(list.forces.map((f) => f.id))
      const next = await addForce(list.id, factionId)
      setList(next)
      const added = next.forces.find((f) => !before.has(f.id))
      if (added) setActiveForceId(added.id)
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not add the force.', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function dropForce(forceId: string) {
    const force = list.forces.find((f) => f.id === forceId)
    if (!force) return
    if (!confirm(`Remove the ${force.factionName} force and its units?`)) return
    try {
      const next = await run(() => removeForce(list.id, forceId))
      // Switching away from a removed force is handled by activeForce's fallback.
      if (activeForceId === forceId) setActiveForceId(list.forces[0]?.id ?? '')
      return next
    } catch {
      // run() already surfaced the error.
    }
  }

  // Factions available to add, loaded once, excluding those already in the list.
  async function ensureFactions() {
    if (allFactions) return
    try {
      setAllFactions(await getFactions(list.gameSystemKey))
    } catch {
      toast.push('Could not load factions.', 'error')
    }
  }

  async function copyText() {
    try {
      await navigator.clipboard.writeText(listToText(list, pointless))
      setCopied(true)
      setTimeout(() => setCopied(false), 1800)
    } catch {
      toast.push('Could not copy to the clipboard.', 'error')
    }
  }

  function downloadText() {
    const safe = list.name.replace(/[^\w.-]+/g, '_').replace(/^_+|_+$/g, '') || 'army-list'
    const blob = new Blob([listToText(list, pointless)], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${safe}.txt`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }

  // Copies an entry: re-adds the same datasheet, size, wargear, and loadout pool
  // (variants, when the datasheet's shape is known). Warlord/enhancement are
  // intentionally not copied (each is unique).
  function duplicateEntry(e: ListEntry) {
    const groups = detailCache[e.datasheetId]?.variantGroups ?? []
    const variantIds = new Set(groups.flatMap((g) => g.variants.map((v) => v.id)))
    const optionIds = e.selectedOptions.filter((o) => !variantIds.has(o.id)).map((o) => o.id)
    const variants = e.selectedOptions
      .filter((o) => variantIds.has(o.id))
      .map((o) => ({ id: o.id, quantity: o.quantity }))
    void run(() => addEntry(list.id, e.datasheetId, optionIds, e.modelCount, undefined, variants.length ? variants : undefined))
  }

  // Inline custom-name editing: open with the current name, save on blur/Enter.
  function startRename(e: ListEntry) {
    setNotesId(null)
    setRenameId(e.id)
    setNameDraft(e.customName ?? '')
  }
  async function commitRename(e: ListEntry) {
    setRenameId(null)
    if (cancelEdit.current) {
      cancelEdit.current = false
      return
    }
    const next = nameDraft.trim()
    if (next === (e.customName ?? '')) return
    await run(() => updateEntry(list.id, e.id, { customName: next }))
  }

  // Inline notes editing: open with the current note, save on blur.
  function startNotes(e: ListEntry) {
    setRenameId(null)
    setNotesId(e.id)
    setNotesDraft(e.notes ?? '')
  }
  async function commitNotes(e: ListEntry) {
    setNotesId(null)
    if (cancelEdit.current) {
      cancelEdit.current = false
      return
    }
    const next = notesDraft.trim()
    if (next === (e.notes ?? '')) return
    await run(() => updateEntry(list.id, e.id, { notes: next }))
  }

  // Adds a datasheet and sets the new entry as Warlord in one step.
  async function addAsWarlord(datasheetId: string) {
    setBusy(true)
    try {
      const before = new Set(list.entries.map((e) => e.id))
      const added = await addEntry(list.id, datasheetId, [])
      const fresh = added.entries.find((e) => !before.has(e.id))
      setList(fresh ? await setWarlord(list.id, fresh.id, true) : added)
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Something went wrong.', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    setBusy(true)
    try {
      const parsed = limit.trim() === '' ? null : Number(limit)
      setList(await updateList(list.id, name.trim() || list.name, Number.isNaN(parsed!) ? null : parsed))
      setSaved(true)
      setTimeout(() => setSaved(false), 1800)
      notify('saved')
    } catch (err) {
      toast.push(err instanceof ApiError ? err.message : 'Could not save the list.', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!confirm('Delete this list? This cannot be undone.')) return
    setBusy(true)
    try {
      await deleteList(list.id)
      onDeleted()
    } catch {
      toast.push('Could not delete the list.', 'error')
      setBusy(false)
    }
  }

  const over = list.isOverLimit
  const hasDetachments = detachments.length > 0
  const rules = BATTLE_SIZE_RULES[list.battleSize ?? '']
  const dpUsed = list.detachments.reduce((n, d) => n + d.dp, 0)
  // Detachment-locked datasheets in the active faction, for legality checks.
  const detachmentReqs = useMemo(() => {
    const byDatasheet = new Map<string, string[]>()
    for (const d of datasheets) {
      const req = d.availability?.requiredDetachmentIds
      if (req && req.length > 0) byDatasheet.set(d.id, req)
    }
    return {
      byDatasheet,
      selectedIds: chosenIds,
      names: new Map(detachments.map((d) => [d.id, d.name])),
    }
  }, [datasheets, chosenIds, detachments])
  const issues = useMemo(
    () => validateList(list, hasDetachments, pointless, detachmentReqs),
    [list, hasDetachments, pointless, detachmentReqs],
  )
  // Loadout-pool fill status, from the datasheet details seen so far.
  const variantInfo = useMemo(() => {
    const map = new Map<string, VariantInfo>()
    for (const d of Object.values(detailCache)) {
      const groups = d.variantGroups ?? []
      if (groups.length > 0) {
        map.set(d.id, { fixed: d.fixedModelCount ?? 0, variantIds: new Set(groups.flatMap((g) => g.variants.map((v) => v.id))) })
      }
    }
    return map
  }, [detailCache])
  const poolWarnings = useMemo(
    () => (pointless ? [] : variantPoolWarnings(list, variantInfo)),
    [list, variantInfo, pointless],
  )
  // A unit whose rules force the Warlord slot locks it: it can't be cleared and
  // no other unit can take the slot while it is in the list.
  const hasForcedWarlord = useMemo(() => list.entries.some((e) => e.forcedWarlord), [list.entries])

  const chosenDetachments = useMemo(
    () => detachments.filter((d) => chosenIds.has(d.id)),
    [detachments, chosenIds],
  )

  // Enhancements offered by any chosen detachment.
  const availableEnhancements = useMemo(
    () => chosenDetachments.flatMap((d) => d.enhancements),
    [chosenDetachments],
  )

  const changeBattleSize = (b: string | null) => run(() => setBattleSize(list.id, b))
  const saveDisposition = () => {
    const next = disposition.trim()
    if (next === (list.forceDisposition ?? '')) return
    void run(() => setForceDisposition(list.id, next || null))
  }
  const toggleDetachment = (id: string) =>
    run(() => setDetachments(list.id, chosenIds.has(id) ? [...chosenIds].filter((x) => x !== id) : [...chosenIds, id]))

  const toggleContent = (name: string) =>
    setEnabledToggles((prev) => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })

  const attach = (entryId: string, targetId: string | null) => run(() => attachEntry(list.id, entryId, targetId))

  // Leader attachment grouping: each bodyguard entry id -> the Leaders attached
  // to it, plus the set of leader ids that render nested (their bodyguard still
  // exists). A leader whose bodyguard is gone falls back to a normal row.
  const { leadersByBodyguard, nestedLeaderIds } = useMemo(() => {
    const ids = new Set(list.entries.map((e) => e.id))
    const byBodyguard = new Map<string, ListEntry[]>()
    const nested = new Set<string>()
    for (const e of list.entries) {
      if (e.attachedToEntryId && ids.has(e.attachedToEntryId)) {
        const arr = byBodyguard.get(e.attachedToEntryId)
        if (arr) arr.push(e)
        else byBodyguard.set(e.attachedToEntryId, [e])
        nested.add(e.id)
      }
    }
    return { leadersByBodyguard: byBodyguard, nestedLeaderIds: nested }
  }, [list.entries])

  // Non-Character units in the same force a given Leader can be attached to.
  const bodyguardsFor = (leader: ListEntry) =>
    list.entries.filter((e) => !isCharacter(e) && e.forceId === leader.forceId)

  // Top-level entries (attached Leaders fold into their Bodyguard) for one force.
  const topLevelOf = (forceId: string) =>
    list.entries.filter((e) => e.forceId === forceId && !nestedLeaderIds.has(e.id))

  // Full id sequence across all forces, with each top-level entry's Leaders right
  // after it; `override` supplies a reordered top-level list for one force.
  function fullOrder(overrideForceId: string, overrideTop: ListEntry[]): string[] {
    const ids: string[] = []
    for (const f of list.forces) {
      const top = f.id === overrideForceId ? overrideTop : topLevelOf(f.id)
      for (const e of top) {
        ids.push(e.id)
        for (const l of leadersByBodyguard.get(e.id) ?? []) ids.push(l.id)
      }
    }
    return ids
  }

  // Move a top-level entry up/down within its force, carrying its Leaders, and
  // persist the full id sequence.
  function moveTopLevel(forceId: string, index: number, dir: -1 | 1) {
    const top = topLevelOf(forceId)
    const next = index + dir
    if (next < 0 || next >= top.length) return
    ;[top[index], top[next]] = [top[next], top[index]]
    void run(() => reorderEntries(list.id, fullOrder(forceId, top)))
  }

  // Enhancements still free to take (plus the entry's own current one).
  function enhancementsFor(e: ListEntry) {
    const usedByOthers = new Set(
      list.entries.filter((x) => x.id !== e.id && x.enhancementId).map((x) => x.enhancementId),
    )
    return availableEnhancements.filter((en) => !usedByOthers.has(en.id))
  }

  const renderUnit = (d: CatalogueDatasheet, tourFirst: boolean) => (
    <div className="unit-row" key={d.id}>
      <button type="button" className="unit-name" onClick={() => setDetailId(d.id)}>
        {d.name}
      </button>
      {!pointless && <span className="unit-pts">{d.points != null ? `${d.points}` : '-'}</span>}
      <button
        type="button"
        className="add-btn"
        data-tour={tourFirst ? 'catalogue-add' : undefined}
        onClick={() => void add(d.id)}
        disabled={busy}
      >
        Add
      </button>
    </div>
  )

  // One list entry. Variant: 'normal' standalone; 'leader' the attached Leader
  // shown on top (with a detach control); 'embedded' the Bodyguard inset beneath
  // its Leader.
  const renderEntryRow = (e: ListEntry, variant: 'normal' | 'leader' | 'embedded' = 'normal') => {
    const embedded = variant === 'embedded'
    const leaderRow = variant === 'leader'
    return (
    <div className={`entry-row${embedded ? ' entry-embedded' : ''}${leaderRow ? ' entry-leader' : ''}`} key={e.id}>
      <div className="entry-main">
        {renameId === e.id ? (
          <input
            className="entry-name-input"
            autoFocus
            value={nameDraft}
            maxLength={200}
            placeholder={e.datasheetName}
            aria-label="Custom unit name"
            onChange={(ev) => setNameDraft(ev.target.value)}
            onBlur={() => void commitRename(e)}
            onKeyDown={(ev) => {
              if (ev.key === 'Enter') ev.currentTarget.blur()
              else if (ev.key === 'Escape') {
                cancelEdit.current = true
                ev.currentTarget.blur()
              }
            }}
          />
        ) : (
          <button type="button" className="entry-name entry-edit" onClick={() => setEditEntry(e)} title="View / edit options">
            {embedded && <span className="lead-marker" aria-hidden="true">↳ </span>}
            {e.customName || e.datasheetName}
          </button>
        )}
        {e.customName && renameId !== e.id && <div className="entry-sub">{e.datasheetName}</div>}
        {!pointless && (
          <div className="entry-each">
            {e.modelCount > 1 ? `${e.modelCount} models · ` : ''}
            {e.points + e.optionsPoints} pts
            {e.optionsPoints > 0 ? ` (${e.points} + ${e.optionsPoints})` : ''}
          </div>
        )}
        {e.selectedOptions.length > 0 && (
          <ul className="entry-opts">
            {e.selectedOptions.map((o) => (
              <li key={o.id}>
                {o.name}
                {o.points > 0 ? ` (+${o.points})` : ''}
              </li>
            ))}
          </ul>
        )}
        {((!pointless && isCharacter(e)) || e.enhancementName) && (
          <div className="entry-badges">
            {!pointless && isCharacter(e) && (
              <button
                type="button"
                className={`warlord-btn${e.isWarlord ? ' on' : ''}${e.forcedWarlord ? ' locked' : ''}`}
                onClick={() => void run(() => setWarlord(list.id, e.id, !e.isWarlord))}
                disabled={busy || e.forcedWarlord || (!e.isWarlord && hasForcedWarlord)}
                title={
                  e.forcedWarlord
                    ? "This unit's rules require it to be your Warlord."
                    : hasForcedWarlord
                      ? 'Another unit must be your Warlord.'
                      : undefined
                }
              >
                ★ {e.isWarlord ? 'Warlord' : 'Make Warlord'}
              </button>
            )}
            {e.enhancementName && (
              <span className="badge-enh">{e.enhancementName} (+{e.enhancementPoints})</span>
            )}
          </div>
        )}
        {notesId === e.id ? (
          <textarea
            className="entry-notes-input"
            autoFocus
            value={notesDraft}
            maxLength={2000}
            rows={3}
            placeholder="Notes for this unit"
            aria-label="Unit notes"
            onChange={(ev) => setNotesDraft(ev.target.value)}
            onBlur={() => void commitNotes(e)}
            onKeyDown={(ev) => {
              if (ev.key === 'Escape') {
                cancelEdit.current = true
                ev.currentTarget.blur()
              }
            }}
          />
        ) : (
          e.notes && (
            <button type="button" className="entry-note" onClick={() => startNotes(e)} title="Edit note">
              {e.notes}
            </button>
          )
        )}
        <div className="entry-links">
          <button type="button" className="entry-edit-link" onClick={() => setEditEntry(e)}>
            Edit…
          </button>
          <button type="button" className="entry-edit-link" onClick={() => startRename(e)}>
            {e.customName ? 'Rename' : 'Name'}
          </button>
          {notesId !== e.id && !e.notes && (
            <button type="button" className="entry-edit-link" onClick={() => startNotes(e)}>
              Add note
            </button>
          )}
          {!pointless && leaderRow && (
            <button type="button" className="entry-edit-link" onClick={() => void attach(e.id, null)} disabled={busy}>
              Detach
            </button>
          )}
          {!pointless && variant === 'normal' && isCharacter(e) && (() => {
            const options = bodyguardsFor(e)
            return options.length > 0 ? (
              <select
                className="attach-select"
                value={e.attachedToEntryId ?? ''}
                onChange={(ev) => void attach(e.id, ev.target.value || null)}
                disabled={busy}
                aria-label="Attach this Leader to a unit"
              >
                <option value="">Attach to…</option>
                {options.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.customName || b.datasheetName}
                  </option>
                ))}
              </select>
            ) : null
          })()}
        </div>
      </div>
      {!pointless && <span className="line-total">{e.lineTotal}</span>}
      <button
        type="button"
        className="icon-btn dup-btn"
        onClick={() => duplicateEntry(e)}
        disabled={busy}
        aria-label="Duplicate"
        title="Duplicate"
      >
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
          <rect x="9" y="9" width="11" height="11" rx="2" />
          <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
        </svg>
      </button>
      <button type="button" className="icon-btn" onClick={() => void run(() => removeEntry(list.id, e.id))} disabled={busy} aria-label="Remove">×</button>
    </div>
    )
  }

  // A top-level list item: either a plain entry row, or an attached unit with the
  // Leader(s) on top and the Bodyguard embedded beneath.
  const renderListItem = (e: ListEntry) => {
    const leaders = leadersByBodyguard.get(e.id)
    if (leaders && leaders.length > 0) {
      const combined = e.lineTotal + leaders.reduce((n, l) => n + l.lineTotal, 0)
      return (
        <div className="attached-unit" key={e.id}>
          <div className="attached-head">
            <span className="attached-label">Attached unit</span>
            {!pointless && <span className="attached-pts">{combined} pts</span>}
          </div>
          {leaders.map((l) => renderEntryRow(l, 'leader'))}
          {renderEntryRow(e, 'embedded')}
        </div>
      )
    }
    return renderEntryRow(e, 'normal')
  }

  // The entries of one force, grouped by role or shown in custom order.
  const renderForceEntries = (forceId: string) => {
    const top = topLevelOf(forceId)
    if (top.length === 0) {
      return <p className="empty">No units in this force yet.</p>
    }
    if (listView === 'custom') {
      return (
        <div className="custom-list">
          {top.map((e, i) => (
            <div className="custom-item" key={e.id}>
              <div className="reorder">
                <button
                  type="button"
                  className="reorder-btn"
                  onClick={() => moveTopLevel(forceId, i, -1)}
                  disabled={busy || i === 0}
                  aria-label="Move up"
                >
                  ↑
                </button>
                <button
                  type="button"
                  className="reorder-btn"
                  onClick={() => moveTopLevel(forceId, i, 1)}
                  disabled={busy || i === top.length - 1}
                  aria-label="Move down"
                >
                  ↓
                </button>
              </div>
              <div className="custom-body">{renderListItem(e)}</div>
            </div>
          ))}
        </div>
      )
    }
    return groupByRole(top, (e) => e.keywords).map((group) => (
      <details className="role-group" key={group.role} open>
        <summary className="role-summary">
          {group.role} <span className="role-count">{group.items.length}</span>
        </summary>
        {group.items.map((e) => renderListItem(e))}
      </details>
    ))
  }

  return (
    <section className="card">
      <div className="builder-bar">
        <div className="builder-id">
          <button type="button" className="tour-skip" onClick={onBack} style={{ textAlign: 'left' }}>
            ← All lists
          </button>
          <input
            className="builder-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={save}
            aria-label="List name"
            maxLength={120}
          />
          <span className="builder-faction">{list.factionName}</span>
        </div>

        <div className="builder-meta">
          <div className="points-readout" data-tour="list-total">
            {pointless ? (
              <>
                <div className="pts">{list.entries.length}</div>
                <label className="limit-label">operatives</label>
              </>
            ) : (
              <>
                <div className={`pts${over ? ' over' : ''}`}>{list.total}</div>
                <label className="limit-label">
                  {list.battleSize ? (
                    <>{list.battleSize} · {list.pointsLimit}</>
                  ) : (
                    <>
                      limit{' '}
                      <input
                        className="limit-edit"
                        inputMode="numeric"
                        value={limit}
                        placeholder="none"
                        onChange={(e) => setLimit(e.target.value.replace(/[^0-9]/g, ''))}
                        onBlur={save}
                      />
                    </>
                  )}
                </label>
                {over && <div className="over-badge">Over limit</div>}
              </>
            )}
          </div>
          <div className="lists-actions">
            <div className="export-menu" ref={exportRef}>
              <button
                type="button"
                className="btn"
                onClick={() => setExportOpen((v) => !v)}
                disabled={list.entries.length === 0}
                aria-haspopup="menu"
                aria-expanded={exportOpen}
              >
                Export ▾
              </button>
              {exportOpen && (
                <div className="export-dropdown" role="menu">
                  <button
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      void copyText()
                      setExportOpen(false)
                    }}
                  >
                    {copied ? 'Copied ✓' : 'Copy to clipboard'}
                  </button>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      downloadText()
                      setExportOpen(false)
                    }}
                  >
                    Download .txt file
                  </button>
                </div>
              )}
            </div>
            <button
              type="button"
              className={`btn btn-primary${saved ? ' is-saved' : ''}`}
              data-tour="save-list"
              onClick={save}
              disabled={busy || saved}
            >
              {busy ? 'Saving…' : saved ? 'Saved ✓' : 'Save'}
            </button>
            <button type="button" className="btn btn-danger" onClick={remove} disabled={busy}>
              Delete
            </button>
          </div>
        </div>
      </div>


      {!pointless && list.forces.length > 0 && (
        <div className="force-bar">
          <span className="force-bar-label">Forces</span>
          <div className="force-tabs">
            {list.forces.map((f) => (
              <button
                key={f.id}
                type="button"
                className={`force-tab${activeForce?.id === f.id ? ' on' : ''}`}
                onClick={() => setActiveForceId(f.id)}
              >
                {f.factionName}
              </button>
            ))}
          </div>
          {addingForce ? (
            <span className="add-force-pick">
              <select
                className="attach-select"
                defaultValue=""
                disabled={busy}
                aria-label="Choose a faction to add"
                onChange={(e) => {
                  const fid = e.target.value
                  if (fid) {
                    void addAlliedForce(fid)
                    setAddingForce(false)
                  }
                }}
              >
                <option value="">Choose faction…</option>
                {(allFactions ?? [])
                  .filter((f) => !list.forces.some((lf) => lf.factionId === f.id))
                  .map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.name}
                    </option>
                  ))}
              </select>
              <button type="button" className="entry-edit-link" onClick={() => setAddingForce(false)}>
                Cancel
              </button>
            </span>
          ) : (
            <button
              type="button"
              className="btn btn-sm add-force-btn"
              onClick={() => {
                void ensureFactions()
                setAddingForce(true)
              }}
              disabled={busy}
            >
              + Add force
            </button>
          )}
        </div>
      )}

      {!pointless && (
        <div className="list-setup">
          <div className="setup-title">Configuration</div>
          <label className="setup-field battle-size">
            <span>Battle size</span>
            <select value={list.battleSize ?? ''} onChange={(e) => changeBattleSize(e.target.value || null)} disabled={busy}>
              <option value="">Custom</option>
              {BATTLE_SIZES.map((b) => (
                <option key={b} value={b}>{b} · {BATTLE_SIZE_RULES[b].cap} pts</option>
              ))}
            </select>
          </label>
          <label className="setup-field disposition">
            <span>Force disposition</span>
            <input
              className="disposition-input"
              value={disposition}
              maxLength={100}
              placeholder="Optional"
              onChange={(e) => setDisposition(e.target.value)}
              onBlur={saveDisposition}
              disabled={busy}
            />
          </label>
          {hasDetachments && (
            <div className="setup-field">
              <span>
                Detachments{rules ? ` · ${dpUsed}/${rules.dp} DP` : dpUsed > 0 ? ` · ${dpUsed} DP` : ''}
              </span>
              <div className="detachment-chips">
                {detachments.map((d) => {
                  const selected = chosenIds.has(d.id)
                  // One detachment is always allowed; past that, adding can't exceed the budget.
                  const overBudget =
                    !selected && !!rules && list.detachments.length >= 1 && dpUsed + d.dp > rules.dp
                  const hasPreview = d.rules.length > 0 || d.enhancements.length > 0
                  return (
                    <div className="det-chip-wrap" key={d.id}>
                      <button
                        type="button"
                        className={`det-chip${selected ? ' on' : ''}`}
                        onClick={() => toggleDetachment(d.id)}
                        disabled={busy || overBudget}
                        title={overBudget ? 'Not enough Detachment Points left' : undefined}
                      >
                        {d.name} <span className="det-dp">{d.dp} DP</span>
                      </button>
                      {hasPreview && (
                        <div className="det-popover" role="tooltip">
                          <div className="dp-title">{d.name} · {d.dp} DP</div>
                          {d.rules.length > 0 && (
                            <dl className="rules-list">
                              {d.rules.map((r) => (
                                <div className="rule" key={r.name}>
                                  <dt>{r.name}</dt>
                                  {r.text && <dd><RuleText text={r.text} /></dd>}
                                </div>
                              ))}
                            </dl>
                          )}
                          {d.enhancements.length > 0 && (
                            <div className="dp-enh">
                              <span className="dp-sub">Enhancements</span>
                              <ul>
                                {d.enhancements.map((e) => (
                                  <li key={e.id}>
                                    {e.name} <span className="dp-pts">+{e.points}</span>
                                  </li>
                                ))}
                              </ul>
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          )}
        </div>
      )}

      {!pointless && list.entries.length > 0 && (
        <CompositionBar list={list} hasDetachments={hasDetachments} />
      )}

      {!pointless && chosenDetachments.some((d) => d.rules.length > 0) && (
        <div className="detachment-rules">
          {chosenDetachments
            .filter((d) => d.rules.length > 0)
            .map((d) => (
              <details className="det-rules" key={d.id} open>
                <summary>{d.name} - detachment rule{d.rules.length > 1 ? 's' : ''}</summary>
                <dl className="rules-list">
                  {d.rules.map((r) => (
                    <div className="rule" key={r.name}>
                      <dt>{r.name}</dt>
                      {r.text && <dd><RuleText text={r.text} /></dd>}
                    </div>
                  ))}
                </dl>
              </details>
            ))}
        </div>
      )}

      {!pointless && (
        <div className={`legality ${issues.length === 0 ? 'ok' : 'bad'}`}>
          {issues.length === 0 ? (
            <span className="legality-head">✓ Battle ready{list.detachments.length > 0 ? ` · ${list.detachments.map((d) => d.name).join(', ')}` : ''}</span>
          ) : (
            <>
              <div className="legality-top">
                <span className="legality-head">{issues.length} to fix before this list is legal</span>
                <button type="button" className="btn btn-sm" onClick={() => setShowAssistant(true)}>
                  Help me fix
                </button>
              </div>
              <ul>{issues.map((i, idx) => <li key={idx}>{i.message}</li>)}</ul>
            </>
          )}
        </div>
      )}

      {!pointless && poolWarnings.length > 0 && (
        <div className="legality warn">
          <span className="legality-head">
            ⚠ {poolWarnings.length} loadout{poolWarnings.length > 1 ? 's' : ''} to finish assigning
          </span>
          <ul>{poolWarnings.map((w) => <li key={w.entryId}>{w.message}</li>)}</ul>
        </div>
      )}

      {equipment.length > 0 && (
        <div className="faction-equipment">
          <span className="fe-label">Kill team equipment</span>
          <div className="fe-chips">
            {equipment.map((e) => (
              <span className="fe-chip" key={e}>{e}</span>
            ))}
          </div>
        </div>
      )}

      <div className="builder-hud">
        <div className="hud-metrics">
          {pointless ? (
            <>
              <span className="hud-total">{list.entries.length}</span>
              <span className="hud-lim">operatives</span>
            </>
          ) : (
            <>
              <span className={`hud-total${over ? ' over' : ''}`}>{list.total}</span>
              {list.pointsLimit != null && <span className="hud-lim">/ {list.pointsLimit}</span>}
              <span className="hud-unit">pts</span>
              {rules && dpUsed > 0 && <span className="hud-dp">{dpUsed}/{rules.dp} DP</span>}
            </>
          )}
        </div>
        {!pointless &&
          (issues.length === 0 ? (
            <span className="hud-status ok">Legal</span>
          ) : (
            <button type="button" className="hud-status bad" onClick={() => setShowAssistant(true)}>
              {issues.length} to fix
            </button>
          ))}
      </div>

      <div className="builder-tabs" role="tablist" aria-label="Builder panels">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'catalogue'}
          className={tab === 'catalogue' ? 'active' : ''}
          onClick={() => setTab('catalogue')}
        >
          Catalogue
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'list'}
          className={tab === 'list' ? 'active' : ''}
          onClick={() => setTab('list')}
        >
          Your list{list.entries.length > 0 ? ` · ${list.entries.length}` : ''}
        </button>
      </div>

      <div className={`builder-grid tab-${tab}`}>
        <div className="panel panel-catalogue" data-tour="catalogue">
          <div className="panel-head">
            <h3>Catalogue</h3>
            <div className="panel-head-actions">
              <input
                className="catalogue-search"
                placeholder="Search units…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              <select
                className="catalogue-sort"
                value={sort}
                onChange={(e) => setSort(e.target.value as 'role' | 'name' | 'points')}
                aria-label="Sort catalogue"
              >
                <option value="role">By role</option>
                <option value="name">A to Z</option>
                {!pointless && <option value="points">By points</option>}
              </select>
              {sort === 'role' && catalogueGroups.length > 1 && (
                <button type="button" className="collapse-all" onClick={toggleAllGroups}>
                  {allCollapsed ? 'Expand all' : 'Collapse all'}
                </button>
              )}
              <button type="button" className="collapse-all" onClick={() => setShowSearch(true)}>
                Rule search
              </button>
            </div>
          </div>
          <p className="panel-hint">
            {list.forces.length > 1 && activeForce ? (
              <>Adding to <b>{activeForce.factionName}</b>. </>
            ) : null}
            Tap a unit's name for its full datasheet.
          </p>
          {(legendsCount > 0 || crucibleCount > 0) && (
            <div className="tier-toggles">
              <span className="tier-label">Show</span>
              {legendsCount > 0 && (
                <label className="tier-toggle">
                  <input type="checkbox" checked={showLegends} onChange={(e) => setShowLegends(e.target.checked)} />
                  Legends <span className="tier-count">{legendsCount}</span>
                </label>
              )}
              {crucibleCount > 0 && (
                <label className="tier-toggle">
                  <input type="checkbox" checked={showCrucible} onChange={(e) => setShowCrucible(e.target.checked)} />
                  Crucible <span className="tier-count">{crucibleCount}</span>
                </label>
              )}
            </div>
          )}
          {contentToggles.length > 0 && (
            <div className="tier-toggles">
              <span className="tier-label">Optional</span>
              {contentToggles.map(([name, count]) => (
                <label key={name} className="tier-toggle">
                  <input
                    type="checkbox"
                    checked={enabledToggles.has(name)}
                    onChange={() => toggleContent(name)}
                  />
                  {name.replace(/^Show /, '')} <span className="tier-count">{count}</span>
                </label>
              ))}
            </div>
          )}
          <div className="scroll">
            {filtered.length === 0 ? (
              <p className="empty">No units match.</p>
            ) : sort !== 'role' ? (
              <div className="unit-flat">
                {sortedFlat.map((d, i) => renderUnit(d, i === 0))}
              </div>
            ) : (
              catalogueGroups.map((group) => (
                <details
                  className="role-group"
                  key={group.role}
                  open={!catCollapsed.has(group.role)}
                  onToggle={(e) => {
                    const open = (e.currentTarget as HTMLDetailsElement).open
                    setCatCollapsed((prev) => {
                      if (open === !prev.has(group.role)) return prev
                      const next = new Set(prev)
                      if (open) next.delete(group.role)
                      else next.add(group.role)
                      return next
                    })
                  }}
                >
                  <summary className="role-summary">
                    {group.role} <span className="role-count">{group.items.length}</span>
                  </summary>
                  {group.items.map((d) => renderUnit(d, d.id === filtered[0].id))}
                </details>
              ))
            )}
          </div>
        </div>

        <div className="panel panel-list">
          <div className="panel-head">
            <h3>Your list</h3>
            <div className="panel-head-actions">
              <span className="li-points">{list.entries.length} entries</span>
              {list.entries.length > 1 && (
                <div className="view-toggle" role="group" aria-label="List grouping">
                  <button type="button" className={listView === 'role' ? 'on' : ''} onClick={() => setListView('role')}>
                    Role
                  </button>
                  <button type="button" className={listView === 'custom' ? 'on' : ''} onClick={() => setListView('custom')}>
                    Custom
                  </button>
                </div>
              )}
              {list.entries.length > 0 && (
                <button type="button" className="play-btn" onClick={() => setShowPlay(true)}>
                  <span aria-hidden="true">▶</span> Play view
                </button>
              )}
            </div>
          </div>
          <div className="scroll">
            {list.entries.length === 0 && list.forces.length <= 1 ? (
              <p className="empty">No {unitWord} yet. Add some from the catalogue.</p>
            ) : list.forces.length > 1 ? (
              list.forces.map((f) => (
                <section className="force-section" key={f.id}>
                  <div className="force-section-head">
                    <span className="force-name">{f.factionName}</span>
                    <span className="force-count">
                      {list.entries.filter((e) => e.forceId === f.id).length}
                    </span>
                    <button
                      type="button"
                      className="entry-edit-link force-remove"
                      onClick={() => void dropForce(f.id)}
                      disabled={busy}
                    >
                      Remove force
                    </button>
                  </div>
                  {renderForceEntries(f.id)}
                </section>
              ))
            ) : (
              renderForceEntries(list.forces[0]?.id ?? activeForceId)
            )}
          </div>
        </div>
      </div>

      {detailId && (
        <UnitDetailModal
          datasheetId={detailId}
          presets={presetsBySheet.get(detailId) ?? []}
          onDetail={cacheDetail}
          onSavePreset={(name, optionIds, models, variants) => void savePreset(detailId, name, optionIds, models, variants)}
          onDeletePreset={(presetId) => void removePreset(presetId)}
          onAdd={(optionIds, models, variants) => {
            void add(detailId, optionIds, models, variants)
            setDetailId(null)
          }}
          onClose={() => setDetailId(null)}
        />
      )}

      {editEntry && (
        <UnitDetailModal
          key={editEntry.id}
          datasheetId={editEntry.datasheetId}
          initialSelected={editEntry.selectedOptions.map((o) => o.id)}
          initialModels={editEntry.modelCount}
          initialQuantities={Object.fromEntries(editEntry.selectedOptions.map((o) => [o.id, o.quantity]))}
          submitLabel="Save changes"
          busy={busy}
          presets={presetsBySheet.get(editEntry.datasheetId) ?? []}
          onDetail={cacheDetail}
          onSavePreset={(name, optionIds, models, variants) => void savePreset(editEntry.datasheetId, name, optionIds, models, variants)}
          onDeletePreset={(presetId) => void removePreset(presetId)}
          onSizeChange={(m, variants) => void runEntry(editEntry.id, () => setModelCount(list.id, editEntry.id, m, variants))}
          enhancement={
            pointless || list.detachments.length === 0
              ? undefined
              : {
                  value: editEntry.enhancementId,
                  options: enhancementsFor(editEntry),
                  onChange: (id) => void runEntry(editEntry.id, () => setEnhancement(list.id, editEntry.id, id)),
                }
          }
          onAdd={(optionIds, _models, variants) => {
            void run(() => updateEntry(list.id, editEntry.id, { selectedOptionIds: optionIds, variants }))
            setEditEntry(null)
          }}
          onClose={() => setEditEntry(null)}
        />
      )}

      {showAssistant && (
        <ListAssistant
          list={list}
          issues={issues}
          datasheets={datasheets}
          detachments={detachments}
          chosenDetachmentIds={chosenIds}
          busy={busy}
          onToggleDetachment={toggleDetachment}
          onSetWarlord={(entryId, value) => void run(() => setWarlord(list.id, entryId, value))}
          onRemoveEnhancement={(entryId) => void run(() => setEnhancement(list.id, entryId, null))}
          onRemoveEntry={(entryId) => void run(() => removeEntry(list.id, entryId))}
          onAdd={(datasheetId) => void add(datasheetId)}
          onAddWarlord={(datasheetId) => void addAsWarlord(datasheetId)}
          onGoCatalogue={() => {
            setTab('catalogue')
            setShowAssistant(false)
          }}
          onClose={() => setShowAssistant(false)}
        />
      )}

      {showPlay && <PlayView list={list} onClose={() => setShowPlay(false)} />}

      {showSearch && (
        <RuleSearch
          systemKey={list.gameSystemKey}
          onOpenDatasheet={(dsId) => {
            setShowSearch(false)
            setDetailId(dsId)
          }}
          onClose={() => setShowSearch(false)}
        />
      )}
    </section>
  )
}
