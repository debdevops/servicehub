import { useState } from 'react'
import { usePageSize } from '../../lib/pageSize'
import { useSearchParams } from 'react-router-dom'
import { ScrollText, TriangleAlert } from 'lucide-react'
import { CloudBadge, ByCell, LevelChip, StateChip, EntryPanel, ExportEvidence } from '../../components/advanced/LedgerParts'
import { EntityCell } from '../../components/message/EntityCell'
import { columnHelp } from '../../content/columns'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { DataTable, type Column } from '../../components/ui/DataTable'
import { Pager } from '../../components/ui/Pager'
import { TabBar } from '../../components/ui/TabBar'
import { useLedger, useRecoverySummary } from '../../hooks/useRecoverySummary'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { PendingWorkPage } from '../../lib/api/pendingWork'
import { pendingRows } from '../../lib/pendingRows'
import { PendingWorkList } from '../../components/pending/PendingWorkList'
import { useNamespaces } from '../../hooks/useNamespaces'
import { AdvancedFilters, asBy } from '../../components/advanced/AdvancedFilters'
import { resolveScope } from '../../components/provider/scopeChoice'
import { type EntryState, type LedgerEntry, type RecoveryWindow } from '../../lib/api/recovery'
import { stateChip } from '../../lib/ledgerWords'
import { formatWhen } from '../../lib/format'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { Skeleton } from '../../components/ui/Skeleton'
import { Select } from '../../components/ui/Select'
import PageHelpLink from '../../components/help/PageHelpLink'


const windows: readonly { id: RecoveryWindow; label: string }[] = [
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
  { id: 'all', label: 'All time' },
]

/** The tabs the design draws, each a state (or "All"). Other states stay reachable by `?state=` and appear under All. */
const tabs: readonly { id: EntryState | 'All'; label: string; help: keyof typeof columnHelp.ledgerTabs }[] = [
  { id: 'All', label: 'All', help: 'all' },
  { id: 'Observing', label: 'Watching', help: 'observing' },
  { id: 'Recovered', label: 'Recovered', help: 'recovered' },
  { id: 'Unverified', label: 'Unverified', help: 'unverified' },
  { id: 'Returned', label: 'Returned', help: 'returned' },
  { id: 'ExecutionFailed', label: 'Failed', help: 'failed' },
  { id: 'ExecutionUnknown', label: 'Unknown', help: 'unknown' },
  { id: 'Declined', label: 'Declined', help: 'declined' },
]

/** The colour a tab takes: green for proof, red for a failure or a return, amber for waiting on a person. */
const tabTone: Partial<Record<EntryState | 'All', 'good' | 'bad' | 'neutral'>> = {
  Recovered: 'good', Returned: 'bad', ExecutionFailed: 'bad', Observing: 'neutral', Unverified: 'neutral', ExecutionUnknown: 'neutral', Declined: 'neutral',
}

const allStates = Object.keys(stateChip) as EntryState[]
const asState = (v: string | null): EntryState | undefined => allStates.find((s) => s === v)
const asWindow = (v: string | null): RecoveryWindow => (v === '7d' || v === '30d' || v === 'all' ? v : '24h')
const asProvider = (v: string | null): CloudProvider | undefined => (v === 'azure' || v === 'aws' || v === 'gcp' ? v : undefined)

/**
 * The Recovery Ledger (Advanced): every recovery action, its evidence and how it ended — readable, filterable and
 * verifiable. Acting happens in place (ADR-0016 D3, amended 2026-09-27): Replay and Approve open the Simple modals over this
 * page, with the same gate and permissions. There is still no write-off or purge here.
 *
 * State, window, scope and the open entry are all in the URL, so the Advanced Overview's bar can deep-link
 * (`?state=Unverified`) and a filtered view survives a refresh. The tab counts come from the same summary Simple's
 * numbers use. It shows only this server's own chain; 4.0.0 entries never appear (ADR-0015 D5).
 */
