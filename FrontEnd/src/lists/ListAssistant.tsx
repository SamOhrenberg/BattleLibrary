import type { CatalogueDatasheet, CatalogueDetachment } from '../lib/catalogue'
import type { ArmyList, ListEntry } from '../lib/lists'
import type { ListIssue } from './validation'

const hasKw = (keywords: string[], kw: string) => keywords.some((k) => k.toLowerCase() === kw.toLowerCase())

// A guided "fix my list" sheet: each legality issue is shown with directions
// and one-tap actions that resolve it against the relevant units. The parent
// keeps the list state, so the sheet re-renders as issues clear.
interface Props {
  list: ArmyList
  issues: ListIssue[]
  datasheets: CatalogueDatasheet[]
  detachments: CatalogueDetachment[]
  chosenDetachmentIds: Set<string>
  busy?: boolean
  onToggleDetachment: (id: string) => void
  onSetWarlord: (entryId: string, value: boolean) => void
  onRemoveEnhancement: (entryId: string) => void
  onRemoveEntry: (entryId: string) => void
  onChangeQty: (entryId: string, quantity: number) => void
  onAdd: (datasheetId: string) => void
  onAddWarlord: (datasheetId: string) => void
  onGoCatalogue: () => void
  onClose: () => void
}

export function ListAssistant({
  list,
  issues,
  datasheets,
  detachments,
  chosenDetachmentIds,
  busy,
  onToggleDetachment,
  onSetWarlord,
  onRemoveEnhancement,
  onRemoveEntry,
  onChangeQty,
  onAdd,
  onAddWarlord,
  onGoCatalogue,
  onClose,
}: Props) {
  const byId = new Map(list.entries.map((e) => [e.id, e] as const))
  const entries = (ids?: string[]) => (ids ?? []).map((id) => byId.get(id)).filter((e): e is ListEntry => !!e)

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal assistant" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h3>List assistant</h3>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Close">×</button>
        </div>

        {issues.length === 0 ? (
          <p className="assistant-done">✓ This list is battle ready — nothing to fix.</p>
        ) : (
          <>
            <p className="assistant-intro">
              {issues.length} thing{issues.length === 1 ? '' : 's'} to sort out. Fixes apply straight away.
            </p>
            <div className="assistant-list">
              {issues.map((issue) => (
                <div className="assistant-issue" key={issue.code + (issue.entryIds?.[0] ?? '')}>
                  <div className="ai-head">
                    <span className="ai-msg">{issue.message}</span>
                  </div>
                  <p className="ai-fix">{issue.fix}</p>
                  <IssueActions
                    issue={issue}
                    entries={entries}
                    datasheets={datasheets}
                    detachments={detachments}
                    chosenDetachmentIds={chosenDetachmentIds}
                    busy={busy}
                    onToggleDetachment={onToggleDetachment}
                    onSetWarlord={onSetWarlord}
                    onRemoveEnhancement={onRemoveEnhancement}
                    onRemoveEntry={onRemoveEntry}
                    onChangeQty={onChangeQty}
                    onAdd={onAdd}
                    onAddWarlord={onAddWarlord}
                    onGoCatalogue={onGoCatalogue}
                  />
                </div>
              ))}
            </div>
          </>
        )}

        <div className="modal-actions">
          <button type="button" className="btn btn-primary" onClick={onClose}>Done</button>
        </div>
      </div>
    </div>
  )
}

interface ActionProps {
  issue: ListIssue
  entries: (ids?: string[]) => ListEntry[]
  datasheets: CatalogueDatasheet[]
  detachments: CatalogueDetachment[]
  chosenDetachmentIds: Set<string>
  busy?: boolean
  onToggleDetachment: (id: string) => void
  onSetWarlord: (entryId: string, value: boolean) => void
  onRemoveEnhancement: (entryId: string) => void
  onRemoveEntry: (entryId: string) => void
  onChangeQty: (entryId: string, quantity: number) => void
  onAdd: (datasheetId: string) => void
  onAddWarlord: (datasheetId: string) => void
  onGoCatalogue: () => void
}

