import { useQueryClient } from '@tanstack/react-query'
import { Hash, ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { ByCell, EntryPanel, ExportEvidence, StateChip, type EntryLinks } from '../../components/advanced/LedgerParts'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { useMe } from '../../hooks/useIdentity'
import { usePagedList } from '../../hooks/usePagedList'
import { usePendingWork } from '../../hooks/usePendingWork'
import { useLedger, useRecoverySummary } from '../../hooks/useRecoverySummary'
import { verifyChain, type ChainVerification, type EntryState, type LedgerEntry, type RecoveryWindow } from '../../lib/api/recovery'
import { formatAge, formatWhen } from '../../lib/format'
import { stateChip, stateMeaning } from '../../lib/ledgerWords'
import { usePageSize } from '../../lib/pageSize'
import { permission } from '../../lib/permissions'
import { DetailWindow } from '../components/DetailWindow'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { entityLabel } from '../lib/entityRows'
import { recoveryWindows } from '../lib/words'
import { Th } from '../components/Tip'
import { Tip } from '../components/Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2.5 py-1.5 text-sm'
type Tab = 'all' | 'waiting' | 'watching'

const states = Object.keys(stateChip) as EntryState[]
const asState = (v: string | null) => states.find((s) => s === v)
const asWindow = (v: string | null): RecoveryWindow => (v === '24h' || v === '30d' || v === 'all' ? v : '7d')
const asTab = (v: string | null): Tab => (v === 'waiting' || v === 'watching' ? v : 'all')
const sentence = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)

/**
 * Evidence ledger (build-plan unit E7; design `p12-evidence-ledger`): every recovery action with who did it, each step and what was
 * observed afterwards, across one cloud or all of them. The same two reads and the same words as the 4.2.0 Recovery Ledger — its entry
 * panel and Export moved to `components/advanced/LedgerParts` — so the two cannot disagree. The page investigates; it never acts:
 * "Check the chain" and Export are reads, and a Declined entry still waiting is answered on Pending approvals.
 */
