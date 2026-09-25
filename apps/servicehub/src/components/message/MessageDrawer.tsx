import { columnHelp, type ColumnHelp } from '../../content/columns'
import { describeEntity } from '../../lib/entities'
import { InfoTip } from '../ui/InfoTip'
import { EntityCell } from './EntityCell'
import { useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Copy, Download, Maximize2, Minimize2, Play, Sparkles } from 'lucide-react'
import { Link } from 'react-router-dom'
import { OverlayFrame } from '../overlays/OverlayFrame'
import { OutcomeCard } from '../OutcomeCard'
import { Attribution } from '../Attribution'
import { WatchCard } from '../agent/WatchCard'
import { useReplays } from '../../hooks/useReplay'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import type { DeadLetterDetail } from '../../lib/api/deadLetters'
import { explainFailure } from '../../lib/analyzer'
import { formatAge, formatBytes, formatWhen } from '../../lib/format'

type Tab = 'overview' | 'body' | 'properties' | 'headers' | 'delivery'
const tabs: readonly { id: Tab; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'body', label: 'Body' },
  { id: 'properties', label: 'Properties' },
  { id: 'headers', label: 'Headers' },
  { id: 'delivery', label: 'Delivery' },
]

/**
 * One dead letter, read in place (`?message=<id>`). Its order is the design: what failed → why → the
 * body with the failing field marked → details → Replay. Metadata sits below the decision, never above.
 * `&view=full` is the same content in a wide modal; there is no message page.
 *
 * The Agent's opinion and "Approved by" are not drawn: nothing produces either yet (units 4.x, 2.9), and
 * an empty slot would read as "the agent has no opinion" (R5).
 */
export function MessageDrawer() {
  const [params, setParams] = useSearchParams()
  const raw = params.get('message')
  const id = raw !== null && /^\d+$/.test(raw) ? Number(raw) : null
  const full = params.get('view') === 'full'
  const { data, isPending, isError, error } = useDeadLetter(id)
  const [tab, setTab] = useState<Tab>('overview')

  if (raw === null) return null

  const edit = (mutate: (next: URLSearchParams) => void) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      mutate(next)
      return next
    }, { replace: true })
  // While the replay proposal is open on top, Esc belongs to it: closing both at once would lose the message.
  const replayOpen = params.get('modal') === 'replay'
  const close = () => { if (!replayOpen) edit((n) => { n.delete('message'); n.delete('view') }) }
  const backToSide = () => { if (!replayOpen) edit((n) => n.delete('view')) }
  const openReplay = () => edit((n) => n.set('modal', 'replay'))
  const expand = () => edit((n) => n.set('view', 'full'))

  const notFound = id === null || (isError && (error as { response?: { status?: number } })?.response?.status === 404)

  const expandButton = (
    <button
      type="button"
      onClick={full ? backToSide : expand}
      className="flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-medium text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"
    >
      {full ? <Minimize2 className="h-3.5 w-3.5" aria-hidden="true" /> : <Maximize2 className="h-3.5 w-3.5" aria-hidden="true" />}
      {full ? 'Back to side view' : 'Expand'}
    </button>
  )

  return (
    <OverlayFrame
      kind={full ? 'modal' : 'panel'}
      size={full ? 'wide' : 'drawer'}
      docked={!full}
      title={full ? 'Dead letter' : 'Message details'}
      onClose={full ? backToSide : close}
      actions={data ? expandButton : undefined}
    >
      {notFound ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">
          ServiceHub has no dead letter with this link. It may belong to a cloud you are not connected to.
        </p>
      ) : isError ? (
        <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read this message. Close it and try again.</p>
      ) : isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the message…</p>
      ) : (
        <Content detail={data} full={full} tab={tab} onTab={setTab} onReplay={openReplay} />
      )}
    </OverlayFrame>
  )
}

