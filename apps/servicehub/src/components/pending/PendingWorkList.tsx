import { Bot, Clock, Zap } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import type { PendingRow } from '../../lib/pendingRows'
import { formatAge } from '../../lib/format'

const icons = {
  approval: { Icon: Clock, box: 'bg-[#fffbeb] text-[#d97706]' },
  rule: { Icon: Zap, box: 'bg-[var(--color-error-light)] text-[#dc2626]' },
  agent: { Icon: Bot, box: 'bg-[var(--color-error-light)] text-[#dc2626]' },
} as const

/** "?modal=approve&group=…" keeps the page you are on and its filters; "/advanced/…" is a page of its own. */
export function useResolveHref() {
  const { pathname, search } = useLocation()
  return (href: string) => {
    if (!href.startsWith('?')) return href
    const next = new URLSearchParams(search)
    new URLSearchParams(href.slice(1)).forEach((v, k) => next.set(k, v))
    return `${pathname}?${next.toString()}`
  }
}

/**
 * The rows of pending work — built once and drawn by the bell's panel, Home's Needs-you strip and the Ledger's Waiting tab
 * (5.3, 5.8, 5.9). Each row: what, where, why in plain words, and ONE action that opens the flow that resolves it. There is
 * no "mark as read": a row goes away only when the work is done.
 */
export function PendingWorkList({ rows, now, primaryFirst = true, dense = false }: { rows: readonly PendingRow[]; now: Date; primaryFirst?: boolean; dense?: boolean }) {
  const resolve = useResolveHref()
  return (
    <ul className="divide-y divide-[var(--color-border)]">
      {rows.map((r, n) => {
        const { Icon, box } = icons[r.kind]
        const primary = primaryFirst && n === 0
        return (
          <li key={r.key} className={`flex items-start gap-3 ${dense ? 'px-4 py-2.5' : 'px-5 py-3'}`}>
            <span className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${box}`}><Icon className="h-4 w-4" aria-hidden="true" /></span>
            <div className="min-w-0 flex-1">
              <p className="text-[14px] font-bold text-[var(--color-text)]">{r.title}</p>
              <p className="text-[12.5px] text-[var(--color-text-muted)]">
                {r.where}{r.where ? ' — ' : ''}{r.why}
              </p>
              <p className="mt-0.5 text-[11px] text-[var(--color-text-muted)]">waiting {formatAge(r.since, now)} · <span className="font-mono">{r.reasonCode}</span></p>
            </div>
            <Link
              to={resolve(r.action.href)}
              className={`shrink-0 rounded-lg px-3 py-1.5 text-sm font-semibold ${primary ? 'bg-[#d97706] text-white hover:bg-[#b45309]' : 'border border-[var(--color-border)] bg-[var(--color-surface)] hover:bg-[var(--color-surface-muted)]'}`}
            >
              {r.action.label}
            </Link>
          </li>
        )
      })}
    </ul>
  )
}
