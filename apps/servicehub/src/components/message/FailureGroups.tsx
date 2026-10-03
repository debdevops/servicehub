import type { DeadLetterPage } from '../../lib/api/deadLetters'

export const NO_REASON = '__none__'

/**
 * One chip per RECORDED reason, with its count. The counts add up to the tab (the API counts them over
 * everything the other filters leave), and choosing one filters the table — the pager then says
 * "matching". A reason is what the cloud or the application wrote down: a fact, not a category
 * ServiceHub guessed. Rare reasons fold into a plain "other" line that names how many kinds.
 */
export function FailureGroups({
  groups,
  other,
  selected,
  onSelect,
}: {
  groups: DeadLetterPage['groups']
  other: DeadLetterPage['otherReasons']
  /** The reason filter now: a reason, NO_REASON, or null for none. */
  selected: string | null
  onSelect: (reason: string | null) => void
}) {
  if (groups.length === 0) return null

  return (
    <section aria-label="Why messages failed" className="mb-4">
      <ul className="flex flex-wrap gap-2">
        {groups.map((g) => {
          const key = g.reason ?? NO_REASON
          const active = selected === key
          return (
            <li key={key}>
              <button
                type="button"
                aria-pressed={active}
                onClick={() => onSelect(active ? null : key)}
                className={`rounded-full border px-3 py-1.5 text-sm ${active ? 'border-[var(--color-primary-600)] bg-[var(--color-primary-50)] font-medium' : 'border-[var(--color-border)] bg-[var(--color-surface)] hover:bg-[var(--color-surface-muted)]'}`}
              >
                <span className="tabular font-semibold">{g.count.toLocaleString()}</span>{' '}
                {g.reason ?? 'No reason recorded'}
              </button>
            </li>
          )
        })}
        {other && (
          <li className="px-2 py-1.5 text-sm text-[var(--color-text-muted)]">
            <span className="tabular font-semibold">{other.count.toLocaleString()}</span> more, across {other.kinds} other reasons
          </li>
        )}
      </ul>
    </section>
  )
}
