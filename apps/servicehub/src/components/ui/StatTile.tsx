import { Link } from 'react-router-dom'

/**
 * One number and what it is. Every tile is a link — a number nobody can act on is decoration.
 *
 * `value` is null when the cloud cannot count: the tile then says so in words instead of a
 * number (R5). A tile with no source at all is not rendered by the page, never shown as 0.
 */
export function StatTile({
  label,
  value,
  note,
  unavailable,
  to,
  action,
}: {
  label: string
  value: number | null
  /** The footer line — what the number is a reading of (for example, "right now"). */
  note: string
  /** What to say instead of a number when `value` is null. */
  unavailable: string
  to: string
  action: string
}) {
  return (
    <div className="flex flex-col rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5">
      <div className="text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">{label}</div>
      {value === null ? (
        <div className="mt-2 text-sm text-[var(--color-text-muted)]">{unavailable}</div>
      ) : (
        <div className="tabular mt-1 text-3xl font-semibold text-[var(--color-text)]">{value.toLocaleString()}</div>
      )}
      <div className="mt-1 text-xs text-[var(--color-text-muted)]">{note}</div>
      <Link to={to} className="mt-3 text-sm font-medium text-[var(--color-primary-700)] hover:underline">
        {action} →
      </Link>
    </div>
  )
}
