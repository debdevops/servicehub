import { Mail, TriangleAlert } from 'lucide-react'
import { useDeadLetterTrend } from '../../hooks/useDeadLetters'
import type { CloudProvider } from '../../lib/api/namespaces'
import type { DeadLetterPage } from '../../lib/api/deadLetters'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'

const tones = [
  { bg: 'bg-[#fee2e2]', fg: 'text-[#dc2626]', Icon: Mail },
  { bg: 'bg-[#ffedd5]', fg: 'text-[#ea580c]', Icon: Mail },
  { bg: 'bg-[#f3e8ff]', fg: 'text-[#9333ea]', Icon: Mail },
] as const

/**
 * The strip beside the title: the two most common recorded reasons, everything else together, and the total with its last two weeks.
 * Every number is a count of what ServiceHub recorded in this view; there is no percentage change, because no earlier period is
 * kept to compare with. The line is dead letters that first appeared each day.
 */
export function ReasonStrip({ provider, page, namespaceId, environment }: { provider: CloudProvider; page: DeadLetterPage; namespaceId?: string; environment?: string }) {
  const trend = useDeadLetterTrend(provider, 14, { namespaceId, environment: environment as never })
  const groups = [...page.groups].sort((a, b) => b.count - a.count)
  const top = groups.slice(0, 2)
  const rest = groups.slice(2).reduce((n, g) => n + g.count, 0) + (page.otherReasons?.count ?? 0)
  const items = [...top.map((g) => ({ label: g.reason ?? 'No reason recorded', n: g.count })), ...(rest > 0 ? [{ label: 'Other errors', n: rest }] : [])]
  const series = trend.data?.series.map((d) => d.new) ?? []
  const max = Math.max(1, ...series)
  const points = series.map((n, i) => `${(i / Math.max(1, series.length - 1)) * 96},${28 - (n / max) * 26}`).join(' ')

  return (
    <section aria-label="Dead letters by reason" className="flex flex-wrap items-center gap-x-5 gap-y-3 rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
      {items.map((it, i) => {
        const t = tones[i] ?? tones[2]
        return (
          <div key={it.label} className="flex items-center gap-3">
            <span className={`flex h-10 w-10 items-center justify-center rounded-xl ${t.bg} ${t.fg}`}>{i === 0 ? <TriangleAlert className="h-5 w-5" aria-hidden="true" /> : <t.Icon className="h-5 w-5" aria-hidden="true" />}</span>
            <span><span className="tabular block text-lg font-extrabold leading-none">{it.n.toLocaleString()}</span><span className="text-xs text-[var(--color-text-muted)]">{it.label}</span></span>
          </div>
        )
      })}
      <div className="flex items-center gap-3 border-l border-[var(--color-border)] pl-5">
        <span>
          <span className="flex items-center text-xs text-[var(--color-text-muted)]">Total messages<InfoTip help={sectionHelp.dlq.total} /></span>
          <span className="tabular block text-lg font-extrabold leading-none">{page.paging.total.toLocaleString()}</span>
        </span>
        {points && (
          <svg viewBox="0 0 96 30" className="h-8 w-24" role="img" aria-label={`New dead letters per day, last ${series.length} days: ${series.join(', ')}`}>
            <polyline points={points} fill="none" stroke="#ef4444" strokeWidth="1.5" strokeLinejoin="round" />
          </svg>
        )}
      </div>
    </section>
  )
}
