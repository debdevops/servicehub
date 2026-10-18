import { ChevronRight, Info, RefreshCw } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { formatWhen } from '../../lib/format'
import { cloudLabel, cloudOf, type CloudScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'

/**
 * The head every Operational Tools page shares (designs `p06`–`p14`): breadcrumb, the page title with an optional count, the
 * "What you're looking at" note and "Last updated" with its refresh. Nothing here reads data; the page passes what it knows.
 */
export function ToolPageHeader({ scope, title, count, lookingAt, updatedAtMs, onRefresh }: {
  scope: CloudScope; title: string; count?: number | null; lookingAt: ReactNode; updatedAtMs: number; onRefresh: () => void
}) {
  const cloud = cloudOf(scope)
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <nav aria-label="Breadcrumb" className="mb-1 flex items-center gap-1 text-sm text-[var(--color-text-muted)]">
          <Link to={nextHref(nextEntry(cloud ? 'home' : 'home-all'), { cloud: cloud ?? undefined })} className="hover:underline">{cloudLabel[scope]}</Link>
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
          <span aria-current="page" className="font-semibold text-[var(--color-primary-700)]">{title}</span>
        </nav>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          {title}
          {count != null && <span className="rounded-full bg-red-50 px-2 py-0.5 text-sm font-semibold text-red-700" aria-label={`${count} waiting`}>{count}</span>}
        </h1>
      </div>
      <div className="flex gap-3">
        <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
          <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
          <div><p className="font-semibold">What you’re looking at</p><p>{lookingAt}</p></div>
        </aside>
        <div className="flex items-start gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-sm">
          <div><p className="text-[var(--color-text-muted)]">Last updated</p><p className="font-semibold">{updatedAtMs ? formatWhen(new Date(updatedAtMs).toISOString(), new Date()) : '—'}</p></div>
          <button type="button" aria-label="Refresh" onClick={onRefresh} className="rounded-md p-1.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
        </div>
      </div>
    </div>
  )
}
