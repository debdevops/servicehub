import { Link } from 'react-router-dom'
import type { CloudSummary } from '../lib/homeSummary'

/**
 * Queues holding dead letters, deepest first. It is built from entity counts alone — nothing is peeked or
 * read — so it works on every cloud, including those where messages cannot be browsed safely. Each row opens
 * Dead letters filtered to that queue. Draws nothing when no queue holds any: an empty box is not information.
 */
export function QueuesNeedingAttention({ summary }: { summary: CloudSummary }) {
  if (summary.needingAttention.length === 0) return null
  return (
    <section aria-label="Queues needing attention" className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-[13px]">
        <h2 className="text-[13.5px] font-bold text-[#1f2937]">Queues needing attention</h2>
        <span className="text-[11px] text-[var(--color-text-muted)]">most dead letters first</span>
      </div>
      <ul>
        {summary.needingAttention.map((e) => (
          <li key={`${e.kind}:${e.name}`} className="flex items-center gap-3 border-b border-[#f3f4f6] px-4 py-[9px] last:border-b-0">
            <span className="min-w-0 flex-1 truncate font-mono text-[12px] font-medium text-[#1f2937]">{e.name}</span>
            <span className="rounded-full bg-[var(--color-error-light)] px-[9px] py-0.5 text-[11px] font-bold text-[#b91c1c]">
              {(e.deadLetterMessages ?? 0).toLocaleString()} dead
            </span>
            <Link to={`/?tab=dlq&entity=${encodeURIComponent(e.name)}`} className="text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline">
              Open →
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
