import { Lightbulb, Play } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { Link, useLocation } from 'react-router-dom'
import { columnHelp } from '../../content/columns'
import { explainFailure } from '../../lib/analyzer'
import { EntityCell } from './EntityCell'
import type { DeadLetter } from '../../lib/api/deadLetters'
import { formatAge, formatAgo, formatBytes, formatWhen } from '../../lib/format'
import { resolutionWords } from '../../lib/resolutionWords'
import { DataTable, type Column, type Selection } from '../ui/DataTable'

/**
 * Dead letters as a table: When · Queue or topic · Failed because · Tries · Waiting · Size · Details.
 *
 * "Failed because" is the reason the cloud or the application RECORDED, and the error text that came
 * with it. It is a fact from the message, not a category ServiceHub guessed — a guess, when there is
 * one, is badged as one where the message is opened (R3).
 */
export function MessageTable({
  rows,
  namespaceNames,
  selection,
  compact = true,
  showOutcome = false,
  glance = false,
  caption = 'Dead-lettered messages, newest first',
}: {
  rows: readonly DeadLetter[]
  /** Namespace id → name. A namespace is named beside the queue only when there is more than one. */
  namespaceNames?: ReadonlyMap<string, string>
  selection?: Selection
  compact?: boolean
  /** History (unit 6.11): "Waiting" becomes "Now" — still stuck, or gone since when and how, as recorded. */
  showOutcome?: boolean
  /** The small Home widget: half a page wide, so only what a quick look needs — when, where, why — and one link. */
  glance?: boolean
  caption?: string
}) {
  const { search } = useLocation()
  const now = new Date()
  const showNamespace = (namespaceNames?.size ?? 0) > 1

  const may = permission(useMe().data, 'Operator', 'replay these messages', { recover: true })
  const openHref = (row: DeadLetter, modal?: 'replay') => {
    const params = new URLSearchParams(search)
    params.set('tab', 'dlq')
    // A row's Replay opens the Replay modal by itself (`replay=`); only Details opens the message drawer (`message=`).
    if (modal) {
      // Put away any message drawer that is open for another row: the Replay window is the only thing this click opens.
      params.delete('message')
      params.delete('view')
      params.set('replay', String(row.id))
      params.set('modal', modal)
    } else {
      params.set('message', String(row.id))
    }
    return `/?${params.toString()}`
  }

  const help = columnHelp.deadLetters
  const columns: Column<DeadLetter>[] = [
    { key: 'when', header: 'When', info: help.when, className: 'whitespace-nowrap', render: (r) => (<><span className="block font-medium">{formatWhen(r.detectedAtUtc, now)}</span><span className="block text-xs text-[var(--color-text-muted)]">{formatAgo(r.detectedAtUtc, now)}</span></>) },
    {
      key: 'queue',
      header: 'Queue or topic',
      info: help.where,
      width: 'min-w-[10rem] w-[22%]',
      render: (r) => (
        <>
          <EntityCell entityName={r.entityName} entityType={r.entityType} topicName={r.topicName} note={showNamespace ? namespaceNames?.get(r.namespaceId) : undefined} />
          {/* Which message, in the cloud's own words — the queue alone cannot tell two rows apart. */}
          <p title={`Message ID: ${r.messageId}`} className={`mt-1 ${glance ? 'max-w-[7rem]' : 'max-w-[12rem]'} truncate font-mono text-[11px] text-[var(--color-text-muted)]`}>{r.messageId}</p>
        </>
      ),
    },
    {
      // The widest column, on purpose: it is the one a person reads to decide what to do.
      key: 'reason',
      header: 'Failed because',
      info: help.failedBecause,
      width: 'w-[44%] min-w-[14rem]',
      render: (r) => <FailedBecause row={r} />,
    },
    { key: 'tries', header: 'Tries', info: help.tries, numeric: true, render: (r) => (r.deliveryCount > 0 ? r.deliveryCount : <span title="This cloud does not report how many times the message was delivered." className="text-[var(--color-text-muted)]">—</span>) },
    showOutcome
      ? { key: 'now', header: 'Now', info: help.now, className: 'min-w-[11rem]', render: (r) => <Now row={r} now={now} /> }
      : { key: 'waiting', header: 'Waiting', info: help.waiting, className: 'whitespace-nowrap', render: (r) => formatAge(r.detectedAtUtc, now) },
    { key: 'size', header: 'Size', info: help.size, numeric: true, className: 'whitespace-nowrap', render: (r) => formatBytes(r.sizeInBytes) },
    {
      key: 'open',
      header: 'Actions',
      info: help.details,
      render: (r) => (
        <span className="flex items-center gap-2">
          <Link
            to={openHref(r)}
            aria-label={`Details of message ${r.messageId}`}
            className="whitespace-nowrap rounded-lg bg-[var(--color-primary-50)] px-3 py-1.5 font-medium text-[var(--color-primary-700)] hover:bg-[var(--color-primary-100)]"
          >
            Details →
          </Link>
          {!glance && !showOutcome && r.status === 'active' && (
            may.allowed ? (
              <Link to={openHref(r, 'replay')} aria-label={`Replay message ${r.messageId}`} className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-lg bg-[var(--color-primary-50)] px-3 py-1.5 font-medium text-[var(--color-primary-700)] hover:bg-[var(--color-primary-100)]">
                <Play className="h-3.5 w-3.5" aria-hidden="true" /> Replay
              </Link>
            ) : (
              <button type="button" disabled title={may.reason ?? 'You cannot replay these messages'} aria-label={`Replay message ${r.messageId}`} className="inline-flex cursor-not-allowed items-center gap-1.5 whitespace-nowrap rounded-lg bg-[var(--color-surface-muted)] px-3 py-1.5 font-medium text-[var(--color-text-muted)]">
                <Play className="h-3.5 w-3.5" aria-hidden="true" /> Replay
              </button>
            )
          )}
        </span>
      ),
    },
  ]

  // The glance keeps four columns, lets the wide ones shrink, and offers Details only — the full list has the rest.
  const shown = glance
    ? columns
        .filter((c) => ['when', 'queue', 'reason', 'open'].includes(c.key))
        .map((c) => (c.key === 'queue' ? { ...c, width: 'min-w-[7rem] w-[30%]' } : c.key === 'reason' ? { ...c, width: 'min-w-[9rem] w-[50%]' } : c))
    : columns

  return (
    <DataTable
      caption={caption}
      columns={shown}
      rows={rows}
      rowKey={(r) => String(r.id)}
      selection={selection}
      compact={compact}
      rowLabel={(r) => `Select message ${r.messageId}`}
    />
  )
}

