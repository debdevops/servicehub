import { Link, useLocation } from 'react-router-dom'
import type { DeadLetter } from '../../lib/api/deadLetters'
import { formatAge, formatBytes, formatWhen } from '../../lib/format'
import { DataTable, type Column, type Selection } from '../ui/DataTable'

/**
 * Dead letters as a table: When · Queue · Failed because · Tries · Waiting · Size · Open.
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
  caption = 'Dead-lettered messages, newest first',
}: {
  rows: readonly DeadLetter[]
  /** Namespace id → name. A namespace is named beside the queue only when there is more than one. */
  namespaceNames?: ReadonlyMap<string, string>
  selection?: Selection
  compact?: boolean
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

  const columns: Column<DeadLetter>[] = [
    { key: 'when', header: 'When', className: 'whitespace-nowrap', render: (r) => formatWhen(r.detectedAtUtc, now) },
    {
      key: 'queue',
      header: 'Queue',
      render: (r) => (
        <>
          <span className="font-mono text-[13px]">{r.entityName}</span>
          {showNamespace && <span className="block text-xs text-[var(--color-text-muted)]">{namespaceNames?.get(r.namespaceId)}</span>}
        </>
      ),
    },
    {
      key: 'reason',
      header: 'Failed because',
      render: (r) => (
        <>
          <span className="inline-block rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-xs font-medium text-[var(--color-text)]">
            {r.deadLetterReason ?? 'No reason recorded'}
          </span>
          {r.deadLetterErrorDescription && (
            <span className="mt-1 block max-w-md truncate text-xs text-[var(--color-text-muted)]" title={r.deadLetterErrorDescription}>
              {r.deadLetterErrorDescription}
            </span>
          )}
        </>
      ),
    },
    { key: 'tries', header: 'Tries', numeric: true, render: (r) => r.deliveryCount },
    { key: 'waiting', header: 'Waiting', className: 'whitespace-nowrap', render: (r) => formatAge(r.detectedAtUtc, now) },
    { key: 'size', header: 'Size', numeric: true, className: 'whitespace-nowrap', render: (r) => formatBytes(r.sizeInBytes) },
    {
      key: 'open',
      header: 'Open',
      render: (r) => (
        <Link
          to={openHref(r)}
          aria-label={`Open message ${r.messageId}`}
          className="whitespace-nowrap font-medium text-[var(--color-primary-700)] hover:underline"
        >
          Open →
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
