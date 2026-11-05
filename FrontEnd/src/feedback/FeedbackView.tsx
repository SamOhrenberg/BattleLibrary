import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../lib/api'
import {
  FEEDBACK_AREAS,
  getFeedback,
  getMyFeedback,
  submitFeedback,
  type FeedbackDetail,
  type FeedbackKind,
  type FeedbackStatus,
  type FeedbackSummary,
} from '../lib/feedback'
import { useToast } from '../ui/Toast'
import '../social/social.css'
import './feedback.css'

type Tab = 'send' | 'mine'

const KINDS: { id: FeedbackKind; label: string; placeholder: string }[] = [
  { id: 'Bug', label: 'Bug', placeholder: 'What happened, what you expected, and the steps to make it happen again.' },
  { id: 'Idea', label: 'Idea', placeholder: 'What would you like the app to do, and what problem would it solve?' },
  { id: 'Data', label: 'Data', placeholder: 'Which faction and unit, what looks wrong, and what it should be.' },
  { id: 'Other', label: 'Other', placeholder: 'Anything else on your mind.' },
]

// Match the server's validation in FeedbackEndpoints.Submit.
const MIN_TITLE = 5
const MIN_DETAILS = 10

const STATUS_LABEL: Record<FeedbackStatus, string> = {
  open: 'Open',
  done: 'Done',
  not_planned: 'Not planned',
  duplicate: 'Duplicate',
}

export function FeedbackView() {
  const [tab, setTab] = useState<Tab>('send')
  const [viewing, setViewing] = useState<string | null>(null)
  const [enabled, setEnabled] = useState(true)
  const [reports, setReports] = useState<FeedbackSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const list = await getMyFeedback()
      setEnabled(list.enabled)
      setReports(list.reports)
      setError(null)
    } catch {
      setError('Could not load your feedback.')
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  if (viewing) {
    return (
      <FeedbackDetailView
        id={viewing}
        onBack={() => {
          setViewing(null)
          void reload()
        }}
      />
    )
  }

  return (
    <section className="card">
      <h2>Feedback</h2>
      <div className="segmented" role="tablist">
        <button role="tab" aria-selected={tab === 'send'} className={tab === 'send' ? 'active' : ''} onClick={() => setTab('send')}>
          Send feedback
        </button>
        <button role="tab" aria-selected={tab === 'mine'} className={tab === 'mine' ? 'active' : ''} onClick={() => setTab('mine')}>
          Yours{reports && reports.length > 0 ? ` (${reports.length})` : ''}
        </button>
      </div>

      {error && <p className="builder-error">{error}</p>}

      <div className="community-body">
        {tab === 'send' && (
          <FeedbackForm
            enabled={enabled}
            onSent={async (report) => {
              await reload()
              setTab('mine')
              setViewing(report.id)
            }}
          />
        )}
        {tab === 'mine' && <FeedbackList reports={reports} onOpen={setViewing} onNew={() => setTab('send')} />}
      </div>
    </section>
  )
}

function FeedbackForm({ enabled, onSent }: { enabled: boolean; onSent: (report: FeedbackSummary) => void }) {
  const toast = useToast()
  const [kind, setKind] = useState<FeedbackKind>('Bug')
  const [title, setTitle] = useState('')
  const [area, setArea] = useState('')
  const [description, setDescription] = useState('')
  const [includeDevice, setIncludeDevice] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const trimmedTitle = title.trim()
  const trimmedDescription = description.trim()
  const titleShort = trimmedTitle.length < MIN_TITLE
  const detailsShort = trimmedDescription.length < MIN_DETAILS
  const valid = !titleShort && !detailsShort

  async function send() {
    setBusy(true)
    setError(null)
    try {
      const report = await submitFeedback({
        kind,
        title: trimmedTitle,
        description: trimmedDescription,
        area: area || null,
        device: includeDevice ? navigator.userAgent : null,
      })
      toast.push(`Thanks! Filed as #${report.issueNumber}.`, 'success')
      setTitle('')
      setDescription('')
      setArea('')
      onSent(report)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not send your feedback.')
    } finally {
      setBusy(false)
    }
  }

  if (!enabled) {
    return <p className="empty-note">Feedback isn't set up on this server yet.</p>
  }

  const placeholder = KINDS.find((k) => k.id === kind)?.placeholder

  return (
    <div className="profile-form">
      <div>
        <h3 className="section-title">What kind of feedback?</h3>
        <div className="segmented">
          {KINDS.map((k) => (
            <button key={k.id} type="button" className={kind === k.id ? 'active' : ''} aria-pressed={kind === k.id} onClick={() => setKind(k.id)}>
              {k.label}
            </button>
          ))}
        </div>
      </div>

      <label>
        Title
        <input
          className="search-box"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          maxLength={120}
          placeholder="A short summary"
        />
        {title && titleShort && <span className="fb-need">At least {MIN_TITLE} characters ({trimmedTitle.length}/{MIN_TITLE})</span>}
      </label>

      <label>
        <span>
          Area <span className="fb-optional">(optional)</span>
        </span>
        <select className="search-box" value={area} onChange={(e) => setArea(e.target.value)}>
          <option value="">Not sure</option>
          {FEEDBACK_AREAS.map((a) => (
            <option key={a} value={a}>
              {a}
            </option>
          ))}
        </select>
      </label>

      <label>
        Details
        <textarea
          className="fb-details"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={5000}
          placeholder={placeholder}
        />
        {description && detailsShort && (
          <span className="fb-need">At least {MIN_DETAILS} characters ({trimmedDescription.length}/{MIN_DETAILS})</span>
        )}
      </label>

      <label className="fb-check">
        <input type="checkbox" checked={includeDevice} onChange={(e) => setIncludeDevice(e.target.checked)} />
        Include my browser and device type
      </label>

      <p className="vis-hint">
        Feedback is posted publicly as an issue on the project's GitHub. Your username and email are not included,
        so leave out anything personal.
      </p>

      {error && <p className="builder-error">{error}</p>}

      <button type="button" className="btn btn-primary" onClick={send} disabled={busy || !valid}>
        {busy ? 'Sending…' : 'Send feedback'}
      </button>
    </div>
  )
}

