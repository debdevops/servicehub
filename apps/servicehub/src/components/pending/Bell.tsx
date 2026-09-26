import { Bell as BellIcon } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { usePendingWork } from '../../hooks/usePendingWork'
import { pendingRows } from '../../lib/pendingRows'
import { PendingWorkList } from './PendingWorkList'

/**
 * The bell (5.3): how many things are waiting for a person, across every cloud. It counts PENDING WORK, not unread
 * notifications — so it cannot drift, and opening it clears nothing. It is always on: no setting silences it (R7).
 */
export function Bell() {
  const pending = usePendingWork()
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const { pathname, search } = useLocation()
  const total = pending.data?.total ?? 0

  // A chosen action navigates; the panel gets out of the way.
  useEffect(() => setOpen(false), [pathname, search])
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => box.current && !box.current.contains(e.target as Node) && setOpen(false)
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('keydown', onKey) }
  }, [open])

  const rows = pendingRows(pending.data?.items ?? [])
  return (
    <div ref={box} className="relative">
      <button
        type="button"
        aria-label={total > 0 ? `Waiting for you: ${total}` : 'Nothing is waiting for you'}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((o) => !o)}
        className="relative rounded-lg p-2 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"
      >
        <BellIcon className="h-5 w-5" aria-hidden="true" />
        {total > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-[#f59e0b] px-1 text-[10.5px] font-bold text-white">
            {total > 99 ? '99+' : total}
          </span>
        )}
      </button>
      {open && (
        <div role="dialog" aria-label="Waiting for you" className="absolute right-0 top-11 z-40 w-[440px] max-w-[calc(100vw-24px)] overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-xl">
          <div className="flex items-center justify-between gap-2 border-b border-[var(--color-border)] px-5 py-3">
            <p className="flex items-center gap-2 text-[15px] font-bold">
              Waiting for you {total > 0 && <span className="rounded-full bg-[#fef3c7] px-2 text-xs font-bold text-[#92400e]">{total}</span>}
            </p>
            <p className="text-[11px] text-[var(--color-text-muted)]">clears when it’s resolved, not when you look</p>
          </div>
          {pending.isPending && <p role="status" className="px-5 py-4 text-sm text-[var(--color-text-muted)]">Reading what is waiting…</p>}
          {pending.isError && <p className="px-5 py-4 text-sm">ServiceHub couldn’t read what is waiting. <button type="button" className="font-medium text-[var(--color-primary-700)] hover:underline" onClick={() => void pending.refetch()}>Try again</button></p>}
          {pending.data && rows.length === 0 && <p className="px-5 py-5 text-sm text-[var(--color-text-muted)]">Nothing is waiting for you.</p>}
          {rows.length > 0 && <div className="max-h-[60vh] overflow-y-auto"><PendingWorkList rows={rows} now={new Date()} dense /></div>}
          <p className="flex items-center gap-2 border-t border-[var(--color-border)] bg-[var(--color-surface-muted)] px-5 py-2.5 text-[11.5px] text-[var(--color-text-muted)]">
            The bell is always on. Slack and Teams get the same items if they are set up.
            <Link to="/advanced/ledger?state=Waiting" className="ml-auto whitespace-nowrap font-semibold text-[var(--color-primary-700)] hover:underline">All in the Ledger ›</Link>
          </p>
        </div>
      )}
    </div>
  )
}
