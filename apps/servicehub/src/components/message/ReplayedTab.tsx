import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { TriangleAlert } from 'lucide-react'
import { Pager } from '../ui/Pager'
import { DataTable, type Column } from '../ui/DataTable'
import { WorkTabs } from './WorkTabs'
import { ReplayedNumbers } from './ReplayedNumbers'
import { Attribution } from '../Attribution'
import { useReplays } from '../../hooks/useReplay'
import type { ReplayListItem } from '../../lib/api/replay'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'

const PAGE_SIZE = 25

const results = [
  { id: '', label: 'Any result' },
  { id: 'accepted', label: 'Sent back' },
  { id: 'rejected', label: 'Not accepted' },
  { id: 'unknown', label: 'Outcome unknown' },
] as const

/**
 * One chip per honest ending. "Verified — stayed fixed" appears only when the cloud could prove it; the amber
 * "Verification required" is not a failure — it says the confirmation is unproven (R4, C2, C3).
 */
function ResultChip({ row }: { row: ReplayListItem }) {
  const v = row.verification
  switch (v.status) {
    case 'verified':
      return <Chip tone="success">Verified — stayed fixed</Chip>
    case 'verification_required':
      return <Chip tone="warning">Verification required</Chip>
    case 'returned':
      return <Chip tone="error">Came back</Chip>
    case 'not_sent':
      return <Chip tone="error">Not accepted</Chip>
    case 'unknown':
      return <Chip tone="warning">Outcome unknown</Chip>
    default:
      return <Chip tone="neutral">{v.watchUntil ? `Watching · until ${new Date(v.watchUntil).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}` : 'Watching'}</Chip>
  }
}

function Chip({ tone, children }: { tone: 'neutral' | 'error' | 'warning' | 'success'; children: string }) {
  const bg = tone === 'success' ? 'bg-[var(--color-success-light)]' : tone === 'error' ? 'bg-[var(--color-error-light)]' : tone === 'warning' ? 'bg-[var(--color-warning-light)]' : 'bg-[var(--color-surface-muted)]'
  return <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium text-[var(--color-text)] ${bg}`}>{children}</span>
}

/**
 * Home's `?tab=replayed`: what was put back, when, by whom and how it went. Each row reopens the message
 * in the drawer (`?message=`) — replay has one home, and it is not this page. The four numbers above the
 * table (replayed today · stayed fixed · being watched · came back) come from the one recovery summary the
 * Advanced ledger reads too (unit 2.12).
 */
export function ReplayedTab({ provider }: { provider: CloudProvider }) {
  const cloud = providerLabel[provider]
  const [params, setParams] = useSearchParams()
  const { search } = useLocation()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const result = (['accepted', 'rejected', 'unknown'] as const).find((r) => r === params.get('result'))
  const { data, isPending, isError, refetch } = useReplays({ provider, result, page, pageSize: PAGE_SIZE })
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

  const openHref = (row: ReplayListItem) => {
    const next = new URLSearchParams(search)
    next.set('tab', 'replayed')
    next.set('message', String(row.dlqMessageId))
    return `/?${next.toString()}`
  }

  const columns: Column<ReplayListItem>[] = [
    { key: 'when', header: 'Replayed', className: 'whitespace-nowrap', render: (r) => formatWhen(r.replayedAt, now) },
    { key: 'from', header: 'From', render: (r) => <span className="font-mono text-[13px]">{r.sourceEntity}</span> },
    { key: 'by', header: 'By', render: (r) => <Attribution actor={r.actor} at={r.replayedAt} compact /> },
    { key: 'count', header: 'Messages', numeric: true, render: () => 1 },
    { key: 'result', header: 'Result', render: (r) => <ResultChip row={r} /> },
    {
      key: 'open',
      header: 'Open',
      render: (r) => (
        <Link to={openHref(r)} aria-label={`Open message ${r.messageId}`} className="whitespace-nowrap font-medium text-[var(--color-primary-700)] hover:underline">
          Open →
        </Link>
      ),
    },
  ]

  return (
    <section className="px-6 py-6">
      <header className="mb-4">
        <h1 className="text-2xl font-semibold text-[var(--color-text)]">{cloud} — Replayed</h1>
        <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">Everything that was put back, by whom, and how it went.</p>
      </header>

      <ReplayedNumbers provider={provider} />
      <WorkTabs current="replayed" />

      {isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading replays…</p>}
      {isError && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read its list of replays.</p>
          <button type="button" onClick={() => void refetch()} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
        </div>
      )}

      {data && (
        <>
          <div className="mb-3 flex items-center gap-3 text-sm">
            <label className="flex items-center gap-2">
              <span className="text-[var(--color-text-muted)]">Result</span>
              <select value={result ?? ''} onChange={(e) => change({ result: e.target.value })} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5">
                {results.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
              </select>
            </label>
            <span className="ml-auto text-xs text-[var(--color-text-muted)]">Newest first</span>
          </div>
          <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            {data.items.length === 0 ? (
              <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">
                {result ? 'No replays with that result.' : `Nothing has been replayed in ${cloud} yet. Replay a dead letter and it appears here.`}
              </p>
            ) : (
              <>
                <DataTable caption="Replays, newest first" columns={columns} rows={data.items} rowKey={(r) => String(r.id)} />
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} filtered={!!result} onPage={(p) => change({ page: String(p) })} />
              </>
            )}
          </div>
        </>
      )}
    </section>
  )
}