function Content({ detail, full, tab, onTab, onReplay }: { detail: DeadLetterDetail; full: boolean; tab: Tab; onTab: (t: Tab) => void; onReplay: () => void }) {
  const m = detail.item
  const now = new Date()
  const explanation = explainFailure(m.deadLetterReason, m.deadLetterErrorDescription, m.deliveryCount)
  const show = (t: Tab) => full || tab === t
  // The latest replay of this message, if any: while it is being watched the slot is the watch card, after that the outcome.
  const latest = useReplays({ dlqMessageId: m.id, pageSize: 1 }).data?.items[0]

  return (
    <div className="space-y-5">
      {!full && (
        <div role="tablist" aria-label="Message sections" className="-mt-1 flex gap-1 border-b border-[var(--color-border)]">
          {tabs.map((t) => (
            <button
              key={t.id}
              role="tab"
              type="button"
              aria-selected={tab === t.id}
              onClick={() => onTab(t.id)}
              className={`px-2.5 py-2 text-sm ${tab === t.id ? 'border-b-2 border-[var(--color-primary-600)] font-semibold text-[var(--color-text)]' : 'text-[var(--color-text-muted)]'}`}
            >
              {t.label}
            </button>
          ))}
        </div>
      )}

      {show('overview') && (
        <>
          <section aria-label="What failed">
            <span className="inline-block rounded-full bg-[var(--color-error-light)] px-3 py-1 text-sm font-semibold text-[var(--color-text)]">
              {m.deadLetterReason ?? 'Reason not recorded'}
            </span>
            <p className="mt-2 text-sm text-[var(--color-text)]">
              <span className="font-mono text-[13px]">{describeEntity(m.entityName, m.entityType, m.topicName).topic ? `${describeEntity(m.entityName, m.entityType, m.topicName).topic} › ` : ''}{describeEntity(m.entityName, m.entityType, m.topicName).name}</span>
              {m.deliveryCount > 0 ? ` · tried ${m.deliveryCount} ${m.deliveryCount === 1 ? 'time' : 'times'}` : ''} ·{' '}
              {formatBytes(m.sizeInBytes)}{detail.contentType ? ` · ${detail.contentType}` : ''} · set aside {formatAge(m.detectedAtUtc, now)} ago
            </p>
            {m.deadLetterErrorDescription && <p className="mt-1 text-sm text-[var(--color-text-muted)]">{m.deadLetterErrorDescription}</p>}
            {m.status === 'Resolved' && (
              <p className="mt-2 rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-sm">
                No longer in the dead-letter queue{detail.resolvedAt ? ` since ${formatWhen(detail.resolvedAt, now)}` : ''}. ServiceHub cannot say what became of it.
              </p>
            )}
          </section>

          <section aria-label="Why it failed">
            <h3 className="mb-1 flex items-center gap-2 text-sm font-semibold">
              Why it failed
              {explanation.recorded !== false && (
                <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-primary-50)] px-2 py-0.5 text-[11px] font-medium text-[var(--color-primary-700)]">
                  <Sparkles className="h-3 w-3" aria-hidden="true" /> Suggestion
                </span>
              )}
            </h3>
            <p className="text-sm text-[var(--color-text)]">{explanation.summary}</p>
            {detail.othersLikeIt > 0 && (
              <p className="mt-1 text-sm"><b>{detail.othersLikeIt} other {detail.othersLikeIt === 1 ? 'message' : 'messages'}</b> in this queue failed the same way.</p>
            )}
            {explanation.recorded !== false && <p className="mt-1 text-xs text-[var(--color-text-muted)]">A reading of the recorded reason, not something the cloud reported.</p>}
          </section>
        </>
      )}

      {full ? (
        <div className="grid gap-6 lg:grid-cols-2">
          <div className="space-y-5">
            <BodyBlock detail={detail} field={explanation.failingField} />
            <Headers detail={detail} />
          </div>
          <div className="space-y-5">
            <Properties json={detail.applicationPropertiesJson} />
            <Details detail={detail} now={now} />
            <Delivery detail={detail} now={now} />
            <OthersLikeIt detail={detail} />
          </div>
        </div>
      ) : (
        <>
          {(show('overview') || show('body')) && <BodyBlock detail={detail} field={explanation.failingField} />}
          {show('overview') && <Details detail={detail} now={now} />}
          {show('properties') && <Properties json={detail.applicationPropertiesJson} />}
          {show('headers') && <Headers detail={detail} />}
          {show('delivery') && <Delivery detail={detail} now={now} />}
        </>
      )}

      {show('overview') && latest && (latest.verification.status === 'watching' ? <WatchCard replay={latest} /> : <OutcomeCard replay={latest} />)}
      {show('overview') && latest && <Attribution actor={latest.actor} at={latest.replayedAt} />}

      {show('overview') && <ReplayHero active={m.status === 'Active'} onReplay={onReplay} />}
      {full && <FullFooter detail={detail} />}
    </div>
  )
}

