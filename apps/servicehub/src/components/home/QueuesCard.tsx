import { InfoTip } from '../ui/InfoTip'
import { columnHelp } from '../../content/columns'
import { namespaceTag } from '../provider/scopeChoice'
import { describeEntity } from '../../lib/entities'
import type { Namespace } from '../../lib/api/namespaces'
import type { CloudSummary } from '../../lib/homeSummary'

const SHOWN = 6

/**
 * Queues and topics needing attention — `QueuesNeedingAttention` and `QueueDepth` merged into one card
 * (plan §5.3 F4): one list, dead letters with an inline bar on a shared scale, active beside it, worst
 * first. Built from entity counts alone, so it exists even where messages can't be browsed. Where
 * nothing needs a look it says so in the same card frame, rather than leaving a hole in the row (plan
 * §4 rule 5) — `QueueDepth` used to draw nothing at all here.
 */
export function QueuesCard({ summary, namespaces }: { summary: CloudSummary; namespaces: readonly Namespace[] }) {
  const nameOf = new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))
  const several = namespaces.length > 1
  const canCount = summary.active !== null || summary.deadLetters !== null
  // A queue that is only another queue's dead-letter queue (AWS) is that queue's dead letters, not a queue of its own.
  const dlqTargets = new Set(summary.entities.flatMap((r) => (r.entity.deadLetterTargetName ? [`${r.namespaceId}:${r.entity.deadLetterTargetName}`] : [])))
  const rows = summary.entities
    .filter((r) => r.entity.kind !== 'topic')
    .filter((r) => !dlqTargets.has(`${r.namespaceId}:${r.entity.name}`))
    .filter((r) => (r.entity.deadLetterMessages ?? 0) > 0 || (r.entity.activeMessages ?? 0) > 0)
    .sort((a, b) => (b.entity.deadLetterMessages ?? 0) - (a.entity.deadLetterMessages ?? 0))
    .slice(0, SHOWN)
  const max = Math.max(1, ...rows.map((r) => r.entity.deadLetterMessages ?? 0))

  return (
    <section aria-label="Queues and topics needing attention" className="flex h-full flex-col overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-[13px]">
        <h2 className="flex items-center text-[13.5px] font-bold text-[#1f2937]">Queues and topics needing attention<InfoTip help={columnHelp.attention.queue} /></h2>
        <span className="text-[11px] text-[var(--color-text-muted)]">most dead letters first</span>
      </div>
      {!canCount ? (
        <p className="p-4 text-[13px] text-[var(--color-text-muted)]">This cloud does not report message counts, so ServiceHub can’t say which queues need a look.</p>
      ) : rows.length === 0 ? (
        <p className="flex items-center gap-1.5 p-4 text-[13px] text-[var(--color-text-muted)]">No queue or topic has dead letters right now.</p>
      ) : (
        <ul className="divide-y divide-[#f3f4f6]">
          {rows.map(({ namespaceId, entity: e }) => {
            const d = describeEntity(e.name, e.kind)
            const label = d.kind === 'subscription' && d.topic ? `${d.topic} › ${d.name}` : d.name
            return (
              <li key={`${namespaceId}:${e.kind}:${e.name}`} className="flex items-center gap-3 px-4 py-[9px]">
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-mono text-[12px] font-medium">{label}</span>
                  {several && <span className="block truncate text-[11px] text-[var(--color-text-muted)]">{nameOf.get(namespaceId) ?? 'Unknown namespace'}</span>}
                  <span className="mt-1 block h-1.5 w-full max-w-[160px] rounded-full bg-[#f3f4f6]">
                    <span className="block h-1.5 rounded-full bg-[#dc2626]" style={{ width: `${((e.deadLetterMessages ?? 0) / max) * 100}%` }} />
                  </span>
                </span>
                <span className="shrink-0 text-right">
                  <span className="block rounded-full bg-[var(--color-error-light)] px-[9px] py-0.5 text-[11px] font-bold text-[#b91c1c]">
                    {e.deadLetterMessages === null ? 'can’t count' : `${e.deadLetterMessages.toLocaleString()} dead`}
                  </span>
                  <span className="mt-1 block text-[10.5px] text-[var(--color-text-muted)]">{e.activeMessages === null ? 'can’t count active' : `${e.activeMessages.toLocaleString()} active`}</span>
                </span>
                <a href={`?tab=dlq&entity=${encodeURIComponent(e.name)}&ns=${encodeURIComponent(namespaceId)}`} className="shrink-0 text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline">
                  Open →
                </a>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