export default function RecoveryLedgerPage() {
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('ledger')
  const namespaces = useNamespaces()

  const state = asState(params.get('state'))
  // The Waiting tab (5.9) is not a ledger state: it is the pending work — every Declined entry still waiting for a person.
  const waitingTab = params.get('state') === 'Waiting'
  const window = asWindow(params.get('window'))
  const provider = asProvider(params.get('provider'))
  const selected = params.get('entry')
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [pageSize, setPageSize] = usePageSize()

  // The cloud picks which namespaces are on offer; `?ns=` / `?env=` narrow within them. A stale one is ignored.
  const inScope = (namespaces.data ?? []).filter((n) => !provider || n.provider === provider)
  const choice = resolveScope(inScope, params)
  const narrow = { namespaceId: choice.ns?.id, environment: choice.env ?? undefined }
  const summary = useRecoverySummary({ window, provider, ...narrow })
  // With an entry open its pane sits beside the table, so the table stays short (10 rows) and the pane in view; close it and the reader's own page size returns.
  const rowsPerPage = selected ? 10 : pageSize
  const by = asBy(params.get('by'))
  const entity = params.get('entity') ?? undefined
  const search = params.get('q') ?? undefined
  const narrowed = !!(by || entity || search)
  const ledger = useLedger({ window, provider, state, page, pageSize: rowsPerPage, by, entity, q: search, ...narrow })

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

  const waiting = usePendingWork({ provider, ...narrow })
  // A state without a tab of its own (Written off, Discarded…) still gets one while it is the chosen view, so the page never shows a filter with nothing lit.
  const visibleTabs = state && !tabs.some((t) => t.id === state) ? [...tabs, { id: state, label: stateChip[state], help: 'other' as const }] : tabs
  const countOf = (id: EntryState | 'All') => (id === 'All' ? summary.data?.total : summary.data?.states.find((s) => s.state === id)?.count)

  return (
    <section className="px-6 py-6">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">
            <ScrollText className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Recovery Ledger{' '}
            <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} /> <PageHelpLink page="advanced" />
          </h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">Every recovery action, who took it, the evidence, and how it ended. Append-only and tamper-evident.</p>
        </div>
        <div className="flex flex-wrap items-end gap-2 text-sm">
          <Select variant="card" label="Window" ariaLabel="Window" value={window} onChange={(v) => change({ window: v === '24h' ? null : v })}>
            {windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
          </Select>
          <ExportEvidence window={window} />
        </div>
      </header>

      {explainer.shown && <ExplainerCard id="ledger" onDismiss={explainer.dismiss} />}

      {/* The entry pane takes room only while an entry is open: an empty 360 px placeholder squeezed the table until Outcome and
          Details sat behind a side-scroll at 1366 px (found live 2026-09-28). */}
      <div className={`grid gap-4 ${selected ? 'lg:grid-cols-[minmax(0,1fr)_360px]' : ''}`}>
        <div className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <TabBar
            variant="pills"
            label="Outcome"
            active={waitingTab ? 'Waiting' : (state ?? 'All')}
            onSelect={(id) => change({ state: id === 'All' ? null : id })}
            tabs={[
              { id: 'Waiting', label: 'Waiting', count: waiting.data?.total, help: columnHelp.ledgerTabs.waiting, tone: 'attention' },
              ...visibleTabs.map((t) => ({ id: t.id, label: t.label, count: countOf(t.id), help: columnHelp.ledgerTabs[t.help], tone: tabTone[t.id] })),
            ]}
          />
          <AdvancedFilters namespaces={namespaces.data ?? []} searchPlaceholder="Search queues, namespaces, reasons or who…" />
          {narrowed && <p className="border-b border-[var(--color-border)] px-4 py-1.5 text-xs text-[var(--color-text-muted)]">The tab numbers follow the cloud, namespace and window; By, queue and search narrow the table below them.</p>}

          {waitingTab && <WaitingView page={waiting.data} pending={waiting.isPending} failed={waiting.isError} />}
          {!waitingTab && ledger.isPending && <Skeleton label="Reading the ledger…" rows={6} className="px-6 py-8" />}
          {!waitingTab && ledger.isError && (
            <p role="alert" className="flex items-center gap-2 px-6 py-8 text-sm"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read the ledger.
              <button type="button" onClick={() => void ledger.refetch()} className="text-[var(--color-primary-700)] hover:underline">Try again</button></p>
          )}
          {!waitingTab && ledger.data && (ledger.data.items.length === 0 ? (
            <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">
              {state ? `No ${stateChip[state].toLowerCase()} entries in this window.` : 'No recovery actions in this window. When something is replayed, it is recorded here.'}
            </p>
          ) : (
            <>
              <LedgerTable namespaces={namespaces.data ?? []} rows={ledger.data.items} selected={selected} onSelect={(id) => change({ entry: id })} />
              <Pager page={ledger.data.page} pageSize={ledger.data.pageSize} total={ledger.data.total} filtered={!!state || !!provider} onPage={(p) => change({ page: String(p) })} onPageSize={selected ? undefined : (s) => { setPageSize(s); change({ page: null }) }} />
            </>
          ))}
          <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">{selected ? '' : 'Choose Details on a row to see who took it, what happened and its evidence. '}The chain starts at this server’s own genesis.</p>
        </div>

        <EntryPanel id={selected} onClose={() => change({ entry: null })} />
      </div>
    </section>
  )
}

/**
 * The Waiting tab (5.9): everything the Agent stopped and asked about, with the gate's reason code — read-only. Each row's
 * action opens in place (the Approve modal, the rule panel) or goes to the agent — same gate as in Simple (ADR-0016 D3 amended).
 * Filterable by reason code; cloud and namespace come from the page's own scope.
 */
