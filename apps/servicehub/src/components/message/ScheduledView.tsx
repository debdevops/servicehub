import { useQuery } from '@tanstack/react-query'
import { RefreshCw } from 'lucide-react'
import { fetchScheduled } from '../../lib/api/messages'
import { formatBytes, formatWhen } from '../../lib/format'

/**
 * Scheduled (unit 6.17): what is waiting to be delivered later, soonest first. Read-only — cancelling would change the cloud
 * and has no ledger route yet. Where the cloud has no scheduled messages, the caller says so instead of showing an empty table.
 */
export function ScheduledView({ namespaceId, entity, subscription }: { namespaceId: string; entity: string; subscription?: string }) {
  const q = useQuery({ queryKey: ['scheduled', namespaceId, entity, subscription], queryFn: () => fetchScheduled(namespaceId, { entity, subscription }) })
  const now = new Date()
  return (
    <div>
      <div className="flex items-center border-b border-[var(--color-border)] px-4 py-2.5 text-[12.5px] text-[var(--color-text-muted)]">
        Soonest first · refreshed when you ask
        <button type="button" onClick={() => void q.refetch()} className="ml-auto inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-2.5 py-1.5 font-semibold text-[var(--color-text)]">
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" /> Refresh
        </button>
      </div>
      {q.isPending && <p role="status" className="px-4 py-3 text-sm text-[var(--color-text-muted)]">Looking for scheduled messages…</p>}
      {q.isError && <p role="alert" className="px-4 py-3 text-sm">ServiceHub couldn’t read the scheduled messages just now.</p>}
      {q.data && q.data.messages.length === 0 && <p className="px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">Nothing is scheduled on this queue.</p>}
      {q.data && q.data.messages.length > 0 && (
        <table className="w-full text-[12.5px]">
          <caption className="sr-only">Scheduled messages in {entity}, soonest first</caption>
          <thead><tr className="text-left text-[11px] uppercase tracking-wide text-[var(--color-text-muted)]"><th className="px-4 py-2">Due</th><th className="px-2 py-2">Message</th><th className="px-2 py-2 text-right">Size</th><th className="px-4 py-2">Body</th></tr></thead>
          <tbody className="divide-y divide-[var(--color-border)]">
            {q.data.messages.map((m) => (
              <tr key={m.sequenceNumber}>
                <td className="whitespace-nowrap px-4 py-2 font-semibold">{m.scheduledFor ? formatWhen(m.scheduledFor, now) : 'not given'}</td>
                <td className="px-2 py-2 font-mono">{m.messageId}</td>
                <td className="whitespace-nowrap px-2 py-2 text-right">{formatBytes(m.sizeInBytes)}</td>
                <td className="max-w-[18rem] truncate px-4 py-2 font-mono" title={m.bodyPreview ?? undefined}>{m.bodyPreview ?? '(no body)'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {q.data?.capped && <p className="border-t border-[var(--color-border)] px-4 py-2 text-[12px] text-[var(--color-text-muted)]">Showing the first {q.data.messages.length}; there may be more.</p>}
    </div>
  )
}
