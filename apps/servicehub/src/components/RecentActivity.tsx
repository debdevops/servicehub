import { useState } from 'react'
import { ChevronRight } from 'lucide-react'
import { InfoTip } from './ui/InfoTip'
import { useAudit } from '../hooks/useIdentity'
import { useStreamStatus } from '../hooks/useEventStream'
import { Attribution } from './Attribution'
import type { CloudProvider, EnvironmentKind } from '../lib/api/namespaces'
import { formatWhen } from '../lib/format'
import { auditActionWords as words } from '../lib/auditWords'

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
  const [open, setOpen] = useState<string | null>(null)

  return (
    <section aria-label="Recent activity" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3">
      <header className="mb-1 flex items-center justify-between">
        <h2 className="flex items-center text-[13.5px] font-bold text-[#1f2937]">Recent activity<InfoTip help={{ title: 'Recent activity', text: 'Every action ServiceHub recorded — connecting a cloud, replaying a message, looking at dead letters — newest first, with who did it. Pick a row to see the full record.' }} /></h2>
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
            <li key={a.id}>
              <button type="button" aria-expanded={open === a.id} onClick={() => setOpen(open === a.id ? null : a.id)} className="flex w-full items-center gap-3 py-1.5 text-left text-[12.5px]">
                <ChevronRight className={`h-3.5 w-3.5 shrink-0 text-[var(--color-text-muted)] transition-transform ${open === a.id ? 'rotate-90' : ''}`} aria-hidden="true" />
                <span className="min-w-0 flex-1">
                  <span className={a.outcome === 'Failure' ? 'text-[var(--color-error)]' : undefined}>{words[a.action] ?? a.action}</span>
                  {a.outcome === 'Failure' && <span className="text-[var(--color-error)]"> — did not go through</span>}
                  {a.resourceName && <span className="text-[var(--color-text-muted)]"> · {a.resourceName}</span>}
                </span>
                <span className="hidden text-xs text-[var(--color-text-muted)] sm:inline"><Attribution actor={a.actor} at={a.timestamp} compact /></span>
                <time dateTime={a.timestamp} className="shrink-0 text-xs text-[var(--color-text-muted)]">{formatWhen(a.timestamp, now)}</time>
              </button>
              {open === a.id && (
                <dl className="mb-2 ml-6 grid grid-cols-[110px_minmax(0,1fr)] gap-x-3 gap-y-0.5 rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-[12px]">
                  <dt className="text-[var(--color-text-muted)]">What</dt><dd>{words[a.action] ?? a.action} <span className="font-mono text-[11px] text-[var(--color-text-muted)]">{a.action}</span></dd>
                  <dt className="text-[var(--color-text-muted)]">Result</dt><dd>{a.outcome === 'Success' ? 'Went through' : 'Did not go through'}</dd>
                  {a.resourceName && <><dt className="text-[var(--color-text-muted)]">On</dt><dd className="break-all">{a.resourceName}</dd></>}
                  {a.namespaceName && <><dt className="text-[var(--color-text-muted)]">Namespace</dt><dd>{a.namespaceName}{a.cloudProvider ? ` · ${a.cloudProvider}` : ''}{a.environment ? ` · ${a.environment}` : ''}</dd></>}
                  <dt className="text-[var(--color-text-muted)]">When</dt><dd>{new Date(a.timestamp).toLocaleString()}</dd>
                  {a.errorDetails && <><dt className="text-[var(--color-text-muted)]">Details</dt><dd className="break-words">{a.errorDetails}</dd></>}
                  {a.correlationId && <><dt className="text-[var(--color-text-muted)]">Correlation</dt><dd className="break-all font-mono text-[11px]">{a.correlationId}</dd></>}
                </dl>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
