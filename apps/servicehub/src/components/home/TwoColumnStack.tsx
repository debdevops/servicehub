import type { ReactNode } from 'react'

/**
 * Two columns that stack independently (plan §4 rule 4). Where a cloud's cards vary a lot in length —
 * Google Cloud's one failure reason beside AWS's many, two subscriptions beside 163 recorded dead
 * letters — a shared row leaves the shorter card's half of the row empty. Two columns that each stack
 * their own cards top-to-bottom never do: every AWS and Google Cloud view uses this instead of a row.
 */
export function TwoColumnStack({ left, right }: { left: ReactNode; right: ReactNode }) {
  return (
    <div className="grid items-start gap-3.5 lg:grid-cols-2">
      <div className="flex flex-col gap-3.5">{left}</div>
      <div className="flex flex-col gap-3.5">{right}</div>
    </div>
  )
}
