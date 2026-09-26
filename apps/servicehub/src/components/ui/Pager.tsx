import { ChevronLeft, ChevronRight } from 'lucide-react'
import { PAGE_SIZES } from '../../lib/pageSize'

/**
 * "1–25 of 128" and previous/next. Counts the set it is given: unfiltered that is everything, filtered
 * it is what the filter leaves — the caller says which by passing the right total, and `filtered`
 * words the sentence so the number is never mistaken for the whole.
 */
export function Pager({
  page,
  pageSize,
  total,
  filtered = false,
  onPage,
  onPageSize,
}: {
  page: number
  pageSize: number
  total: number
  filtered?: boolean
  onPage: (page: number) => void
  /** When given, the reader can choose rows per page. */
  onPageSize?: (size: number) => void
}) {
  if (total === 0) return null
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, total)
  const pages = Math.ceil(total / pageSize)

  return (
    <nav aria-label="Pages" className="flex items-center justify-between gap-3 px-4 py-3 text-sm text-[var(--color-text-muted)]">
      <span aria-live="polite">
        {first.toLocaleString()}–{last.toLocaleString()} of {total.toLocaleString()}
        {filtered && ' matching'}
      </span>
      <span className="flex items-center gap-1">
        {onPageSize && (
          <label className="mr-3 flex items-center gap-1.5 text-[12px]">
            Rows per page
            <select
              value={pageSize}
              onChange={(e) => onPageSize(Number(e.target.value))}
              className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-1.5 py-1 text-[12px] text-[var(--color-text)]"
            >
              {PAGE_SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
        )}
        <button
          type="button"
          aria-label="Previous page"
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
          className="rounded-lg border border-[var(--color-border)] p-1.5 enabled:hover:bg-[var(--color-surface-muted)] disabled:opacity-40"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="px-2 tabular">
          Page {page} of {pages}
        </span>
        <button
          type="button"
          aria-label="Next page"
          disabled={page >= pages}
          onClick={() => onPage(page + 1)}
          className="rounded-lg border border-[var(--color-border)] p-1.5 enabled:hover:bg-[var(--color-surface-muted)] disabled:opacity-40"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </span>
    </nav>
  )
}
