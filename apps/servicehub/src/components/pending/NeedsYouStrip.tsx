import { TriangleAlert } from 'lucide-react'
import { Link } from 'react-router-dom'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { CloudProvider, EnvironmentKind } from '../../lib/api/namespaces'
import { pendingRows } from '../../lib/pendingRows'
import { providerLabel } from '../../lib/providers'
import { useProviderScope } from '../provider/providerScope'
import { PendingWorkList } from './PendingWorkList'

const MaxRows = 3

/**
 * "Needs you" (5.8): the first thing Home says is whether anything needs a person, for THIS cloud — and one action for each
 * thing that does. The same pending-work query as the bell. At most three rows; the rest are the bell's job. Other clouds
 * get one line, never their rows (Home never mixes clouds). When nothing needs anyone: one small sentence, never a banner.
 */
export function NeedsYouStrip({ provider, namespaceId, environment, watching }: {
  provider: CloudProvider; namespaceId?: string; environment?: EnvironmentKind; watching: string
}) {
  const here = usePendingWork({ provider, namespaceId, environment })
  const everywhere = usePendingWork()
  const { select } = useProviderScope()

  if (!here.data) return null
  const rows = pendingRows(here.data.items)
  const cloud = providerLabel[provider]
  const elsewhere = (everywhere.data?.byProvider ?? []).filter((p) => p.provider !== provider && p.count > 0)

  if (rows.length === 0 && elsewhere.length === 0) {
    return <p className="text-[13px] text-[var(--color-text-muted)]">Nothing needs you. {watching}</p>
  }

  return (
    <section aria-label="Needs you" className="overflow-hidden rounded-xl border border-[#fcd34d] bg-[#fffbeb]">
      {rows.length > 0 && (
        <>
          <header className="flex items-center justify-between px-5 pt-3">
            <h2 className="flex items-center gap-2 text-[15px] font-bold text-[#92400e]"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> Needs you</h2>
            <span className="text-xs font-semibold text-[#b45309]">{here.data.total} {here.data.total === 1 ? 'thing' : 'things'} in {cloud}</span>
          </header>
          <PendingWorkList rows={rows.slice(0, MaxRows)} now={new Date()} />
          {rows.length > MaxRows && <p className="px-5 pb-2 text-xs text-[#92400e]">+ {rows.length - MaxRows} more — open the bell to see them all.</p>}
        </>
      )}
      {elsewhere.map((p) => (
        <p key={p.provider} className="flex flex-wrap items-center gap-2 border-t border-[#fde68a] px-5 py-2 text-[13px] text-[#78350f]">
          Also waiting: <b>{p.count} {p.count === 1 ? 'replay needs' : 'replays need'} your approval in {providerLabel[p.provider]}</b> — the Agent stopped and asked.
          <Link to="/" onClick={() => select(p.provider)} className="ml-auto font-semibold text-[var(--color-primary-700)] hover:underline">Switch to {providerLabel[p.provider]} →</Link>
        </p>
      ))}
    </section>
  )
}
