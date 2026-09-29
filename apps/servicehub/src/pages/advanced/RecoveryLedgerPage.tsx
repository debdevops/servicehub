import { useDeadLetter } from '../../hooks/useDeadLetter'
import { useState } from 'react'
import { usePageSize } from '../../lib/pageSize'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { Bot, Download, Hash, KeyRound, ScrollText, ShieldCheck, TriangleAlert, UserRound, X } from 'lucide-react'
import { Attribution } from '../../components/Attribution'
import { EntityCell } from '../../components/message/EntityCell'
import { columnHelp } from '../../content/columns'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { DataTable, type Column } from '../../components/ui/DataTable'
import { Pager } from '../../components/ui/Pager'
import { InfoTip } from '../../components/ui/InfoTip'
import { sectionHelp } from '../../content/sections'
import { TabBar } from '../../components/ui/TabBar'
import { useLedger, useLedgerEntry, useRecoverySummary } from '../../hooks/useRecoverySummary'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { PendingWorkPage } from '../../lib/api/pendingWork'
import { pendingRows } from '../../lib/pendingRows'
import { PendingWorkList } from '../../components/pending/PendingWorkList'
import { useNamespaces } from '../../hooks/useNamespaces'
import { AdvancedFilters, asBy } from '../../components/advanced/AdvancedFilters'
import { cloudColor, resolveScope } from '../../components/provider/scopeChoice'
import { exportEvidence, verifyChain, type ChainVerification, type EntryState, type LedgerEntry, type RecoveryWindow } from '../../lib/api/recovery'
import { describeEvent, shortHash, stateChip, stateMeaning, stateTone } from '../../lib/ledgerWords'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { Skeleton } from '../../components/ui/Skeleton'


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
            <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">Every recovery action, who took it, the evidence, and how it ended. Append-only and tamper-evident.</p>
        </div>
        <div className="flex flex-wrap items-end gap-2 text-sm">
          <label className="flex flex-col text-xs uppercase tracking-wide text-[var(--color-text-muted)]">
            Window
            <select value={window} onChange={(e) => change({ window: e.target.value === '24h' ? null : e.target.value })} className="mt-0.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm normal-case text-[var(--color-text)]">
              {windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
            </select>
          </label>
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
  const rows = pendingRows(page.items.filter((i) => !reason || i.reasonCode === reason)).map((r) => ({
    ...r,
    action: r.action.href.startsWith('?') ? { label: r.kind === 'approval' ? 'Review ›' : 'Open ›', href: r.action.href } : r.action,
  }))
  if (page.items.length === 0) {
    return <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">Nothing is waiting for a person. When the Agent stops and asks, it appears here and in the bell.</p>
  }
  return (
    <div>
      {codes.length > 1 && (
        <label className="flex items-center gap-2 px-5 pt-3 text-xs text-[var(--color-text-muted)]">
          Reason
          <select value={reason} onChange={(e) => setReason(e.target.value)} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1 text-sm text-[var(--color-text)]">
            <option value="">All reasons</option>
            {codes.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
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
    { key: 'time', header: 'Time', info: help.time, className: 'whitespace-nowrap', render: (r) => formatWhen(r.begunAt, now) },
    { key: 'cloud', header: 'Cloud', info: help.cloud, className: 'whitespace-nowrap', render: (r) => <CloudBadge provider={r.provider} /> },
    ...(selected ? [] : [{ key: 'ns', header: 'Namespace', info: help.namespace, render: (r: LedgerEntry) => tagOf(r) }]),
    { key: 'entity', header: 'Queue / topic', info: help.entity, render: (r) => <EntityCell size="sm" entityName={r.entityName} entityType={r.entityType ?? (r.entityName.includes('/') ? 'subscription' : 'queue')} /> },
    ...(selected ? [] : [{ key: 'msg', header: 'Message', info: columnHelp.drawer.messageId, render: (r: LedgerEntry) => r.messageId ? <span title={r.messageId} className="block max-w-[10rem] truncate font-mono text-[11.5px]">{r.messageId}</span> : <span className="text-[var(--color-text-muted)]">—</span> }]),
    { key: 'by', header: 'By', info: help.by, render: (r) => <ByCell actor={r.actor} at={r.begunAt} /> },
    ...(selected ? [] : [{ key: 'what', header: 'What', info: help.what, className: 'whitespace-nowrap', render: (r: LedgerEntry) => `${r.kind} · 1 message` }]),
    { key: 'outcome', header: 'Outcome', info: help.outcome, render: (r) => <StateChip state={r.state} /> },
    { key: 'level', header: 'Level', info: help.level, render: (r) => <LevelChip level={r.level} /> },
    ...(selected ? [] : [{ key: 'match', header: 'Match', info: help.match, render: (r: LedgerEntry) => r.confidence ?? '—' }]),
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

/** A cloud, drawn the same for all three: its colour, its initial and its name. */
export function CloudBadge({ provider }: { provider: CloudProvider | null }) {
  if (!provider) return <span className="text-[var(--color-text-muted)]">—</span>
  return (
    <span className="inline-flex items-center gap-2">
      <span aria-hidden="true" className="flex h-6 w-6 items-center justify-center rounded-md text-[11px] font-extrabold text-white" style={{ background: cloudColor[provider] }}>{providerLabel[provider][0]}</span>
      {providerLabel[provider]}
    </span>
  )
}

/** Who made it, in two lines: the name, then what kind of actor it was. Never a made-up person. */
export function ByCell({ actor, at }: { actor: LedgerEntry['actor']; at: string }) {
  const autonomous = actor.kind === 'system' || actor.kind === 'automation'
  const sub = autonomous
    ? (/AutoReplay/.test(actor.identity) ? 'Auto Replay agent' : 'ServiceHub agent')
    : actor.kind === 'apiKey' ? 'API key' : 'User initiated'
  const Icon = autonomous ? Bot : actor.kind === 'apiKey' ? KeyRound : UserRound
  const name = autonomous
    ? `ServiceHub · ${actor.identity.replace(/^System:/, '').replace(/^AutoReplay:/, 'AutoReplay:')}`
    : actor.kind === 'apiKey' ? actor.identity.replace(/^ApiKey:/, '') : actor.isSession ? 'This browser session' : actor.label
  return (
    <span className="flex items-start gap-2" data-actor-kind={actor.kind} title={`${name} · ${formatWhen(at, new Date())}`}>
      <Icon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-text-muted)]" />
      <span><span className="block text-[13px] font-medium">{name}</span><span className="block text-xs text-[var(--color-text-muted)]">{sub}</span></span>
    </span>
  )
}

const levelMeta = {
  approve: { chip: 'L3', label: 'L3 Approve — a person approves each replay', cls: 'bg-[#dbeafe] text-[#1d4ed8]' },
  standing: { chip: 'L4', label: 'L4 Standing — rules may replay without asking', cls: 'bg-[#ede9fe] text-[#6d28d9]' },
  unattended: { chip: 'L5', label: 'L5 Unattended — earned the most trust', cls: 'bg-[#f3e8ff] text-[#7e22ce]' },
} as const

/** The level the failure's signature held when the action began. A dash means no signature was known — never a guess. */
export function LevelChip({ level }: { level?: LedgerEntry['level'] }) {
  if (!level) return <span className="text-[var(--color-text-muted)]" aria-label="No level recorded">—</span>
  const m = levelMeta[level]
  return <span title={m.label} aria-label={m.label} className={`inline-block rounded-md px-2 py-0.5 text-xs font-bold ${m.cls}`}>{m.chip}</span>
}

export function StateChip({ state }: { state: EntryState }) {
  const tone = stateTone(state)
  const bg = tone === 'good' ? 'bg-[var(--color-success-light)]' : tone === 'bad' ? 'bg-[var(--color-error-light)]' : tone === 'warn' ? 'bg-[var(--color-warning-light)]' : 'bg-[var(--color-surface-muted)]'
  return <span className={`inline-block whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium text-[var(--color-text)] ${bg}`}>{stateChip[state]}</span>
}

/** One entry opened: who, what happened, the evidence and the chain check. Read-only, and its links open the Simple flow. */
function EntryPanel({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { data, isPending, isError } = useLedgerEntry(id)
  const { search } = useLocation()
  // Whether the message is still stuck decides if "replay again" means anything: a replayed message has left the queue.
  const source = useDeadLetter(data?.entry.dlqMessageId ?? null).data?.item
  const [chain, setChain] = useState<{ busy: boolean; result?: ChainVerification; failed?: boolean }>({ busy: false })

  if (id === null) {
    return null
  }
  if (isPending) return <aside aria-label="Entry" className="rounded-xl border border-[var(--color-border)] p-6 text-sm" role="status">Reading the entry…</aside>
  if (isError || !data) return <aside aria-label="Entry" role="alert" className="rounded-xl border border-[var(--color-border)] p-6 text-sm">ServiceHub couldn’t read this entry. It may not be one you can see.</aside>

  const e = data.entry
  const cloud = e.provider ? providerLabel[e.provider] : 'the cloud'
  const last = data.events.at(-1)
  const homeParams = (tab: string) => {
    const next = new URLSearchParams()
    next.set('tab', tab)
    if (e.dlqMessageId !== null) next.set('message', String(e.dlqMessageId))
    return `/?${next.toString()}`
  }

  const check = async () => {
    setChain({ busy: true })
    try {
      setChain({ busy: false, result: await verifyChain() })
    } catch {
      setChain({ busy: false, failed: true })
    }
  }

  return (
    <aside aria-label="Entry" className="self-start overflow-y-auto rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] lg:sticky lg:top-[calc(var(--header-height)+8px)] lg:max-h-[calc(100vh-var(--header-height)-24px)]">
      <div className="flex items-start gap-2 border-b border-[var(--color-border)] p-4">
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2"><StateChip state={e.state} /> <span className="text-sm font-medium">{stateMeaning[e.state]}</span></p>
          <p className="mt-1 text-sm"><span className="font-mono text-[13px]">{e.entityName}</span> → <span className="font-mono text-[13px]">{e.targetEntity}</span> · {cloud}{e.namespaceName ? ` · ${e.namespaceName}` : ''}</p>
          {e.state === 'Returned' && e.confidence && (
            <p className="mt-1 text-xs text-[var(--color-text-muted)]">Match: {e.confidence === 'Exact' ? 'Exact — by the recovery ID' : 'Heuristic — by contents, which may be shared'}</p>
          )}
        </div>
        <button type="button" onClick={onClose} aria-label="Close entry" className="rounded-lg p-1 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"><X className="h-4 w-4" /></button>
      </div>

      <div className="space-y-4 p-4 text-sm">
        {(e.messageId || source) && (
          <section aria-label="Message">
            <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Message<InfoTip help={sectionHelp.ledger.message} /></h3>
            <dl className="grid grid-cols-[110px_1fr] gap-y-1">
              <dt className="text-[var(--color-text-muted)]">Message ID</dt><dd className="break-all font-mono text-xs">{e.messageId ?? source?.messageId ?? '—'}</dd>
              {source?.deadLetterReason && (<><dt className="text-[var(--color-text-muted)]">Failed because</dt><dd>{source.deadLetterReason}{source.deadLetterErrorDescription ? <span className="block text-xs text-[var(--color-text-muted)]">{source.deadLetterErrorDescription}</span> : null}</dd></>)}
            </dl>
          </section>
        )}

        <section aria-label="Who"><h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Who<InfoTip help={sectionHelp.ledger.who} /></h3><Attribution actor={e.actor} at={e.begunAt} verb={e.kind === 'Purge' ? 'Purged' : 'Replayed'} /></section>

        <section aria-label="What happened">
          <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">What happened<InfoTip help={sectionHelp.ledger.happened} /></h3>
          <ol className="space-y-2">
            {data.events.map((ev) => {
              const w = describeEvent(ev, cloud)
              return (
                <li key={ev.seq} className="flex items-start justify-between gap-3">
                  <span><span className="font-medium">{w.title}</span>{w.note && <span className="block text-xs text-[var(--color-text-muted)]">{w.note}</span>}</span>
                  <time dateTime={ev.occurredAt} className="shrink-0 font-mono text-xs text-[var(--color-text-muted)]">{new Date(ev.occurredAt).toLocaleTimeString('en-GB')}</time>
                </li>
              )
            })}
          </ol>
        </section>

        <section aria-label="Evidence">
          <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Evidence<InfoTip help={sectionHelp.ledger.evidence} /></h3>
          <dl className="grid grid-cols-[110px_1fr] gap-y-1">
            <dt className="text-[var(--color-text-muted)]">Entry</dt><dd className="break-all font-mono text-xs">{e.id}</dd>
            <dt className="text-[var(--color-text-muted)]">Recovery ID</dt><dd className="break-all font-mono text-xs">{data.recoveryMarker ?? (data.markerApplied ? '—' : 'not applied')}</dd>
            {last && (<><dt className="text-[var(--color-text-muted)]">Hash · previous</dt><dd className="font-mono text-xs" title={`${last.entryHash} · ${last.prevHash}`}>{shortHash(last.entryHash)} · {shortHash(last.prevHash)}</dd></>)}
          </dl>
          <div className="mt-3 rounded-xl border border-[var(--color-border)] p-3">
            <button type="button" onClick={() => void check()} disabled={chain.busy} className="flex items-center gap-2 font-medium text-[var(--color-primary-700)] hover:underline disabled:opacity-60">
              <Hash className="h-4 w-4" aria-hidden="true" /> {chain.busy ? 'Checking…' : 'Verify the chain'}
            </button>
            {chain.result?.isValid && (
              <p role="status" className="mt-2 flex items-start gap-2 text-sm"><ShieldCheck className="mt-0.5 h-4 w-4 text-[var(--color-success)]" aria-hidden="true" />
                <span><b>Chain verified</b> — {chain.result.eventsChecked.toLocaleString()} events, unbroken. Verifiable offline with <code>verify-recovery-chain.py</code>.</span></p>
            )}
            {chain.result && !chain.result.isValid && (
              <p role="alert" className="mt-2 text-sm text-[var(--color-error)]"><b>The chain does not verify</b>{chain.result.firstDivergentSeq !== null ? ` — first divergence at event ${chain.result.firstDivergentSeq}` : ''}. {chain.result.reason}</p>
            )}
            {chain.failed && <p role="alert" className="mt-2 text-sm text-[var(--color-error)]">ServiceHub couldn’t run the check.</p>}
          </div>
        </section>
      </div>

      <div className="border-t border-[var(--color-border)] p-4 text-sm">
        <div className="flex flex-wrap gap-4 font-medium">
          {e.dlqMessageId !== null && <Link to={homeParams('replayed')} className="text-[var(--color-primary-700)] hover:underline">Open in Home →</Link>}
          {e.dlqMessageId !== null && source?.status === 'active' && <Link to={`?${new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(search)), modal: 'replay', replay: String(e.dlqMessageId) })}`} className="text-[var(--color-primary-700)] hover:underline">Replay…</Link>}
        </div>
        {e.dlqMessageId !== null && source && source.status !== 'active' && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">
            This message is no longer in the dead-letter queue, so there is nothing to replay. If it failed again it is a new dead letter with its own entry.
          </p>
        )}
        <p className="mt-2 text-xs text-[var(--color-text-muted)]">Replay opens over this page and runs the same safety checks as anywhere else; “Open in Home” leaves for Simple.</p>
      </div>
    </aside>
  )
}

/**
 * Export evidence (unit 6.12): a read, so Advanced may offer it. The whole chain inside the window — the cloud and namespace
 * pickers do not narrow it, because leaving events out would break the chain — and it says so.
 */
function ExportEvidence({ window }: { window: RecoveryWindow }) {
  const [state, setState] = useState<{ busy: boolean; saved?: string; failed?: string }>({ busy: false })
  const run = async () => {
    setState({ busy: true })
    try {
      setState({ busy: false, saved: await exportEvidence(window) })
    } catch (e) {
      const status = (e as { response?: { status?: number } })?.response?.status
      setState({ busy: false, failed: status === 403 ? 'This key can’t export the ledger: it is limited to some namespaces, and the ledger is one chain across all of them.' : 'ServiceHub couldn’t export the ledger. Nothing was changed — try again.' })
    }
  }
  return (
    <div className="flex flex-col items-end">
      <button
        type="button"
        onClick={() => void run()}
        disabled={state.busy}
        title="Every event in this window, from every cloud and namespace, in chain order — check it anywhere with verify-recovery-chain.py."
        className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 font-medium hover:bg-[var(--color-surface-muted)] disabled:opacity-60"
      >
        <Download className="h-4 w-4" aria-hidden="true" /> {state.busy ? 'Exporting…' : 'Export evidence'}
      </button>
      <p aria-live="polite" className="mt-1 max-w-[18rem] text-right text-[11px] text-[var(--color-text-muted)]">
        {state.saved && <>Saved <code>{state.saved}</code> — every cloud and namespace, in chain order.</>}
        {state.failed && <span role="alert" className="text-[var(--color-error)]">{state.failed}</span>}
      </p>
    </div>
  )
}
