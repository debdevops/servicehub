import { Lightbulb } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { columnHelp } from '../../content/columns'
import { explainFailure } from '../../lib/analyzer'
import { EntityCell } from './EntityCell'
import type { DeadLetter, ResolutionCause } from '../../lib/api/deadLetters'
import { formatAge, formatBytes, formatWhen } from '../../lib/format'
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
  compact = false,
  showOutcome = false,
  caption = 'Dead-lettered messages, newest first',
}: {
  rows: readonly DeadLetter[]
  /** Namespace id → name. A namespace is named beside the queue only when there is more than one. */
  namespaceNames?: ReadonlyMap<string, string>
  selection?: Selection
  compact?: boolean
  /** History (unit 6.11): "Waiting" becomes "Now" — still stuck, or gone since when and how, as recorded. */
  showOutcome?: boolean
  caption?: string
}) {
  const { search } = useLocation()
  const now = new Date()
  const showNamespace = (namespaceNames?.size ?? 0) > 1

  const openHref = (row: DeadLetter) => {
    const params = new URLSearchParams(search)
    params.set('tab', 'dlq')
    params.set('message', String(row.id))
    return `/?${params.toString()}`
  }

  const help = columnHelp.deadLetters
  const columns: Column<DeadLetter>[] = [
    { key: 'when', header: 'When', info: help.when, className: 'whitespace-nowrap', render: (r) => formatWhen(r.detectedAtUtc, now) },
    {
      key: 'queue',
      header: 'Queue or topic',
      info: help.where,
      width: 'min-w-[12rem] w-[24%]',
      render: (r) => <EntityCell entityName={r.entityName} entityType={r.entityType} topicName={r.topicName} note={showNamespace ? namespaceNames?.get(r.namespaceId) : undefined} />,
    },
    {
      // The widest column, on purpose: it is the one a person reads to decide what to do.
      key: 'reason',
      header: 'Failed because',
      info: help.failedBecause,
      width: 'w-[42%] min-w-[18rem]',
      render: (r) => <FailedBecause row={r} />,
    },
    { key: 'tries', header: 'Tries', info: help.tries, numeric: true, render: (r) => (r.deliveryCount > 0 ? r.deliveryCount : <span title="This cloud does not report how many times the message was delivered." className="text-[var(--color-text-muted)]">—</span>) },
    showOutcome
      ? { key: 'now', header: 'Now', info: help.now, className: 'min-w-[11rem]', render: (r) => <Now row={r} now={now} /> }
      : { key: 'waiting', header: 'Waiting', info: help.waiting, className: 'whitespace-nowrap', render: (r) => formatAge(r.detectedAtUtc, now) },
    { key: 'size', header: 'Size', info: help.size, numeric: true, className: 'whitespace-nowrap', render: (r) => formatBytes(r.sizeInBytes) },
    {
      key: 'open',
      header: 'Details',
      info: help.details,
      render: (r) => (
        <Link
          to={openHref(r)}
          aria-label={`Details of message ${r.messageId}`}
          className="whitespace-nowrap font-medium text-[var(--color-primary-700)] hover:underline"
        >
          Details →
        </Link>
      ),
    },
  ]

  return (
    <DataTable
      caption={caption}
      columns={columns}
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
      <p className="mt-1 flex items-start gap-1.5 text-[12px] text-[var(--color-text-muted)]" title="ServiceHub’s plain-English reading of the recorded reason — a suggestion, not something the cloud reported.">
        <Lightbulb className="mt-px h-3 w-3 shrink-0 text-[#d97706]" aria-label="Suggestion" />
        <span>{reading.headline}</span>
      </p>
    </div>
  )
}

/** How it left, in words that claim no more than was recorded: absence proves it is gone, not who removed it (R5). */
const causeWords: Readonly<Record<ResolutionCause, string>> = {
  replayedByServiceHub: 'Replayed by ServiceHub',
  purgedByServiceHub: 'Purged by ServiceHub',
  vanishedExternally: 'Left the queue — ServiceHub did not see how',
  declaredByOperator: 'Marked handled by a person',
  unknown: 'How it left was not recorded',
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
          <p className="text-[var(--color-text-muted)]">
            {row.resolutionCause ? causeWords[row.resolutionCause] : row.status === 'replayed' ? causeWords.replayedByServiceHub : row.status === 'discarded' ? 'Discarded on purpose' : row.status === 'replayFailed' ? 'A replay was tried and failed' : causeWords.unknown}
          </p>
        </div>
      )
  }
}
