import { useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { Hash, ScrollText, ShieldCheck, TriangleAlert, X } from 'lucide-react'
import { Attribution } from '../../components/Attribution'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { DataTable, type Column } from '../../components/ui/DataTable'
import { Pager } from '../../components/ui/Pager'
import { useLedger, useLedgerEntry, useRecoverySummary } from '../../hooks/useRecoverySummary'
import { useNamespaces } from '../../hooks/useNamespaces'
import { verifyChain, type ChainVerification, type EntryState, type LedgerEntry, type RecoveryWindow } from '../../lib/api/recovery'
import { describeEvent, shortHash, stateChip, stateMeaning, stateTone } from '../../lib/ledgerWords'
import { formatWhen } from '../../lib/format'
import { connectedProviders, providerLabel } from '../../lib/providers'
import type { CloudProvider } from '../../lib/api/namespaces'

const PAGE_SIZE = 10

const windows: readonly { id: RecoveryWindow; label: string }[] = [
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
  { id: 'all', label: 'All time' },
]

/** The tabs the design draws, each a state (or "All"). Other states stay reachable by `?state=` and appear under All. */
const tabs: readonly { id: EntryState | 'All'; label: string }[] = [
  { id: 'All', label: 'All' },
  { id: 'Observing', label: 'Watching' },
  { id: 'Recovered', label: 'Recovered' },
  { id: 'Unverified', label: 'Unverified' },
  { id: 'Returned', label: 'Returned' },
  { id: 'ExecutionFailed', label: 'Failed' },
  { id: 'ExecutionUnknown', label: 'Unknown' },
]

const allStates = Object.keys(stateChip) as EntryState[]
const asState = (v: string | null): EntryState | undefined => allStates.find((s) => s === v)
const asWindow = (v: string | null): RecoveryWindow => (v === '7d' || v === '30d' || v === 'all' ? v : '24h')
const asProvider = (v: string | null): CloudProvider | undefined => (v === 'azure' || v === 'aws' || v === 'gcp' ? v : undefined)

/**
 * The Recovery Ledger (Advanced): every recovery action, its evidence and how it ended — readable, filterable and
 * verifiable, with NO way to act (ADR-0016 D3). "Replay again" is a link into the Simple flow, which is where
 * acting happens and is gated; nothing on this page changes anything, not even a write-off.
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
  const window = asWindow(params.get('window'))
  const provider = asProvider(params.get('provider'))
  const selected = params.get('entry')
  const page = Math.max(1, Number(params.get('page')) || 1)

  const summary = useRecoverySummary({ window, provider })
  const ledger = useLedger({ window, provider, state, page, pageSize: PAGE_SIZE })
  const clouds = connectedProviders(namespaces.data ?? [])

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
        <div className="flex gap-2 text-sm">
          <label className="flex flex-col text-xs uppercase tracking-wide text-[var(--color-text-muted)]">
            Scope
            <select value={provider ?? ''} onChange={(e) => change({ provider: e.target.value })} className="mt-0.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm normal-case text-[var(--color-text)]">
              <option value="">All clouds</option>
              {clouds.map((c) => <option key={c.provider} value={c.provider}>{c.label} only</option>)}
            </select>
          </label>
          <label className="flex flex-col text-xs uppercase tracking-wide text-[var(--color-text-muted)]">
            Window
            <select value={window} onChange={(e) => change({ window: e.target.value === '24h' ? null : e.target.value })} className="mt-0.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm normal-case text-[var(--color-text)]">
              {windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
            </select>
          </label>
        </div>
      </header>

      {explainer.shown && <ExplainerCard id="ledger" onDismiss={explainer.dismiss} />}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <nav aria-label="Outcome" className="flex flex-wrap gap-1 border-b border-[var(--color-border)] px-2">
            {tabs.map((t) => {
              const active = (t.id === 'All' && !state) || t.id === state
              const n = countOf(t.id)
              return (
                <button
                  key={t.id}
                  type="button"
                  aria-current={active ? 'page' : undefined}
                  onClick={() => change({ state: t.id === 'All' ? null : t.id })}
                  className={`-mb-px border-b-2 px-3 py-2.5 text-sm ${active ? 'border-[var(--color-primary-600)] font-semibold' : 'border-transparent text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}
                >
                  {t.label} <span className="ml-1 rounded-full bg-[var(--color-surface-muted)] px-2 py-0.5 text-xs">{n ?? '…'}</span>
                </button>
              )
            })}
          </nav>

          {ledger.isPending && <p role="status" className="px-6 py-8 text-sm text-[var(--color-text-muted)]">Reading the ledger…</p>}
          {ledger.isError && (
            <p role="alert" className="flex items-center gap-2 px-6 py-8 text-sm"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read the ledger.
              <button type="button" onClick={() => void ledger.refetch()} className="text-[var(--color-primary-700)] hover:underline">Try again</button></p>
          )}
          {ledger.data && (ledger.data.items.length === 0 ? (
            <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">
              {state ? `No ${stateChip[state].toLowerCase()} entries in this window.` : 'No recovery actions in this window. When something is replayed, it is recorded here.'}
            </p>
          ) : (
            <>
              <LedgerTable rows={ledger.data.items} selected={selected} onSelect={(id) => change({ entry: id })} />
              <Pager page={ledger.data.page} pageSize={ledger.data.pageSize} total={ledger.data.total} filtered={!!state || !!provider} onPage={(p) => change({ page: String(p) })} />
            </>
          ))}
          <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">The chain starts at this server’s own genesis.</p>
        </div>

        <EntryPanel id={selected} onClose={() => change({ entry: null })} />
      </div>
    </section>
  )
}

function LedgerTable({ rows, selected, onSelect }: { rows: readonly LedgerEntry[]; selected: string | null; onSelect: (id: string) => void }) {
  const now = new Date()
  const columns: Column<LedgerEntry>[] = [
    { key: 'time', header: 'Time', className: 'whitespace-nowrap', render: (r) => formatWhen(r.beganAt, now) },
    { key: 'entity', header: 'Entity', render: (r) => <span className="font-mono text-[13px]">{r.entityName}</span> },
    { key: 'cloud', header: 'Cloud', render: (r) => (r.provider ? providerLabel[r.provider] : '—') },
    { key: 'by', header: 'By', render: (r) => <Attribution actor={r.actor} at={r.beganAt} compact /> },
    { key: 'what', header: 'What', className: 'whitespace-nowrap', render: (r) => `${r.kind} · 1 message` },
    { key: 'outcome', header: 'Outcome', render: (r) => <StateChip state={r.state} /> },
    { key: 'match', header: 'Match', render: (r) => r.confidence ?? '—' },
    {
      key: 'open',
      header: 'Open',
      render: (r) => (
        <button type="button" aria-pressed={selected === r.id} aria-label={`Open ledger entry from ${formatWhen(r.beganAt, now)}`} onClick={() => onSelect(r.id)}
          className="font-medium text-[var(--color-primary-700)] hover:underline">
          Open →
        </button>
      ),
    },
  ]
  return <DataTable caption="Recovery ledger entries, newest first" columns={columns} rows={rows} rowKey={(r) => r.id} compact />
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
  const [chain, setChain] = useState<{ busy: boolean; result?: ChainVerification; failed?: boolean }>({ busy: false })

  if (id === null) {
    return <aside aria-label="Entry" className="rounded-xl border border-dashed border-[var(--color-border)] p-6 text-sm text-[var(--color-text-muted)]">Open an entry to see who took it, what happened, and its evidence.</aside>
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
  void search

  const check = async () => {
    setChain({ busy: true })
    try {
      setChain({ busy: false, result: await verifyChain() })
    } catch {
      setChain({ busy: false, failed: true })
    }
  }

  return (
    <aside aria-label="Entry" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
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
        <section aria-label="Who"><h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Who</h3><Attribution actor={e.actor} at={e.beganAt} verb={e.kind === 'Purge' ? 'Purged' : 'Replayed'} /></section>

        <section aria-label="What happened">
          <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">What happened</h3>
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
          <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Evidence</h3>
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
          {e.dlqMessageId !== null && <Link to={homeParams('dlq')} className="text-[var(--color-primary-700)] hover:underline">Replay again →</Link>}
        </div>
        <p className="mt-2 text-xs text-[var(--color-text-muted)]">Links open the Simple flow that acts — nothing on this page changes anything.</p>
      </div>
    </aside>
  )
}
