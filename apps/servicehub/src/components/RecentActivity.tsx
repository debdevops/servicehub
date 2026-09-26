import { useAudit } from '../hooks/useIdentity'
import { useStreamStatus } from '../hooks/useEventStream'
import { Attribution } from './Attribution'
import type { CloudProvider, EnvironmentKind } from '../lib/api/namespaces'
import { formatWhen } from '../lib/format'

const words: Readonly<Record<string, string>> = {
  'Namespace.Connect': 'Connected a namespace',
  'Namespace.Remove': 'Removed a namespace',
  'Replay.Message': 'Replayed a message',
  'DeadLetters.Look': 'Looked at dead letters',
}

/**
 * Recent Activity: what has been recorded, newest first, from the durable audit trail. It is labelled "Live" only
 * while the event stream is actually open — and when it is not, it says the list is history, and the list is still
 * right. Events do not appear here by themselves: they make the list look again (a stream is a hint, not a feed).
 * No read/unread: that is the bell's job, and the bell counts work waiting for a person.
 */
export function RecentActivity({ provider, namespaceId, environment }: { provider?: CloudProvider; namespaceId?: string; environment?: EnvironmentKind }) {
  const status = useStreamStatus()
  const { data, isPending, isError } = useAudit({ pageSize: 5, provider, namespaceId, environment })
  const now = new Date()

  return (
    <section aria-label="Recent activity" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3">
      <header className="mb-1 flex items-center justify-between">
        <h2 className="text-[13.5px] font-bold text-[#1f2937]">Recent activity</h2>
        {status === 'live' ? (
          <span className="flex items-center gap-1.5 text-xs font-medium text-[var(--color-success)]"><span aria-hidden="true" className="h-2 w-2 rounded-full bg-[var(--color-success)]" />Live</span>
        ) : (
          <span className="text-xs text-[var(--color-text-muted)]">{status === 'connecting' ? 'Connecting…' : 'History — not live'}</span>
        )}
      </header>

      {isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading activity…</p>}
      {isError && <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read its activity history.</p>}
      {data && data.items.length === 0 && <p className="text-sm text-[var(--color-text-muted)]">Nothing has been recorded yet.</p>}
      {data && data.items.length > 0 && (
        <ul className="divide-y divide-[var(--color-border)]">
          {data.items.map((a) => (
            <li key={a.id} className="flex items-center gap-3 py-1.5 text-[12.5px]">
              <span className="min-w-0 flex-1">
                <span className={a.outcome === 'Failure' ? 'text-[var(--color-error)]' : undefined}>{words[a.action] ?? a.action}</span>
                {a.outcome === 'Failure' && <span className="text-[var(--color-error)]"> — did not go through</span>}
                {a.resourceName && <span className="text-[var(--color-text-muted)]"> · {a.resourceName}</span>}
              </span>
              <span className="hidden text-xs text-[var(--color-text-muted)] sm:inline"><Attribution actor={a.actor} at={a.timestamp} compact /></span>
              <time dateTime={a.timestamp} className="shrink-0 text-xs text-[var(--color-text-muted)]">{formatWhen(a.timestamp, now)}</time>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