/**
 * Replay is the one obviously-primary action: full width, its own row, 46px. It opens the proposal — it
 * never replays by itself. When it cannot, it stays, disabled, with the reason beside it (P4).
 * Bulk Replay is not drawn: it is unit 3.2, and a button that does nothing is worse than none.
 */
function ReplayHero({ active, onReplay }: { active: boolean; onReplay: () => void }) {
  return (
    <div>
      <button
        type="button"
        disabled={!active}
        onClick={onReplay}
        aria-describedby="replay-why"
        className="flex h-[46px] w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-[var(--color-primary-600)] to-[var(--color-primary-700)] text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
      >
        <Play className="h-4 w-4 fill-current" aria-hidden="true" /> Replay this message
      </button>
      <p id="replay-why" className="mt-1.5 text-center text-xs text-[var(--color-text-muted)]">
        {active ? 'You’ll see exactly what will happen before it runs.' : 'Already out of the dead-letter queue, so there is nothing to replay.'}
      </p>
    </div>
  )
}

/** One line of a JSON body, with keys, strings and numbers in their own colours. Plain text stays plain. */
function Colored({ line }: { line: string }) {
  const parts = line.split(/("(?:[^"\\]|\\.)*"\s*:?|-?\b\d+(?:\.\d+)?\b|\btrue\b|\bfalse\b|\bnull\b)/g)
  return (
    <>
      {parts.map((part, i) => {
        if (i % 2 === 0) return <span key={i}>{part}</span>
        const cls = /^"/.test(part) ? (part.trimEnd().endsWith(':') ? 'text-sky-300' : 'text-amber-200') : 'text-orange-300'
        return <span key={i} className={cls}>{part}</span>
      })}
    </>
  )
}

/**
 * The body, dark and monospace, with the failing field CALLED OUT INSIDE the block: the line that carries it is
 * marked, or — when the error names a field the body does not have — a `+ "field": missing` line sits where it
 * should be. You cannot judge a replay without seeing the payload. Only the stored preview is shown, and it says so.
 */
function BodyBlock({ detail, field }: { detail: DeadLetterDetail; field: string | null }) {
  const text = detail.bodyPreview
  const parses = text !== null && !detail.bodyIsPreview && isJson(text)
  const [raw, setRaw] = useState(false)
  if (text === null) {
    return (
      <section aria-label="Message body">
        <h3 className="mb-1 text-sm font-semibold">Message body</h3>
        <p className="text-sm text-[var(--color-text-muted)]">ServiceHub did not keep a body for this message.</p>
      </section>
    )
  }

  const shown = raw ? text : pretty(text, detail.bodyIsPreview)
  const lines = shown.split('\n')
  const marked = field ? lines.findIndex((l) => l.includes(`"${field}"`)) : -1
  // Where a missing field would have been: just before the closing brace of a formatted object.
  const insertAt = field && marked < 0 && !raw && parses && lines.at(-1)?.trim() === '}' ? lines.length - 1 : -1

  return (
    <section aria-label="Message body">
      <div className="mb-1 flex items-center justify-between">
        <h3 className="text-sm font-semibold">Message body</h3>
        <div className="flex items-center gap-3 text-xs">
          {parses && (
            <div role="group" aria-label="Body format" className="flex overflow-hidden rounded-lg border border-[var(--color-border)]">
              <button type="button" aria-pressed={!raw} onClick={() => setRaw(false)} className={`px-2 py-0.5 ${!raw ? 'bg-[var(--color-surface-muted)] font-semibold' : ''}`}>Formatted</button>
              <button type="button" aria-pressed={raw} onClick={() => setRaw(true)} className={`px-2 py-0.5 ${raw ? 'bg-[var(--color-surface-muted)] font-semibold' : ''}`}>Raw</button>
            </div>
          )}
          <button type="button" onClick={() => void navigator.clipboard?.writeText(text)} className="font-medium text-[var(--color-primary-700)] hover:underline">Copy</button>
        </div>
      </div>
      <pre className="max-h-72 overflow-auto rounded-xl bg-slate-900 p-3 font-mono text-xs leading-5 text-slate-100">
        {lines.map((line, i) => (
          <span key={i}>
            {i === insertAt && (
              <code className="-mx-3 block border-l-2 border-red-400 bg-red-500/20 px-3 text-red-200">{'  '}+ "{field}": missing</code>
            )}
            <code className={`block whitespace-pre-wrap ${i === marked ? '-mx-3 border-l-2 border-red-400 bg-red-500/20 px-3' : ''}`}>
              {line ? <Colored line={line} /> : ' '}
            </code>
          </span>
        ))}
        {field && (
          <code className="-mx-3 mt-1 block border-t border-dashed border-slate-600 px-3 pt-1 text-red-200">✕ missing required field: {field}</code>
        )}
      </pre>
      {detail.bodyIsPreview && (
        <p className="mt-1 text-xs text-[var(--color-text-muted)]">Only the first {text.length} characters of {formatBytes(detail.item.sizeInBytes)} are kept.</p>
      )}
    </section>
  )
}

