import { useEffect, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { systemName } from '../lib/gameSystems'
import type { ArmyList, ListEntry } from '../lib/lists'
import { OverflowMenu } from '../ui/OverflowMenu'
import { entryTree } from './entryTree'

// A read-optimized, printable view of the list: one card per unit, grouped by
// section, with attached units kept together.
// `context` adds a lead-in to the subtitle, e.g. the live game's name and round.
export function PlayView({ list, context, onClose }: { list: ArmyList; context?: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const { sections } = useMemo(() => entryTree(list), [list])
  const multi = sections.length > 1
  const sub = [
    context,
    list.factionName || systemName(list.gameSystemKey),
    list.detachment,
    list.total > 0 ? `${list.total} pts` : null,
  ].filter(Boolean)

  // Portaled to body so a themed .card can't anchor or clip the full-screen view.
  return createPortal(
    <div className="play-view">
      <header className="play-head">
        <button type="button" className="play-back" aria-label="Close play view" onClick={onClose}>
          <span aria-hidden="true">‹</span>
        </button>
        <div className="play-title">
          <h2>{list.name}</h2>
          <span className="play-sub">{sub.join(' · ')}</span>
        </div>
        <div className="play-actions">
          <OverflowMenu label="More play view actions" items={[{ label: 'Print', onSelect: () => window.print() }]} />
        </div>
      </header>

      {list.notes && <p className="play-notes">{list.notes}</p>}

      <div className="play-cards">
        {sections.map((section) => (
          <div className="play-force-section" key={section.force.id}>
            {multi && <h3 className="play-force">{section.force.name}</h3>}
            <div className="play-group-cards">
              {section.items.map((item) =>
                item.attached.length > 0 ? (
                  <div className="play-attached" key={item.entry.id}>
                    <span className="play-attached-label">Attached unit</span>
                    {item.attached.map((a) => (
                      <PlayCard key={a.id} entry={a} />
                    ))}
                    <PlayCard entry={item.entry} />
                  </div>
                ) : (
                  <PlayCard key={item.entry.id} entry={item.entry} />
                ),
              )}
            </div>
          </div>
        ))}
        {list.entries.length === 0 && <p className="hint">No units in this list.</p>}
      </div>
    </div>,
    document.body,
  )
}

function PlayCard({ entry }: { entry: ListEntry }) {
  const wargear = (entry.wargear ?? '').split('\n').map((w) => w.trim()).filter(Boolean)
  return (
    <article className="play-card">
      <div className="pc-head">
        <h4>
          {entry.name}
          {entry.modelCount != null && entry.modelCount > 1 && (
            <span className="pc-models"> · {entry.modelCount} models</span>
          )}
        </h4>
        {entry.points != null && <span className="pc-pts">{entry.points} pts</span>}
      </div>

      {(entry.isWarlord || entry.enhancement) && (
        <div className="pc-badges">
          {entry.isWarlord && <span className="badge-warlord">★ Warlord</span>}
          {entry.enhancement && <span className="badge-enh">{entry.enhancement}</span>}
        </div>
      )}

      {wargear.length > 0 && (
        <ul className="pc-wargear-list">
          {wargear.map((w, i) => (
            <li key={i}>{w}</li>
          ))}
        </ul>
      )}
      {entry.notes && <div className="pc-note">{entry.notes}</div>}
    </article>
  )
}
