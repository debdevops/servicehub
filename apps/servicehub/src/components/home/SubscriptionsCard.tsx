import { useQueries } from '@tanstack/react-query'
import { fetchEntities, type Namespace } from '../../lib/api/namespaces'
import { namespaceKeys } from '../../hooks/useNamespaces'
import { useLookAtDeadLetters } from '../../hooks/useDeadLetters'
import { subscriptionParts } from '../../lib/entities'
import { namespaceTag } from '../provider/scopeChoice'

/**
 * Subscriptions — the GCP-shaped slot the Queues card doesn't fit (plan §5.3): Pub/Sub has no message
 * counts, so there is nothing to bar-chart. What there is: which subscription, which topic it reads,
 * and which topic its dead letters land on (`Entity.deadLetterTargetName`) — and a *Look now* per row,
 * since nothing here is watched.
 */
export function SubscriptionsCard({ namespaces }: { namespaces: readonly Namespace[] }) {
  const several = namespaces.length > 1
  const nameOf = new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))
  const entities = useQueries({
    queries: namespaces.map((n) => ({ queryKey: namespaceKeys.entities(n.id), queryFn: () => fetchEntities(n.id) })),
  })
  const look = useLookAtDeadLetters()
  const rows = namespaces.flatMap((n, i) =>
    (entities[i]?.data?.entities ?? [])
      .filter((e) => e.kind === 'subscription')
      .map((e) => ({ namespaceId: n.id, entity: e })),
  )

  return (
    <section aria-label="Subscriptions" className="flex h-full flex-col overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-[13px]">
        <h2 className="text-[13.5px] font-bold text-[#1f2937]">Subscriptions</h2>
        <span className="text-[11px] text-[var(--color-text-muted)]">no message counts here — look to record</span>
      </div>
      {rows.length === 0 ? (
        <p className="p-4 text-[13px] text-[var(--color-text-muted)]">No subscription was found yet.</p>
      ) : (
        <ul className="divide-y divide-[#f3f4f6]">
          {rows.map(({ namespaceId, entity: e }) => {
            const parts = subscriptionParts(e.name)
            return (
              <li key={`${namespaceId}:${e.name}`} className="flex items-center gap-3 px-4 py-[9px]">
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-mono text-[12px] font-medium">{parts.subscription ?? e.name}</span>
                  <span className="block truncate text-[11px] text-[var(--color-text-muted)]">
                    topic {parts.entity}{e.deadLetterTargetName ? ` · dead letters on ${e.deadLetterTargetName}` : ''}{several ? ` · ${nameOf.get(namespaceId) ?? ''}` : ''}
                  </span>
                </span>
                <button
                  type="button"
                  disabled={look.isPending}
                  onClick={() => look.mutate([namespaceId])}
                  className="shrink-0 text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline disabled:opacity-60"
                >
                  {look.isPending ? 'Looking…' : 'Look now →'}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