function isJson(text: string): boolean {
  try {
    JSON.parse(text)
    return true
  } catch {
    return false
  }
}

/** "Others like it": how many share this reason here, and a link to see them — the failure groups, not a guess. */
function OthersLikeIt({ detail }: { detail: DeadLetterDetail }) {
  const m = detail.item
  const to = new URLSearchParams({ tab: 'dlq', entity: m.entityName })
  if (m.deadLetterReason) to.set('reason', m.deadLetterReason)
  return (
    <section aria-label="Others like it">
      <h3 className="mb-1 text-sm font-semibold">Others like it</h3>
      {detail.othersLikeIt === 0 ? (
        <p className="text-sm text-[var(--color-text-muted)]">No other dead letters in {m.entityName} failed this way.</p>
      ) : (
        <p className="text-sm">
          <b>{detail.othersLikeIt} more</b> in <span className="font-mono text-[13px]">{m.entityName}</span> failed the same way.{' '}
          <Link to={`/?${to.toString()}`} className="text-[var(--color-primary-700)] hover:underline">Show them ›</Link>
        </p>
      )}
    </section>
  )
}

/** The full view's footer: things you do with a message you are reading — copy its id or link, save its body. */
function FullFooter({ detail }: { detail: DeadLetterDetail }) {
  const m = detail.item
  const download = () => {
    if (detail.bodyPreview === null) return
    const url = URL.createObjectURL(new Blob([detail.bodyPreview], { type: detail.contentType ?? 'text/plain' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `${m.messageId}.txt`
    a.click()
    URL.revokeObjectURL(url)
  }
  const button = 'flex items-center gap-1 font-medium text-[var(--color-primary-700)] hover:underline disabled:opacity-50'
  return (
    <footer className="flex flex-wrap items-center gap-4 border-t border-[var(--color-border)] pt-3 text-sm">
      <button type="button" className={button} onClick={() => void navigator.clipboard?.writeText(m.messageId)}><Copy className="h-3.5 w-3.5" aria-hidden="true" /> Copy message ID</button>
      <button type="button" className={button} onClick={() => void navigator.clipboard?.writeText(window.location.href)}><Copy className="h-3.5 w-3.5" aria-hidden="true" /> Copy link</button>
      <button type="button" className={button} disabled={detail.bodyPreview === null} onClick={download}><Download className="h-3.5 w-3.5" aria-hidden="true" /> Download body</button>
      {detail.bodyIsPreview && <span className="text-xs text-[var(--color-text-muted)]">Saves the stored preview only.</span>}
    </footer>
  )
}

function pretty(text: string, isPreview: boolean): string {
  if (isPreview) return text // cut mid-way: re-indenting would need it to parse
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
  }
}

function Row({ label, help, children }: { label: string; help?: ColumnHelp; children: ReactNode }) {
  return (
    <>
      <dt className="text-[var(--color-text-muted)]">{label}{help && <InfoTip help={help} />}</dt>
      <dd className="min-w-0 break-all">{children}</dd>
    </>
  )
}

function Details({ detail, now }: { detail: DeadLetterDetail; now: Date }) {
  const m = detail.item
  return (
    <section aria-label="Details">
      <h3 className="mb-1 text-sm font-semibold">Details</h3>
      <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1 text-sm">
        <Row label="Message ID" help={columnHelp.drawer.messageId}>
          <span className="font-mono text-[13px]">{m.messageId}</span>{' '}
          <button type="button" aria-label="Copy message ID" onClick={() => void navigator.clipboard?.writeText(m.messageId)} className="align-middle text-[var(--color-text-muted)] hover:text-[var(--color-text)]">
            <Copy className="inline h-3.5 w-3.5" aria-hidden="true" />
          </button>
        </Row>
        <Row label="Queue or topic" help={columnHelp.drawer.where}><EntityCell size="sm" entityName={m.entityName} entityType={m.entityType} topicName={m.topicName} /></Row>
        <Row label="Enqueued" help={columnHelp.drawer.enqueued}>{formatWhen(m.enqueuedTimeUtc, now)}</Row>
        <Row label="Tries" help={columnHelp.drawer.tries}>{m.deliveryCount > 0 ? m.deliveryCount : '—'}</Row>
      </dl>
    </section>
  )
}

function Properties({ json }: { json: string | null }) {
  let entries: [string, unknown][] = []
  if (json) {
    try {
      const parsed: unknown = JSON.parse(json)
      if (parsed && typeof parsed === 'object') entries = Object.entries(parsed as Record<string, unknown>)
    } catch {
      /* stored text that isn't an object is shown as none */
    }
  }
  return (
    <section aria-label="Properties">
      <h3 className="mb-1 text-sm font-semibold">Properties</h3>
      {entries.length === 0 ? (
        <p className="text-sm text-[var(--color-text-muted)]">This message carried no application properties.</p>
      ) : (
        <dl className="grid grid-cols-[minmax(90px,140px)_1fr] gap-x-3 gap-y-1 text-sm">
          {entries.map(([k, v]) => (
            <Row key={k} label={k}>{typeof v === 'string' ? v : JSON.stringify(v)}</Row>
          ))}
        </dl>
      )}
    </section>
  )
}

function Headers({ detail }: { detail: DeadLetterDetail }) {
  const items: [string, string | null][] = [
    ['Message ID', detail.item.messageId],
    ['Correlation ID', detail.correlationId],
    ['Session ID', detail.sessionId],
    ['Content type', detail.contentType],
  ]
  return (
    <section aria-label="Headers">
      <h3 className="mb-1 text-sm font-semibold">Headers</h3>
      <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1 text-sm">
        {items.map(([k, v]) => (
          <Row key={k} label={k}>{v ?? <span className="text-[var(--color-text-muted)]">none</span>}</Row>
        ))}
      </dl>
    </section>
  )
}

function Delivery({ detail, now }: { detail: DeadLetterDetail; now: Date }) {
  const m = detail.item
  return (
    <section aria-label="Delivery">
      <h3 className="mb-1 text-sm font-semibold">Delivery</h3>
      <dl className="grid grid-cols-[150px_1fr] gap-x-3 gap-y-1 text-sm">
        <Row label="Delivery attempts">{m.deliveryCount}</Row>
        <Row label="Enqueued" help={columnHelp.drawer.enqueued}>{formatWhen(m.enqueuedTimeUtc, now)}</Row>
        <Row label="First seen set aside">{formatWhen(m.detectedAtUtc, now)}</Row>
        <Row label="Recorded reason">{m.deadLetterReason ?? 'none'}</Row>
      </dl>
      <p className="mt-2 text-xs text-[var(--color-text-muted)]">
        The clouds do not report when a message was set aside; this is when ServiceHub first saw it there.
      </p>
    </section>
  )
}