function FeedbackList({
  reports,
  onOpen,
  onNew,
}: {
  reports: FeedbackSummary[] | null
  onOpen: (id: string) => void
  onNew: () => void
}) {
  if (reports === null) return <p className="hint">Loading…</p>

  if (reports.length === 0) {
    return (
      <div className="empty-note">
        <p>You haven't sent any feedback yet.</p>
        <button type="button" className="btn-sm primary" onClick={onNew}>
          Send feedback
        </button>
      </div>
    )
  }

  return (
    <ul className="people-list">
      {reports.map((r) => (
        <li key={r.id}>
          <button type="button" className="person-row fb-row" onClick={() => onOpen(r.id)}>
            <span className="person-main">
              <span className="person-name">{r.title}</span>
              <span className="person-sub">
                #{r.issueNumber} · {r.kind} · updated {shortDate(r.updatedAt)}
                {r.commentCount > 0 && ` · ${r.commentCount} ${r.commentCount === 1 ? 'reply' : 'replies'}`}
              </span>
            </span>
            <StatusPill status={r.status} />
          </button>
        </li>
      ))}
    </ul>
  )
}

function FeedbackDetailView({ id, onBack }: { id: string; onBack: () => void }) {
  const [detail, setDetail] = useState<FeedbackDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getFeedback(id)
      .then(setDetail)
      .catch(() => setError('Could not load this feedback.'))
  }, [id])

  return (
    <section className="card">
      <button type="button" className="back-link" onClick={onBack}>
        ← Your feedback
      </button>

      {error && <p className="builder-error">{error}</p>}
      {!detail && !error && <p className="hint">Loading…</p>}

      {detail && (
        <div className="community-body">
          <div className="fb-head">
            <h2>{detail.report.title}</h2>
            <StatusPill status={detail.report.status} />
          </div>
          <p className="person-sub">
            #{detail.report.issueNumber} · {detail.report.kind}
            {detail.report.area && ` · ${detail.report.area}`} · sent {shortDate(detail.report.createdAt)}
          </p>

          {detail.report.labels.length > 0 && (
            <div className="fb-labels">
              {detail.report.labels.map((l) => (
                <span key={l} className="status-pill">
                  {l}
                </span>
              ))}
            </div>
          )}

          {!detail.live && (
            <p className="vis-hint">Couldn't reach GitHub just now, so this shows the last known status.</p>
          )}

          {detail.description && (
            <div>
              <h3 className="section-title">What you sent</h3>
              <p className="fb-body">{detail.description}</p>
            </div>
          )}

          {detail.live && (
            <div>
              <h3 className="section-title">Updates</h3>
              {detail.comments.length === 0 ? (
                <p className="empty-note">No replies yet. Updates from the maintainer show up here.</p>
              ) : (
                <ol className="fb-thread">
                  {detail.comments.map((c) => (
                    <li key={c.id} className={`fb-comment${c.isMaintainer ? ' maintainer' : ''}`}>
                      <div className="fb-comment-meta">
                        <span className="fb-author">{c.author}</span>
                        {c.isMaintainer && <span className="status-pill s-maintainer">Maintainer</span>}
                        <span className="person-sub">{shortDate(c.createdAt)}</span>
                      </div>
                      <p className="fb-body">{c.body}</p>
                    </li>
                  ))}
                </ol>
              )}
            </div>
          )}

          <a className="btn-sm fb-github" href={detail.report.issueUrl} target="_blank" rel="noreferrer">
            View on GitHub
          </a>
        </div>
      )}
    </section>
  )
}

function StatusPill({ status }: { status: FeedbackStatus }) {
  return <span className={`status-pill fb-${status}`}>{STATUS_LABEL[status]}</span>
}

function shortDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}
