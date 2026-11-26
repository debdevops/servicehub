import { useSearchParams } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { useAudit } from '../../../hooks/useIdentity'
import type { ConnectedComponent } from '../../../lib/api/components'
import { auditActionWords } from '../../../lib/auditWords'
import { formatWhen } from '../../../lib/format'
import { usePageSize } from '../../../lib/pageSize'
import { Th } from '../Tip'
import { Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'

/** The sorted list the Action filter offers: every action ServiceHub has a plain name for. */
const actionChoices = Object.entries(auditActionWords).sort(([, a], [, b]) => a.localeCompare(b))

/**
 * The Audit tab of one connection (build-plan unit D7; design `p05f-service-bus-audit`): who did what on this connection, newest
 * first, from the same durable record Home's Recent activity reads. Read-only: nothing here can be edited. The API filters by
 * connection and by action only, so those are the filters offered; it has no filter for who, entity or time, and no export, so none is
 * drawn (a filter that only narrowed the page on screen would be a count nobody could trust).
 */
export function AuditTab({ component: c }: { component: ConnectedComponent }) {
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const action = actionChoices.find(([id]) => id === params.get('action'))?.[0]

  const audit = useAudit({ namespaceId: c.namespaceId, action, page, pageSize })
  const items = audit.data?.items ?? []
  const total = audit.data?.total ?? 0
  const now = new Date()

  const change = (patch: Record<string, string | null>) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      if (!('page' in patch)) next.delete('page')
      return next
    }, { replace: true })

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm">Action
          <select aria-label="Action" className={field} value={action ?? ''} onChange={(e) => change({ action: e.target.value || null })}>
            <option value="">All</option>
            {actionChoices.map(([id, words]) => <option key={id} value={id}>{words}</option>)}
          </select>
        </label><Tip label="Action:" />
        {action && <button type="button" onClick={() => change({ action: null })} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
      </div>

      <section aria-labelledby="audit-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="audit-title" className="px-4 pt-4 text-base font-semibold">What happened here<Tip label="section:What happened here" /></h2>
        {audit.isError ? (
          <p role="alert" className="px-4 py-6 text-sm text-red-800">The audit trail could not be read. Nothing is shown rather than a list that may be missing what happened.</p>
        ) : audit.isPending ? (
          <p role="status" className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : items.length === 0 ? (
          <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">{action ? 'Nothing matches that action.' : `Nothing has been recorded for ${c.name} yet.`}</p>
        ) : (
          <div className="mt-3 overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">What happened on {c.name}, newest first</caption>
              <thead><tr><Th>When</Th><Th>Who</Th><Th>What</Th><Th>Entity</Th><Th>Result</Th><Th>Detail</Th></tr></thead>
              <tbody>
                {items.map((a) => {
                  const failed = a.outcome === 'Failure'
                  return (
                    <tr key={a.id} className="border-t border-[var(--color-border)]">
                      <td className={`${td} whitespace-nowrap`}>{formatWhen(a.timestamp, now)}</td>
                      <td className={td}>{a.actor.label || a.actor.identity}</td>
                      <td className={td}>{auditActionWords[a.action] ?? a.action}</td>
                      <td className={td}>{a.resourceName ?? '—'}</td>
                      <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${failed ? 'bg-red-50 text-red-700' : 'bg-green-50 text-green-800'}`}>{failed ? 'Failed' : 'Done'}</span></td>
                      <td className={td}>{failed ? a.errorDetails ?? 'No reason was recorded' : ''}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
        <Pager page={page} pageSize={pageSize} total={total} filtered={!!action} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({}) }} label="audit entries" />
        <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">Every action recorded against this connection, by a person, a key or the Agent. Recovery actions also appear in the Evidence ledger with their proof. Nothing here can be edited.</p>
      </section>
    </div>
  )
}
