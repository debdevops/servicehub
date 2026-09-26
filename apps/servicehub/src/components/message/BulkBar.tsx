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
}) {
  const may = permission(useMe().data, 'Operator', 'replay these messages', { recover: true })
  const { search } = useLocation()
  const params = new URLSearchParams(search)
  params.set('modal', 'bulk-replay')
  const shown = allMatching ? matchingTotal : count

  return (
    <div role="region" aria-label="Selected messages" className="sticky z-20 flex flex-wrap items-center gap-3 rounded-t-xl border-b border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-2.5 text-sm shadow-[0_4px_10px_rgba(2,132,199,0.10)]" style={{ top: 'var(--header-height)' }}>
      <span className="font-medium">
        {shown.toLocaleString()} {shown === 1 ? 'message' : 'messages'} selected
      </span>
      {canSelectAll && !allMatching && (
        <button type="button" onClick={onSelectAll} className="text-[var(--color-primary-700)] hover:underline">
          Select all {matchingTotal.toLocaleString()} {matchingLabel}
        </button>
      )}
      <button type="button" onClick={onClear} className="text-[var(--color-text-muted)] hover:underline">
        Clear
      </button>
      <span className="ml-auto flex items-center gap-3">
        <span className="text-xs text-[var(--color-text-muted)]">{may.allowed ? 'you’ll see a preview first' : <NotAllowed reason={may.reason} />}</span>
        {may.allowed ? (
          <Link
            to={`/?${params.toString()}`}
            onClick={onOpen}
            className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 font-medium text-white hover:bg-[var(--color-primary-700)]"
          >
            Replay selected…
          </Link>
        ) : (
          <button type="button" disabled className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 font-medium text-white opacity-50">Replay selected…</button>
        )}
      </span>
    </div>
  )
}
