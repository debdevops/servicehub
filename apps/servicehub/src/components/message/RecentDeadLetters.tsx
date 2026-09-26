import { Link } from 'react-router-dom'
import { namespaceTag, scopeQuery, type ScopeChoice } from '../provider/scopeChoice'
import { MessageTable } from './MessageTable'
import { useDeadLetters } from '../../hooks/useDeadLetters'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'

/**
 * Home's "Latest dead letters": the dead-letter table in compact form, five rows. Each row's *Open →*
 * lands on the Dead letters view with that message selected. Home says what is going on; Dead letters
 * is where you act — so there is one table, drawn twice, and no second implementation.
 * Draws nothing when there is nothing to show: an empty box is not information.
 */
export function RecentDeadLetters({ provider, namespaces, choice }: { provider: CloudProvider; namespaces: readonly Namespace[]; choice: ScopeChoice }) {
  const { data } = useDeadLetters({ provider, namespaceId: choice.ns?.id, environment: choice.env ?? undefined, status: 'active', page: 1, pageSize: 5 })
  if (!data || data.items.length === 0) return null

  return (
    <section aria-label="Latest dead letters" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="flex items-center justify-between px-4 py-3">
        <h2 className="text-sm font-semibold text-[var(--color-text)]">Latest dead letters</h2>
        <Link to={`/?tab=dlq${scopeQuery(choice)}`} className="text-sm font-medium text-[var(--color-primary-700)] hover:underline">
          See all {data.paging.total.toLocaleString()} →
        </Link>
      </div>
      <MessageTable
        rows={data.items}
        namespaceNames={new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))}
        compact
        caption="The five most recent dead-lettered messages"
      />
    </section>
  )
}