/**
 * The three things a person needs to decide, in the order they read them: the recorded reason (a fact), the error text that
 * came with it (a fact, wrapped rather than cut off), and ServiceHub's plain-English reading (a suggestion — marked as one, R3).
 * Where the cloud gave no error text it says so instead of leaving a gap.
 */
function FailedBecause({ row }: { row: DeadLetter }) {
  // Some clouds move a message to the dead-letter queue by policy and record no reason at all. Saying "no reason, no text, no
  // reading" three times helps nobody, so this says the one thing that is known and true: it was set aside automatically after
  // its deliveries ran out, and where to look next.
  if (!row.deadLetterReason && !row.deadLetterErrorDescription) {
    // Some clouds do not report how many times a message was delivered (0 means "not reported", never "never delivered").
    const after = row.deliveryCount > 0 ? ` after ${row.deliveryCount} ${row.deliveryCount === 1 ? 'delivery' : 'deliveries'} that were never completed` : ' after its deliveries ran out'
    return (
      <div className="min-w-0">
        <span className="inline-block rounded-full bg-[var(--color-surface-muted)] px-2.5 py-0.5 text-xs font-semibold text-[var(--color-text-muted)]">Reason not recorded</span>
        <p className="mt-1 text-[12.5px] text-[var(--color-text)]">
          Set aside automatically{after}. This cloud does not record why — open Details to read the message itself.
        </p>
      </div>
    )
  }

  const reading = explainFailure(row.deadLetterReason, row.deadLetterErrorDescription, row.deliveryCount)
  return (
    <div className="min-w-0">
      <span className="inline-block rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-xs font-semibold text-[#b91c1c]">
        {row.deadLetterReason ?? 'Reason not recorded'}
      </span>
      {row.deadLetterErrorDescription ? (
        <p className="mt-1 line-clamp-2 text-[12.5px] text-[var(--color-text)]" title={row.deadLetterErrorDescription}>{row.deadLetterErrorDescription}</p>
      ) : (
        <p className="mt-1 text-[12.5px] italic text-[var(--color-text-muted)]">The cloud gave no error text for this one.</p>
      )}
      {reading.headline !== 'No reading available — see the recorded reason' && <p className="mt-0.5 flex items-start gap-1.5 text-[12px] text-[var(--color-text-muted)]" title="ServiceHub’s plain-English reading of the recorded reason — a suggestion, not something the cloud reported.">
        <Lightbulb className="mt-px h-3 w-3 shrink-0 text-[#d97706]" aria-label="Suggestion" />
        <span>{reading.headline}</span>
      </p>}
    </div>
  )
}

function Now({ row, now }: { row: DeadLetter; now: Date }) {
  switch (row.status) {
    case 'active':
      return <span className="whitespace-nowrap">Still stuck · {formatAge(row.detectedAtUtc, now)}</span>
    case 'replaying':
    case 'purging':
      return <span className="whitespace-nowrap">{row.status === 'replaying' ? 'Being replayed' : 'Being purged'} now</span>
    case 'archived':
      return <span className="text-[var(--color-text-muted)]">Its namespace was removed</span>
    default:
      return (
        <div className="text-[12.5px]">
          <p className="font-medium">No longer in the queue{row.resolvedAt ? ` since ${formatWhen(row.resolvedAt, now)}` : ''}</p>
          <p className="text-[var(--color-text-muted)]">{resolutionWords(row)}</p>
        </div>
      )
  }
}
