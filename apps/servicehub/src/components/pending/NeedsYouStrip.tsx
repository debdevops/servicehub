import { useState } from 'react'
import { Bot, CheckCircle2, Clock, Maximize2, Minimize2, Zap } from 'lucide-react'
import { Link } from 'react-router-dom'
import { usePendingWork } from '../../hooks/usePendingWork'
import { useMinimizable } from '../../hooks/useMinimizable'
import { pendingRows, type PendingRow } from '../../lib/pendingRows'
import { formatAge } from '../../lib/format'
import { useResolveHref } from './PendingWorkList'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'

const MaxCards = 3
const icons = {
  approval: { Icon: Clock, box: 'bg-[#fffbeb] text-[#d97706]' },
  rule: { Icon: Zap, box: 'bg-[var(--color-error-light)] text-[#dc2626]' },
  agent: { Icon: Bot, box: 'bg-[var(--color-error-light)] text-[#dc2626]' },
} as const

/**
 * "Needs your attention" (D48, O-H1): the first thing Home says, always across EVERY connected cloud —
 * never just the one a scope tab happens to have open, so something waiting on AWS is never hidden
 * because the page is showing Azure. Each card already names its own cloud (`PendingRow.where`).
 *
 * Up to three cards in a row; the rest expand in place behind "+N more" — never a second page, never
 * just a hint to go open the bell. Nothing waiting: one small sentence, never a banner (unchanged from
 * the per-cloud strip this replaces).
 *
 * Can also be minimized (remembered per browser, via `useMinimizable`) down to its header alone —
 * the cards themselves can take a lot of vertical space when several things are waiting, and nothing
 * here is lost by collapsing it, just out of the way until reopened.
 */
export function NeedsYouStrip() {
  const pending = usePendingWork()
  const [expanded, setExpanded] = useState(false)
  const panel = useMinimizable('needs-your-attention')

  if (!pending.data) return null
  const rows = pendingRows(pending.data.items, { capped: pending.data.items.length < pending.data.total })

  if (rows.length === 0) {
    return (
      <p className="flex items-center gap-1.5 text-[13px] text-[var(--color-text-muted)]">
        <CheckCircle2 className="h-4 w-4 text-[var(--color-success)]" aria-hidden="true" /> Nothing needs you right now.
      </p>
    )
  }

  const shown = expanded ? rows : rows.slice(0, MaxCards)
  const count = <span className="text-xs font-semibold text-[#b45309]">{rows.length} {rows.length === 1 ? 'thing' : 'things'}</span>

  return (
    <section aria-label="Needs your attention" className="overflow-hidden rounded-xl border border-[#fcd34d] bg-[#fffbeb]">
      <header className="flex items-center justify-between px-4 py-3">
        <h2 className="flex items-center text-[15px] font-bold text-[#92400e]">Needs your attention<InfoTip help={sectionHelp.home.needsYou} /></h2>
        <div className="flex items-center gap-3">
          {count}
          <button
            type="button"
            onClick={panel.minimized ? panel.restore : panel.minimize}
            aria-label={panel.minimized ? 'Show needs your attention' : 'Minimize needs your attention'}
            title={panel.minimized ? 'Show' : 'Minimize'}
            className="rounded-full p-1 text-[#92400e] hover:bg-[#fef3c7]"
          >
            {panel.minimized ? <Maximize2 className="h-3.5 w-3.5" aria-hidden="true" /> : <Minimize2 className="h-3.5 w-3.5" aria-hidden="true" />}
          </button>
        </div>
      </header>
      {!panel.minimized && (
        <>
          <div className="grid gap-3 p-4 pt-0 sm:grid-cols-2 xl:grid-cols-3">
            {shown.map((row) => <NeedsYouCard key={row.key} row={row} />)}
          </div>
          {!expanded && rows.length > MaxCards && (
            <button
              type="button"
              onClick={() => setExpanded(true)}
              className="block w-full border-t border-[#fde68a] px-4 py-2 text-left text-[12.5px] font-semibold text-[#92400e] hover:bg-[#fef3c7]"
            >
              + {rows.length - MaxCards} more
            </button>
          )}
        </>
      )}
    </section>
  )
}

function NeedsYouCard({ row }: { row: PendingRow }) {
  const { Icon, box } = icons[row.kind]
  const resolve = useResolveHref()

  return (
    <article className="flex items-start gap-3 rounded-lg border border-[#fde68a] bg-white p-3">
      <span className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${box}`}><Icon className="h-4 w-4" aria-hidden="true" /></span>
      <div className="min-w-0 flex-1">
        <p className="text-[13px] font-bold text-[var(--color-text)]">{row.title}</p>
        <p className="text-[12px] text-[var(--color-text-muted)]">{row.where}{row.where ? ' — ' : ''}{row.why}</p>
        <p className="mt-0.5 text-[11px] text-[var(--color-text-muted)]">waiting {formatAge(row.since, new Date())}</p>
        {row.kind === 'agent' ? (
          // An agent stopping is ServiceHub's own machinery, not a cloud page — stay on Home and point at
          // the bar that resumes it, rather than sending someone to Advanced (plan §7: never link there).
          <a href="#agent-bar" onClick={(e) => { e.preventDefault(); document.getElementById('agent-bar')?.scrollIntoView({ block: 'center' }) }} className="mt-1.5 inline-block text-[12px] font-semibold text-[#b45309] hover:underline">
            Look at the Agent bar ↓
          </a>
        ) : (
          <Link to={resolve(row.action.href)} className="mt-1.5 inline-block text-[12px] font-semibold text-[#b45309] hover:underline">
            {row.action.label} →
          </Link>
        )}
      </div>
    </article>
  )
}