export default function LedgerPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const { pathname } = useLocation()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const me = useMe()
  const components = useComponents(cloud)

  const tab = asTab(params.get('tab'))
  const state = asState(params.get('state'))
  const window = asWindow(params.get('window'))
  const by = params.get('by') === 'people' || params.get('by') === 'autonomous' ? (params.get('by') as 'people' | 'autonomous') : undefined
  const namespaceId = params.get('connection') ?? undefined
  const page = Math.max(1, Number(params.get('page')) || 1)
  const entry = params.get('entry')
  const stateFilter = tab === 'watching' ? 'Observing' : state

  const change = (patch: Record<string, string | null>) => setParams((current) => {
    const next = new URLSearchParams(current)
    for (const [k, v] of Object.entries(patch)) { if (v === null || v === '') next.delete(k); else next.set(k, v) }
    if (!('page' in patch) && !('entry' in patch)) next.delete('page')
    return next
  }, { replace: true })

  const summary = useRecoverySummary({ window, provider: cloud ?? undefined, namespaceId })
  const ledger = useLedger({ window, provider: cloud ?? undefined, namespaceId, state: stateFilter, by, page, pageSize })
  const waiting = usePendingWork({ ...(cloud ? { provider: cloud } : {}), ...(namespaceId ? { namespaceId } : {}) })
  const needsPerson = (waiting.data?.items ?? []).filter((i) => i.kind === 'approval' || i.kind === 'unresolved')
  const { items: waitingShown, pager: waitingPager } = usePagedList(needsPerson, tab)

  const [chain, setChain] = useState<{ busy: boolean; result?: ChainVerification; failed?: boolean }>({ busy: false })
  const check = async () => {
    setChain({ busy: true })
    try { setChain({ busy: false, result: await verifyChain() }) } catch { setChain({ busy: false, failed: true }) }
  }

  const connections = [...new Map((components.data?.components ?? []).map((c) => [c.namespaceId, c.name])).entries()]
  const countOf = (s: EntryState) => summary.data?.states.find((x) => x.state === s)?.count ?? 0
  const rows = ledger.data?.items ?? []
  const now = new Date()
  const filtering = !!state || !!by || !!namespaceId
  const entryLinks: EntryLinks = { message: (id) => `${pathname}?dialog=message&id=${id}`, replay: (id) => `${pathname}?dialog=replay&id=${id}` }
  const tabs: { id: Tab; label: string; n: number | undefined }[] = [
    { id: 'all', label: 'All', n: summary.data?.total }, { id: 'waiting', label: 'Waiting', n: waiting.data ? needsPerson.length : undefined }, { id: 'watching', label: 'Watching', n: summary.data ? countOf('Observing') : undefined },
  ]
  // "Where": the connection the entry was on, by name. Two clouds can each own an `orders`, so an unmatched or ambiguous name is shown as recorded.
  const connectionOf = (r: LedgerEntry) => r.namespaceName ?? '—'

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Evidence ledger" updatedAtMs={Math.max(ledger.dataUpdatedAt, summary.dataUpdatedAt)}
        onRefresh={() => { void client.invalidateQueries({ queryKey: ['recovery'] }); void client.invalidateQueries({ queryKey: ['pending-work'] }) }}
        lookingAt={`Every recovery action ${scope === 'all' ? 'on every connected cloud' : `on ${cloudLabel[scope]}`} with who did it, each step, and exactly what was observed afterwards.`} />

      <div role="group" aria-label="Filters" className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Connection:</span>
          <select className={field} value={namespaceId ?? ''} onChange={(e) => change({ connection: e.target.value || null })}>
            <option value="">all</option>{connections.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
          </select></label><Tip label="Connection:" />
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">State:</span>
          <select className={field} value={tab === 'watching' ? 'Observing' : (state ?? '')} disabled={tab !== 'all'} onChange={(e) => change({ state: e.target.value || null })}>
            <option value="">all</option>{states.map((s) => <option key={s} value={s}>{stateChip[s]}</option>)}
          </select></label><Tip label="State:" />
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Who:</span>
          <select className={field} value={by ?? ''} onChange={(e) => change({ by: e.target.value || null })}>
            <option value="">anyone</option><option value="people">people</option><option value="autonomous">the Agent</option>
          </select></label><Tip label="Who:" />
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">When:</span>
          <select className={field} value={window} onChange={(e) => change({ window: e.target.value === '7d' ? null : e.target.value })}>
            {recoveryWindows.map((w) => <option key={w.id} value={w.id}>{w.label.toLowerCase()}</option>)}
          </select></label><Tip label="When:" />
        {filtering && <button type="button" onClick={() => change({ state: null, by: null, connection: null })} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
      </div>

      <div className="grid gap-4">
        <section aria-labelledby="ledger-title" className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <h2 id="ledger-title" className="sr-only">Ledger entries</h2>
          <div role="tablist" aria-label="Ledger views" className="flex gap-5 border-b border-[var(--color-border)] px-4 pt-3">
            {tabs.map((t) => (
              <button key={t.id} role="tab" type="button" aria-selected={tab === t.id} onClick={() => change({ tab: t.id === 'all' ? null : t.id, state: null })}
                className={`-mb-px border-b-2 px-1 py-2.5 text-sm font-semibold ${tab === t.id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>{t.label} ({t.n ?? '…'})</button>
            ))}
          </div>
          <Tip label="Tabs" />

          {tab === 'waiting' ? (
            waiting.isError ? <p role="alert" className="px-4 py-6 text-sm">What is waiting could not be read.</p>
              : waitingShown.length === 0 ? <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Nothing is waiting for a person. When the Agent stops and asks, it appears here and in the bell.</p>
              : (
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <caption className="sr-only">Entries waiting for a person</caption>
                    <thead><tr><Th context="ledger">Waiting</Th><Th context="ledger">Message</Th><Th context="ledger">Where</Th><Th context="ledger">State</Th><Th context="ledger">Why</Th><Th context="ledger">Actions</Th></tr></thead>
                    <tbody>
                      {waitingShown.map((i) => {
                        const hand = i.type === 'handOff' || i.kind === 'unresolved'
                        const may = permission(me.data, 'Approver', 'approve or decline a replay', { recover: true, namespaceId: i.namespaceId ?? undefined })
                        const id = encodeURIComponent(i.entryId ?? i.id)
                        return (
                          <tr key={i.id} className="border-t border-[var(--color-border)]">
                            <td className={td}>{formatAge(i.since, now)}</td>
                            <td className={`${td} font-mono text-xs`}>{i.dlqMessageId !== null ? `#${i.dlqMessageId}` : '—'}</td>
                            <td className={td}>{[i.namespaceName, i.entity && entityLabel(i.entity, null)].filter(Boolean).join(' · ') || '—'}</td>
                            <td className={td}>{hand ? 'Handed to a person' : 'Waiting for approval'}</td>
                            <td className={`${td} max-w-sm`}>{i.reason}</td>
                            <td className={td}>{hand || may.allowed
                              ? <Link to={`${pathname}?${hand ? 'panel=hand-off' : 'dialog=review'}&entry=${id}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)]">{hand ? 'View' : 'Review'}</Link>
                              : <span title={may.reason ?? undefined} className="text-xs text-[var(--color-text-muted)]">Needs an Approver</span>}</td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                  <Pager {...waitingPager} label="waiting entries" />
                </div>
              )
          ) : ledger.isError ? (
            <p role="alert" className="px-4 py-6 text-sm text-red-800">The ledger could not be read. Nothing is shown rather than a list that may be incomplete.</p>
          ) : ledger.isPending ? (
            <p role="status" className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
          ) : rows.length === 0 ? (
            <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">{filtering || tab === 'watching' ? 'No entry matches these filters in this period.' : 'No recovery action in this period. When something is replayed, it is recorded here.'}</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full">
                <caption className="sr-only">Recovery ledger entries, newest first</caption>
                <thead>
                  <tr>
                    <Th context="ledger">When</Th>{scope === 'all' && <Th context="ledger">Cloud</Th>}<Th context="ledger">Message</Th><Th context="ledger">Where</Th><Th context="ledger">Who</Th>
                    <Th context="ledger">State</Th><Th context="ledger">What was observed</Th><Th context="ledger">How, and its limits</Th><Th context="ledger">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} aria-selected={entry === r.id} className={`border-t border-[var(--color-border)] ${entry === r.id ? 'bg-[var(--color-primary-50)]' : ''}`}>
                      <td className={td}>{formatWhen(r.begunAt, now)}</td>
                      {scope === 'all' && <td className={td}>{r.provider ? cloudLabel[r.provider] : '—'}</td>}
                      <td className={`${td} font-mono text-xs`}>{r.messageId ? <span title={r.messageId} className="block max-w-[6rem] truncate">{r.messageId}</span> : '—'}</td>
                      <td className={td}>{connectionOf(r)}<span className="block text-xs text-[var(--color-text-muted)]">{entityLabel(r.entityName, null)}</span></td>
                      <td className={td}><ByCell actor={r.actor} at={r.begunAt} /></td>
                      <td className={td}><StateChip state={r.state} /></td>
                      <td className={td}>{sentence(stateMeaning[r.state])}</td>
                      <td className={td}>{r.state === 'Returned' && r.confidence ? (r.confidence === 'Exact' ? 'Recognised by its recovery ID' : 'Recognised by its contents, which may be shared') : '—'}</td>
                      <td className={td}>
                        <button type="button" aria-pressed={entry === r.id} aria-label={`Open ledger entry from ${formatWhen(r.begunAt, now)}`} onClick={() => change({ entry: entry === r.id ? null : r.id })}
                          className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Open</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <Pager page={ledger.data?.page ?? 1} pageSize={ledger.data?.pageSize ?? pageSize} total={ledger.data?.total ?? 0} filtered={filtering || tab === 'watching'} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({ page: null }) }} label="ledger entries" />
            </div>
          )}

          <div className="flex flex-wrap items-center gap-3 border-t border-[var(--color-border)] px-4 py-3 text-xs text-[var(--color-text-muted)]">
            <span className="min-w-0 flex-1">Each entry keeps the message’s identity, who acted, every step, and what was observed. Entries are chained by hash, so a changed or missing entry is detectable.</span>
            <button type="button" onClick={() => void check()} disabled={chain.busy} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-semibold text-[var(--color-text)] disabled:opacity-60"><Hash className="h-4 w-4" aria-hidden="true" /> {chain.busy ? 'Checking…' : 'Check the chain'}</button>
            <Tip label="Check the chain" />
            <ExportEvidence window={window} />
          </div>
          {chain.result?.isValid && <p role="status" className="flex items-start gap-2 px-4 pb-3 text-sm"><ShieldCheck className="mt-0.5 h-4 w-4 text-[var(--color-success)]" aria-hidden="true" /><span><b>Chain verified</b> — {chain.result.eventsChecked.toLocaleString()} events, unbroken.</span></p>}
          {chain.result && !chain.result.isValid && <p role="alert" className="px-4 pb-3 text-sm text-[var(--color-error)]"><b>The chain does not verify</b>{chain.result.firstDivergentSeq !== null ? ` — first divergence at event ${chain.result.firstDivergentSeq}` : ''}. {chain.result.reason}</p>}
          {chain.failed && <p role="alert" className="px-4 pb-3 text-sm text-[var(--color-error)]">ServiceHub couldn’t run the check.</p>}
        </section>
        {entry && (
          <DetailWindow title="Ledger entry" description="What was done, by whom, and what was observed afterwards" onClose={() => change({ entry: null })}>
            <EntryPanel windowed id={entry} onClose={() => change({ entry: null })} links={entryLinks} />
          </DetailWindow>
        )}
      </div>

      <section aria-labelledby="reading-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <h2 id="reading-title" className="text-base font-semibold">Reading the evidence<Tip label="section:Reading the evidence" /></h2>
        <table className="mt-2 w-full">
          <caption className="sr-only">What each observation permits</caption>
          <thead><tr><Th context="ledger">What was observed</Th><Th context="ledger">It permits</Th><Th context="ledger">It does not permit</Th></tr></thead>
          <tbody>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Accepted by the cloud</td><td className={td}>“The send call succeeded.”</td><td className={td}>That it arrived, or was processed.</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Did not return</td><td className={td}>“It did not come back to dead-letter in the window.”</td><td className={td}>That the consumer finished its work.</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Seen again in dead-letter</td><td className={td}>“It failed again.”</td><td className={td}>—</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Processed by the consumer</td><td className={`${td} text-[var(--color-text-muted)]`} colSpan={2}>ServiceHub has no way to observe this today.</td></tr>
          </tbody>
        </table>
        <p className="mt-2 text-xs text-[var(--color-text-muted)]">The observations are separate facts, not steps on one ladder. “Did not return” does not prove the message was delivered.</p>
      </section>

      <p className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3 text-sm"><b>This page investigates. It never acts on its own.</b> Review and Replay open the same dialogs as everywhere else.</p>
    </div>
  )
}