function WaitingView({ page, pending, failed }: { page: PendingWorkPage | undefined; pending: boolean; failed: boolean }) {
  const [reason, setReason] = useState<string>('')
  if (pending) return <Skeleton label="Reading what is waiting…" rows={4} className="px-6 py-8" />
  if (failed || !page) return <p role="alert" className="px-6 py-8 text-sm">ServiceHub couldn’t read what is waiting.</p>
  const codes = [...new Set(page.items.map((i) => i.reasonCode))]
  const rows = pendingRows(page.items.filter((i) => !reason || i.reasonCode === reason), { capped: page.items.length < page.total }).map((r) => ({
    ...r,
    action: r.action.href.startsWith('?') ? { label: r.kind === 'approval' ? 'Review ›' : 'Open ›', href: r.action.href } : r.action,
  }))
  if (page.items.length === 0) {
    return <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">Nothing is waiting for a person. When the Agent stops and asks, it appears here and in the bell.</p>
  }
  return (
    <div>
      {codes.length > 1 && (
        <div className="flex items-center gap-2 px-5 pt-3 text-xs text-[var(--color-text-muted)]">
          Reason
          <Select variant="inline" ariaLabel="Reason" value={reason} onChange={setReason} className="text-sm">
            <option value="">All reasons</option>
            {codes.map((c) => <option key={c} value={c}>{c}</option>)}
          </Select>
        </div>
      )}
      <PendingWorkList rows={rows} now={new Date()} primaryFirst={false} />
      <p className="border-t border-[var(--color-border)] px-5 py-2 text-xs text-[var(--color-text-muted)]">Approving opens right here and goes through the same safety checks as any replay.</p>
    </div>
  )
}

function LedgerTable({ rows, namespaces, selected, onSelect }: { rows: readonly LedgerEntry[]; namespaces: readonly Namespace[]; selected: string | null; onSelect: (id: string) => void }) {
  const now = new Date()
  // An entry records its namespace by name. Two clouds can each own an `orders`, so the match needs the cloud too, and an
  // ambiguous or unknown name is shown as recorded rather than guessed at.
  const tagOf = (r: LedgerEntry): string => {
    if (!r.namespaceName) return '—'
    const hits = namespaces.filter((n) => n.provider === r.provider && (n.name === r.namespaceName || n.displayName === r.namespaceName))
    return hits.length === 1 ? (hits[0].displayName ?? hits[0].name) : r.namespaceName
  }
  const help = columnHelp.ledger
  // With an entry open its pane says what it was and how it was matched, and gives the table 360 px less: those columns
  // go, so Outcome and Details never sit behind a side-scroll at 1366 px.
  const columns: Column<LedgerEntry>[] = [
    { key: 'time', header: 'Time', info: help.time, render: (r) => formatWhen(r.begunAt, now) },
    { key: 'cloud', header: 'Cloud', info: help.cloud, className: 'whitespace-nowrap', render: (r) => <CloudBadge provider={r.provider} /> },
    ...(selected ? [] : [{ key: 'ns', header: 'Namespace', info: help.namespace, render: (r: LedgerEntry) => tagOf(r) }]),
    { key: 'entity', header: 'Queue / topic', info: help.entity, render: (r) => <EntityCell size="sm" entityName={r.entityName} entityType={r.entityType ?? (r.entityName.includes('/') ? 'subscription' : 'queue')} /> },
    ...(selected ? [] : [{ key: 'msg', header: 'Message', info: columnHelp.drawer.messageId, render: (r: LedgerEntry) => r.messageId ? <span title={r.messageId} className="block max-w-[5rem] truncate font-mono text-[11.5px]">{r.messageId}</span> : <span className="text-[var(--color-text-muted)]">—</span> }]),
    { key: 'by', header: 'By', info: help.by, render: (r) => <ByCell actor={r.actor} at={r.begunAt} /> },
    ...(selected ? [] : [{ key: 'what', secondary: true, header: 'What', info: help.what, render: (r: LedgerEntry) => r.kind }]),
    { key: 'outcome', header: 'Outcome', info: help.outcome, render: (r) => <StateChip state={r.state} /> },
    { key: 'level', secondary: true, header: 'Level', info: help.level, render: (r) => <LevelChip level={r.level} /> },
    ...(selected || !rows.some((r) => r.confidence) ? [] : [{ key: 'match', secondary: true, header: 'Match', info: help.match, render: (r: LedgerEntry) => r.confidence ?? '—' }]),
    {
      key: 'open',
      header: 'Details',
      info: help.open,
      render: (r) => (
        <button type="button" aria-pressed={selected === r.id} aria-label={`Details of ledger entry from ${formatWhen(r.begunAt, now)}`} onClick={() => onSelect(r.id)}
          className="font-medium text-[var(--color-primary-700)] hover:underline">
          Details →
        </button>
      ),
    },
  ]
  return <DataTable caption="Recovery ledger entries, newest first" columns={columns} rows={rows} rowKey={(r) => r.id} compact />
}