function IssueActions({
  issue,
  entries,
  datasheets,
  detachments,
  chosenDetachmentIds,
  busy,
  onToggleDetachment,
  onSetWarlord,
  onRemoveEnhancement,
  onRemoveEntry,
  onChangeQty,
  onAdd,
  onAddWarlord,
  onGoCatalogue,
}: ActionProps) {
  switch (issue.code) {
    case 'empty': {
      const battleline = datasheets.filter((d) => hasKw(d.keywords, 'Battleline'))
      return (
        <>
          {battleline.length > 0 && (
            <ul className="ai-units ai-pick">
              {battleline.map((d) => (
                <li key={d.id}>
                  <span className="ai-unit-name">
                    {d.name}
                    {d.points != null && <span className="ai-sub"> {d.points} pts</span>}
                  </span>
                  <button type="button" className="add-btn" onClick={() => onAdd(d.id)} disabled={busy}>Add</button>
                </li>
              ))}
            </ul>
          )}
          <div className="ai-actions">
            <button type="button" className="btn" onClick={onGoCatalogue}>Browse the full catalogue</button>
          </div>
        </>
      )
    }

    case 'no-detachment':
      return (
        <div className="detachment-chips">
          {detachments.map((d) => (
            <button
              key={d.id}
              type="button"
              className={`det-chip${chosenDetachmentIds.has(d.id) ? ' on' : ''}`}
              onClick={() => onToggleDetachment(d.id)}
              disabled={busy}
            >
              {d.name} <span className="det-dp">{d.dp} DP</span>
            </button>
          ))}
        </div>
      )

    case 'dp-over':
      return (
        <div className="detachment-chips">
          {detachments
            .filter((d) => chosenDetachmentIds.has(d.id))
            .map((d) => (
              <button
                key={d.id}
                type="button"
                className="det-chip on"
                onClick={() => onToggleDetachment(d.id)}
                disabled={busy}
                title="Remove this detachment"
              >
                {d.name} <span className="det-dp">{d.dp} DP ✕</span>
              </button>
            ))}
        </div>
      )

    case 'no-warlord': {
      const candidates = entries(issue.entryIds)
      // Already have Characters in the list: set one as Warlord directly.
      if (candidates.length > 0) {
        return (
          <ul className="ai-units">
            {candidates.map((e) => (
              <li key={e.id}>
                <span className="ai-unit-name">{e.datasheetName}</span>
                <button type="button" className="add-btn" onClick={() => onSetWarlord(e.id, true)} disabled={busy}>
                  Make Warlord
                </button>
              </li>
            ))}
          </ul>
        )
      }
      // No Characters yet: offer the faction's Characters to add as Warlord.
      const chars = datasheets.filter((d) => hasKw(d.keywords, 'Character'))
      return (
        <>
          {chars.length === 0 ? (
            <p className="ai-note">No Characters in this faction's catalogue.</p>
          ) : (
            <ul className="ai-units ai-pick">
              {chars.map((d) => (
                <li key={d.id}>
                  <span className="ai-unit-name">
                    {d.name}
                    {d.points != null && <span className="ai-sub"> {d.points} pts</span>}
                  </span>
                  <button type="button" className="add-btn" onClick={() => onAddWarlord(d.id)} disabled={busy}>
                    Add as Warlord
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="ai-actions">
            <button type="button" className="btn" onClick={onGoCatalogue}>Browse the full catalogue</button>
          </div>
        </>
      )
    }

    case 'too-many-warlords':
      return (
        <ul className="ai-units">
          {entries(issue.entryIds).map((e) => (
            <li key={e.id}>
              <span className="ai-unit-name">★ {e.datasheetName}</span>
              <button type="button" className="btn" onClick={() => onSetWarlord(e.id, false)} disabled={busy}>
                Clear
              </button>
            </li>
          ))}
        </ul>
      )

    case 'too-many-enhancements':
      return (
        <ul className="ai-units">
          {entries(issue.entryIds).map((e) => (
            <li key={e.id}>
              <span className="ai-unit-name">
                {e.datasheetName}
                {e.enhancementName ? ` · ${e.enhancementName}` : ''}
              </span>
              <button type="button" className="btn" onClick={() => onRemoveEnhancement(e.id)} disabled={busy}>
                Remove enhancement
              </button>
            </li>
          ))}
        </ul>
      )

    case 'over-points':
    case 'rule-of-n': {
      // Show the relevant entries with quantity / remove controls. For points,
      // cap the list so the sheet stays short.
      const units = entries(issue.entryIds)
      const shown = issue.code === 'over-points' ? units.slice(0, 6) : units
      return (
        <ul className="ai-units">
          {shown.map((e) => (
            <li key={e.id}>
              <span className="ai-unit-name">
                {e.datasheetName} <span className="ai-sub">{e.lineTotal} pts · ×{e.quantity}</span>
              </span>
              <div className="ai-unit-actions">
                {e.quantity > 1 && (
                  <button type="button" className="btn" onClick={() => onChangeQty(e.id, e.quantity - 1)} disabled={busy}>
                    −1
                  </button>
                )}
                <button type="button" className="btn btn-danger" onClick={() => onRemoveEntry(e.id)} disabled={busy}>
                  Remove
                </button>
              </div>
            </li>
          ))}
        </ul>
      )
    }

    default:
      return null
  }
}
