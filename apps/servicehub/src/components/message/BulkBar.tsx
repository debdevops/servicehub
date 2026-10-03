import { PanelRight } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'

/**
 * Appears when messages are selected. Its one primary action goes to Bulk Replay's PREVIEW — the table
 * itself never executes anything. Where a reason filter is on, it offers to select every match, not
 * just the page, and says how many that is.
 */
export function BulkBar({
  count,
  allMatching,
  matchingTotal,
  matchingLabel,
  canSelectAll,
  onSelectAll,
  onClear,
  onOpen,
  edge = 'top',
  allOnPage = false,
  onTogglePage,
  single = null,
}: {
  count: number
  allMatching: boolean
  matchingTotal: number
  matchingLabel: string | null
  canSelectAll: boolean
  onSelectAll: () => void
  onClear: () => void
  /** Called as Replay selected opens the preview: hands the selection to the modal. */
  onOpen?: () => void
  /** `top` sticks under the header; `bottom` closes the table, so the action is in reach at either end of a long page. */
  edge?: 'top' | 'bottom'
  /** Every row on this page is ticked. */
  allOnPage?: boolean
  onTogglePage?: () => void
  /** The one ticked message, when exactly one is: what View details opens. */
  single?: string | null
}) {
  const may = permission(useMe().data, 'Operator', 'replay these messages', { recover: true })
  const { search } = useLocation()
  const params = new URLSearchParams(search)
  params.set('modal', 'bulk-replay')
  // Bulk Replay is its own window: a message drawer left open beside the table is put away, not stacked behind it.
  ;['message', 'view', 'replay'].forEach((k) => params.delete(k))
  const shown = allMatching ? matchingTotal : count
  const none = shown === 0
  const canReplay = may.allowed && !none

  return (
    <div
      role="region"
      aria-label={edge === 'top' ? 'Selected messages' : 'Selected messages (bottom of the table)'}
      className={edge === 'top'
        ? `sticky z-20 flex flex-wrap items-center gap-3 rounded-t-xl border-b py-2.5 pl-4 pr-4 text-sm ${none ? 'border-[var(--color-border)] bg-[var(--color-surface-muted)]' : 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)] shadow-[0_4px_10px_rgba(2,132,199,0.10)]'}`
        : `flex flex-wrap items-center gap-3 rounded-b-xl border-t px-4 py-2.5 text-sm ${none ? 'border-[var(--color-border)] bg-[var(--color-surface-muted)]' : 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)]'}`}
      style={edge === 'top' ? { top: 'var(--header-height)' } : undefined}
    >
      {edge === 'top' && onTogglePage && (
        <input type="checkbox" aria-label="Select this whole page" checked={allOnPage} onChange={onTogglePage} className="block h-4 w-4" />
      )}
      <span className="font-medium">
        {none ? 'No messages selected' : `${shown.toLocaleString()} ${shown === 1 ? 'message' : 'messages'} selected`}
      </span>
      {canSelectAll && !allMatching && (
        <button type="button" onClick={onSelectAll} className="text-[var(--color-primary-700)] hover:underline">
          Select all {matchingTotal.toLocaleString()} {matchingLabel}
        </button>
      )}
      {!none && (
        <button type="button" onClick={onClear} className="text-[var(--color-text-muted)] hover:underline">
          Clear
        </button>
      )}
      <span className="ml-auto flex items-center gap-3">
        {edge === 'top' && (single ? (
          <Link to={`/?${detailParams(search, single)}`} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-medium hover:bg-[var(--color-surface-muted)]">
            <PanelRight className="h-4 w-4" aria-hidden="true" /> View details
          </Link>
        ) : (
          <button type="button" disabled title="Tick exactly one message to read it" className="inline-flex cursor-not-allowed items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-2 font-medium opacity-50">
            <PanelRight className="h-4 w-4" aria-hidden="true" /> View details
          </button>
        ))}
        <span className="text-xs text-[var(--color-text-muted)]">{!may.allowed ? <NotAllowed reason={may.reason} /> : none ? 'tick one or more below to replay them' : 'you’ll see a preview first'}</span>
        {canReplay ? (
          <Link
            to={`/?${params.toString()}`}
            onClick={onOpen}
            className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 font-medium text-white hover:bg-[var(--color-primary-700)]"
          >
            Replay selected…
          </Link>
        ) : (
          <button type="button" disabled className="cursor-not-allowed rounded-lg bg-[var(--color-primary-600)] px-4 py-2 font-medium text-white opacity-50">Replay selected…</button>
        )}
      </span>
    </div>
  )
}

/** The current filters, with one message opened beside the table. */
function detailParams(search: string, id: string): string {
  const p = new URLSearchParams(search)
  p.set('tab', 'dlq')
  p.set('message', id)
  return p.toString()
}
