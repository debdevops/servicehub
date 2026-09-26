import { useQuery } from '@tanstack/react-query'
import { Activity, GitMerge, Lightbulb, TrendingUp } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { fetchInsights, type InsightFinding, type InsightKind } from '../../lib/api/insights'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'

const kinds: Readonly<Record<InsightKind, { label: string; Icon: typeof Activity }>> = {
  anomaly: { label: 'Spike or drop', Icon: Activity },
  backlog: { label: 'Backlog forecast', Icon: TrendingUp },
  correlation: { label: 'Moved together', Icon: GitMerge },
  narration: { label: 'Summary', Icon: Lightbulb },
}

/**
 * Insights (unit 6.18): 4.0.0's intelligence engines, read-only, in one list — kind · what · since · evidence. Every finding is
 * counted from recorded dead letters by an agent listed on the Agents page; none acts. Summaries are fixed templates, badged as
 * suggestions (R3). Drift detection is not here: it needs per-message features 4.1.0 does not record.
 */
export function InsightsTab({ provider }: { provider?: CloudProvider }) {
  const [showCleared, setShowCleared] = useState(false)
  const q = useQuery({ queryKey: ['insights', provider, showCleared], queryFn: () => fetchInsights({ provider, cleared: showCleared }), refetchInterval: 60_000 })
  const now = new Date()
  return (
    <div className="space-y-4">
      <p className="text-sm text-[var(--color-text-muted)]">
        What ServiceHub noticed in the dead letters it recorded — counted, never guessed, and never acted on.{' '}
        {q.data?.lastLookedAt && <>Last looked {formatWhen(q.data.lastLookedAt, now)}. </>}
        <Link to="/advanced/agents" className="font-medium text-[var(--color-primary-700)] hover:underline">The agents that look →</Link>
      </p>
      {q.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading insights…</p>}
      {q.isError && <p role="alert" className="text-sm">ServiceHub couldn’t read its insights. <button type="button" onClick={() => void q.refetch()} className="font-medium text-[var(--color-primary-700)] hover:underline">Try again</button></p>}
      {q.data && q.data.current.length === 0 && (
        <p className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-6 text-center text-sm">
          {q.data.lastLookedAt ? 'Nothing unusual right now: no spikes, no growing backlogs, nothing moving together.' : 'The Insights agents haven’t finished their first look yet.'}
        </p>
      )}
      {q.data && q.data.current.length > 0 && <Findings items={q.data.current} now={now} label="Current insights" />}
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={showCleared} onChange={(e) => setShowCleared(e.target.checked)} /> Show ones that have passed</label>
      {showCleared && q.data && (q.data.cleared.length === 0 ? <p className="text-sm text-[var(--color-text-muted)]">None have passed yet.</p> : <Findings items={q.data.cleared} now={now} label="Insights that have passed" />)}
    </div>
  )
}

function Findings({ items, now, label }: { items: readonly InsightFinding[]; now: Date; label: string }) {
  return (
    <ul aria-label={label} className="divide-y divide-[var(--color-border)] rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      {items.map((f) => {
        const { label: kind, Icon } = kinds[f.kind]
        return (
          <li key={f.id} className="flex gap-3 px-5 py-3 text-sm">
            <Icon className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-600)]" aria-hidden="true" />
            <div className="min-w-0 flex-1">
              <p className="flex flex-wrap items-center gap-2 text-xs">
                <b className="uppercase tracking-wide">{kind}</b>
                {f.suggestion && <span className="rounded-full bg-[#fef3c7] px-2 py-0.5 font-semibold text-[#92400e]">Suggestion</span>}
                {f.provider && <span className="text-[var(--color-text-muted)]">{providerLabel[f.provider]}{f.namespaceName ? ` · ${f.namespaceName}` : ''}</span>}
                <span className="text-[var(--color-text-muted)]">severity {f.severity}</span>
              </p>
              <p className="mt-0.5">{f.what}</p>
              <p className="mt-0.5 text-xs text-[var(--color-text-muted)]">
                Since {formatWhen(f.firstSeenAt, now)}{f.clearedAt ? ` · passed ${formatWhen(f.clearedAt, now)}` : ` · still true ${formatWhen(f.lastSeenAt, now)}`}
                {f.entityName && f.namespaceId && <> · <Link to={`/?tab=dlq&ns=${encodeURIComponent(f.namespaceId)}&entity=${encodeURIComponent(f.entityName)}`} className="font-medium text-[var(--color-primary-700)] hover:underline">See the dead letters →</Link></>}
              </p>
              {f.metrics && (
                <details className="mt-1 text-xs"><summary className="cursor-pointer text-[var(--color-text-muted)]">The numbers</summary>
                  <dl className="mt-1 grid grid-cols-2 gap-x-4 sm:grid-cols-3">{Object.entries(f.metrics).map(([k, v]) => <div key={k}><dt className="inline text-[var(--color-text-muted)]">{k}: </dt><dd className="inline font-mono">{Number.isInteger(v) ? v : v.toFixed(2)}</dd></div>)}</dl>
                </details>
              )}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
