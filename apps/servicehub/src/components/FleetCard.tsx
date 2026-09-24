import { Link } from 'react-router-dom'
import type { CloudSummary } from '../lib/homeSummary'
import type { EntityKind } from '../lib/api/namespaces'

const kindWords: Record<EntityKind, [string, string]> = {
  queue: ['queue', 'queues'],
  topic: ['topic', 'topics'],
  subscription: ['subscription', 'subscriptions'],
}

/**
 * "{Cloud} at a glance" — only the kinds of thing this cloud actually has, in words ("8 with dead
 * letters"), never the abbreviation. Its link goes to Fleet Overview, which exists only with two
 * clouds, so the page passes `fleetHref` only then.
 */
export function FleetCard({ cloud, summary, fleetHref }: { cloud: string; summary: CloudSummary; fleetHref?: string }) {
  return (
    <section aria-label={`${cloud} at a glance`} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5">
      <h2 className="text-sm font-semibold text-[var(--color-text)]">{cloud} at a glance</h2>
      {summary.entityCounts.length === 0 ? (
        <p className="mt-2 text-sm text-[var(--color-text-muted)]">No queues or topics were found in this cloud yet.</p>
      ) : (
        <ul className="mt-3 flex flex-wrap gap-2">
          {summary.entityCounts.map(({ kind, count }) => (
            <li key={kind} className="rounded-full bg-[var(--color-surface-muted)] px-3 py-1 text-sm text-[var(--color-text)]">
              {count.toLocaleString()} {kindWords[kind][count === 1 ? 0 : 1]}
            </li>
          ))}
          {summary.withDeadLetters !== null && (
            <li
              className={`rounded-full px-3 py-1 text-sm ${summary.withDeadLetters > 0 ? 'bg-[var(--color-warning-light)]' : 'bg-[var(--color-success-light)]'} text-[var(--color-text)]`}
            >
              {summary.withDeadLetters.toLocaleString()} with dead letters
            </li>
          )}
        </ul>
      )}
      {fleetHref && (
        <Link to={fleetHref} className="mt-3 inline-block text-sm font-medium text-[var(--color-primary-700)] hover:underline">
          Compare with your other clouds →
        </Link>
      )}
    </section>
  )
}
