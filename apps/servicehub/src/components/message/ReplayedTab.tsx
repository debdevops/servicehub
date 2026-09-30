import { usePageSize } from '../../lib/pageSize'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { Download, RefreshCw, Search, TriangleAlert, Zap } from 'lucide-react'
import { EntityPicker } from './EntityPicker'
import { Pager } from '../ui/Pager'
import { DataTable, type Column } from '../ui/DataTable'
import { WorkTabs } from './WorkTabs'
import type { ScopeChoice } from '../provider/scopeChoice'
import { ReplayedNumbers } from './ReplayedNumbers'
import { Attribution } from '../Attribution'
import { columnHelp } from '../../content/columns'
import { EntityCell } from './EntityCell'
import { useReplays } from '../../hooks/useReplay'
import type { ReplayListItem } from '../../lib/api/replay'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatAgo, formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { Skeleton } from '../ui/Skeleton'
import { ExplainerCard, ExplainerToggle } from '../explainer/Explainer'
import { useExplainer } from '../explainer/useExplainer'
import { Select } from '../ui/Select'


const endings = [
  { id: '', label: 'Any result' },
  { id: 'fixed', label: 'Stayed fixed' },
  { id: 'watching', label: 'Being watched' },
  { id: 'returned', label: 'Came back' },
  { id: 'unproven', label: 'Sent, can’t be proven' },
  { id: 'notsent', label: 'Not sent' },
] as const
const windows = [
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
  { id: 'all', label: 'All time' },
] as const
type Ending = Exclude<(typeof endings)[number]['id'], ''>
type Window = (typeof windows)[number]['id']

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
      return <Chip tone="neutral">{v.watchUntil ? `Watching · until ${formatWhen(v.watchUntil, new Date())}` : 'Watching'}</Chip>
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
export function ReplayedTab({ provider, choice }: { provider: CloudProvider; choice: ScopeChoice }) {
  const explainer = useExplainer('replayed')
  const cloud = providerLabel[provider]
  const [params, setParams] = useSearchParams()
  const { search } = useLocation()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [pageSize, setPageSize] = usePageSize()
  const ending = endings.find((e) => e.id !== '' && e.id === params.get('ending'))?.id as Ending | undefined
  const by = params.get('by') === 'people' || params.get('by') === 'autonomous' ? (params.get('by') as 'people' | 'autonomous') : undefined
  const entity = params.get('entity') ?? undefined
  const windowId: Window = (windows.find((w) => w.id === params.get('window'))?.id ?? '24h') as Window
  const q = params.get('q') ?? ''
  const { data, isPending, isError, refetch, isFetching, dataUpdatedAt } = useReplays({
    provider, namespaceId: choice.ns?.id, environment: choice.env ?? undefined, ending, by, entity, q: q || undefined, window: windowId, page, pageSize,
  })
  const [ticked, setTicked] = useState<ReadonlySet<string>>(new Set())
  const now = new Date()
  const filtering = !!(ending || by || entity || q || windowId !== '24h')

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

  // Search is typed, so it is applied a moment after the last keystroke rather than on each one.
  const [draft, setDraft] = useState(q)
  useEffect(() => {
    if (draft === q) return
    const t = setTimeout(() => change({ q: draft.trim() || null }), 300)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft])

  // One click, one window: Details opens the message drawer and puts away anything else left in the address.
  const openHref = (row: ReplayListItem) => {
    const next = new URLSearchParams(search)
    ;['modal', 'replay', 'active', 'view'].forEach((k) => next.delete(k))
    next.set('tab', 'replayed')
    next.set('message', String(row.dlqMessageId))
    return `/?${next.toString()}`
  }

  const exportCsv = () => {
    const rows = (data?.items ?? []).filter((r) => ticked.size === 0 || ticked.has(String(r.id)))
    const cell = (v: string) => `"${v.replace(/"/g, '""')}"`
    const csv = [['Replayed', 'Queue or topic', 'Message ID', 'Sent back to', 'By', 'Result'], ...rows.map((r) => [r.replayedAt, r.sourceEntity, r.messageId, r.targetEntity, r.actor.label, r.verification.status])]
      .map((l) => l.map(cell).join(',')).join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `replayed-${cloud.toLowerCase()}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const help = columnHelp.replayed
  const columns: Column<ReplayListItem>[] = [
    { key: 'when', header: 'Replayed', info: help.replayed, className: 'whitespace-nowrap', render: (r) => (<><span className="block font-medium">{formatWhen(r.replayedAt, now)}</span><span className="block text-xs text-[var(--color-text-muted)]">{formatAgo(r.replayedAt, now)}</span></>) },
    { key: 'from', header: 'Queue or topic', info: help.from, render: (r) => <EntityCell entityName={r.sourceEntity} entityType={r.sourceEntity.includes('/') ? 'subscription' : 'queue'} /> },
    { key: 'msg', header: 'Message ID', info: columnHelp.drawer.messageId, render: (r) => <span className="font-mono text-[12px] [overflow-wrap:anywhere]">{r.messageId}</span> },
    { key: 'to', header: 'Sent back to', render: (r) => r.targetEntity },
    { key: 'by', header: 'By', info: help.by, render: (r) => <Attribution actor={r.actor} at={r.replayedAt} compact /> },
    { key: 'result', header: 'Result', info: help.result, width: 'min-w-[11rem]', render: (r) => <ResultChip row={r} /> },
    {
      key: 'open',
      header: 'Details',
      info: help.details,
      render: (r) => (
        <Link to={openHref(r)} aria-label={`Details of message ${r.messageId}`} className="whitespace-nowrap font-medium text-[var(--color-primary-700)] hover:underline">
          Details →
        </Link>
      ),
    },
  ]
  const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'
  const label = 'mb-0.5 block text-[12px] font-medium text-[var(--color-text-muted)]'

  return (
    <section className="px-6 py-6">
      <header className="mb-4 flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-[var(--color-text)]">{cloud} — Replayed <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} /></h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">Everything that was put back, by whom, and how it went.</p>
        </div>
        <button
          type="button"
          onClick={() => setParams((p) => { const n = new URLSearchParams(p); ;['message', 'active', 'replay', 'view', 'modal'].forEach((k) => n.delete(k)); n.set('panel', 'rules'); return n }, { replace: true })}
          className="flex items-center gap-2 rounded-lg bg-[var(--color-primary-50)] px-3 py-2 text-sm font-medium text-[var(--color-primary-700)] hover:bg-[var(--color-primary-100)]"
        >
          <Zap className="h-4 w-4" aria-hidden="true" /> Auto Replay rules
        </button>
      </header>

      {explainer.shown && <ExplainerCard id="replayed" onDismiss={explainer.dismiss} />}
      <ReplayedNumbers provider={provider} choice={choice} window={windowId} />
      <WorkTabs current="replayed" />

      <div className="mb-3 flex flex-wrap items-end gap-3 text-sm">
        <div><label className={label} htmlFor="replayed-result">Result</label>
          <Select id="replayed-result" value={ending ?? ''} onChange={(v) => change({ ending: v })}>{endings.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}</Select></div>
        <div className="min-w-[13rem]"><span className={label}>Queue or topic</span>
          <EntityPicker namespaces={choice.namespaces} cloud={cloud} recorded={[]} value={entity} onChange={(e) => change({ entity: e })} /></div>
        <div><label className={label} htmlFor="replayed-by">Replayed by</label>
          <Select id="replayed-by" value={by ?? ''} onChange={(v) => change({ by: v })}>
            <option value="">All</option><option value="people">People</option><option value="autonomous">ServiceHub autonomous</option></Select></div>
        <div><label className={label} htmlFor="replayed-window">Time window</label>
          <Select id="replayed-window" value={windowId} onChange={(v) => change({ window: v === '24h' ? null : v })}>{windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}</Select></div>
        <div className="min-w-[14rem] flex-1"><label className={label} htmlFor="replayed-search">Search</label>
          <span className="relative block"><Search aria-hidden="true" className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-text-muted)]" />
            <input id="replayed-search" type="search" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Message ID, queue, who…" data-shortcut="filter" className={`${field} w-full pl-8`} /></span></div>
        <button type="button" onClick={() => void refetch()} disabled={isFetching} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-60">
          <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} aria-hidden="true" /> Refresh
        </button>
        <button type="button" onClick={exportCsv} disabled={!data || data.items.length === 0} title={ticked.size ? 'Download the ticked rows' : 'Download this page'} aria-label="Download as CSV" className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-2.5 hover:bg-[var(--color-surface-muted)] disabled:opacity-50">
          <Download className="h-4 w-4" aria-hidden="true" />
        </button>
        <span className="pb-2 text-xs text-[var(--color-text-muted)]">Last updated {dataUpdatedAt ? formatAgo(new Date(dataUpdatedAt).toISOString(), now) : '—'}</span>
      </div>

      {isPending && <Skeleton label="Reading replays…" rows={6} />}
      {isError && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read its list of replays.</p>
          <button type="button" onClick={() => void refetch()} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
        </div>
      )}

      {data && (
        <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <div className="flex items-center gap-3 border-b border-[var(--color-border)] px-4 py-2.5">
            <span className="font-semibold">{data.total.toLocaleString()} {data.total === 1 ? 'message' : 'messages'} replayed</span>
            <span className="ml-auto text-xs text-[var(--color-text-muted)]">Newest first</span>
          </div>
          {data.items.length === 0 ? (
            <div className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">
              <p>{filtering ? 'No replays match these filters.' : `Nothing has been replayed in ${cloud} yet. Replay a dead letter and it appears here.`}</p>
              {filtering && <button type="button" onClick={() => setParams((p) => { const n = new URLSearchParams(p); ;['ending', 'by', 'entity', 'q', 'window', 'page'].forEach((k) => n.delete(k)); return n }, { replace: true })} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Clear the filters</button>}
            </div>
          ) : (
            <>
              <DataTable
                caption="Replays, newest first"
                columns={columns}
                rows={data.items}
                rowKey={(r) => String(r.id)}
                compact
                selection={{
                  selected: ticked,
                  onToggle: (k) => setTicked((t) => { const n = new Set(t); if (n.has(k)) n.delete(k); else n.add(k); return n }),
                  onTogglePage: () => setTicked((t) => { const keys = data.items.map((r) => String(r.id)); const all = keys.every((k) => t.has(k)); const n = new Set(t); keys.forEach((k) => (all ? n.delete(k) : n.add(k))); return n }),
                }}
                rowLabel={(r) => `Select replay of ${r.messageId}`}
              />
              <Pager page={data.page} pageSize={data.pageSize} total={data.total} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(s) => { setPageSize(s); change({ page: null }) }} />
            </>
          )}
        </div>
      )}
    </section>
  )
}
