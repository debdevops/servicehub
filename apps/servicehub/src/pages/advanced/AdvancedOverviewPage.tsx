import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, Bot, Info, LayoutGrid, Lock, ShieldCheck, Zap } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { useAgents } from '../../hooks/useAgents'
import { useNamespaces } from '../../hooks/useNamespaces'
import { usePendingWork } from '../../hooks/usePendingWork'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import { fetchAgentActivity } from '../../lib/api/agents'
import type { CloudProvider } from '../../lib/api/namespaces'
import type { EntryState, RecoveryWindow, StateCount } from '../../lib/api/recovery'
import { fetchAuthority, type AuthoritySpread } from '../../lib/api/signatures'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { InsightsTab } from '../../components/advanced/InsightsTab'

const windows: readonly { id: RecoveryWindow; label: string; days: number }[] = [
  { id: '24h', label: 'Last 24 hours', days: 1 },
  { id: '7d', label: 'Last 7 days', days: 7 },
  { id: '30d', label: 'Last 30 days', days: 30 },
]

/** Each state once, in the order a person reads them, with its colour and its plain meaning. Recovered and Unverified never merge (V1). */
const states: readonly { state: EntryState; label: string; meaning: string; color: string }[] = [
  { state: 'Recovered', label: 'Recovered', meaning: 'Did not come back for the whole observation window.', color: '#10b981' },
  { state: 'Unverified', label: 'Unverified', meaning: 'Replayed — but the cloud cannot prove the queue drained. Not a failure.', color: '#f59e0b' },
  { state: 'Returned', label: 'Returned', meaning: 'The failure came back inside the window. The fix did not hold.', color: '#ef4444' },
  { state: 'ExecutionFailed', label: 'Execution failed', meaning: 'The cloud refused the call. Nothing was replayed.', color: '#9ca3af' },
  { state: 'Observing', label: 'Being watched', meaning: 'Replayed; the observation window is still open.', color: '#38bdf8' },
  { state: 'ExecutionUnknown', label: 'Unknown', meaning: 'ServiceHub lost contact before the cloud answered.', color: '#6b7280' },
  { state: 'Discarded', label: 'Discarded', meaning: 'Purged on purpose, with a reason.', color: '#d1d5db' },
  { state: 'Declined', label: 'Declined', meaning: 'A person said no to what the Agent asked.', color: '#e5e7eb' },
  { state: 'WrittenOff', label: 'Written off', meaning: 'Left as it is, on purpose.', color: '#e5e7eb' },
  { state: 'Expired', label: 'Expired', meaning: 'Waited too long for a decision.', color: '#e5e7eb' },
]

const heldWords: Readonly<Record<AuthoritySpread['held'][number]['reason'], { text: string; link?: { label: string; to: string } }>> = {
  cannot_verify: { text: 'the cloud can’t verify the queue drained', link: { label: 'Why →', to: '?panel=help' } },
  needs_evidence: { text: 'not enough verified outcomes yet' },
  rate_too_low: { text: 'fixes have not held often enough' },
  production: { text: 'production — never automatic in 4.1.0' },
  awaiting_evaluation: { text: 'earned it; waiting for the next evaluation' },
}

/**
 * Advanced Overview (unit 6.8): the control plane — what happened, why it was decided, what needs a person — broken down,
 * never rounded off. Every panel reads one named endpoint; none can act (ADR-0016 D3), so every "do" is a link into Simple.
 */
export default function AdvancedOverviewPage() {
  const [params, setParams] = useSearchParams()
  const window = windows.find((w) => w.id === params.get('window')) ?? windows[0]
  const provider = (['azure', 'aws', 'gcp'] as const).find((p) => p === params.get('provider'))
  const tab = params.get('tab') === 'insights' ? 'insights' : 'overview'
  const set = (k: string, v: string | null) => setParams((c) => { const n = new URLSearchParams(c); if (v) n.set(k, v); else n.delete(k); return n }, { replace: true })
  const namespaces = useNamespaces().data ?? []
  const clouds = [...new Set(namespaces.map((n) => n.provider))]

  return (
    <section className="px-6 py-6">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold"><LayoutGrid className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Advanced Overview</h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">The control plane. What Simple states as an outcome, Advanced states as a breakdown.</p>
        </div>
        <div className="flex gap-2 text-sm">
          <label className="flex flex-col text-xs uppercase tracking-wide text-[var(--color-text-muted)]">Scope
            <select value={provider ?? ''} onChange={(e) => set('provider', e.target.value || null)} className="mt-0.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm normal-case text-[var(--color-text)]">
              <option value="">All clouds</option>
              {clouds.map((c) => <option key={c} value={c}>{providerLabel[c]}</option>)}
            </select>
          </label>
          <label className="flex flex-col text-xs uppercase tracking-wide text-[var(--color-text-muted)]">Window
            <select value={window.id} onChange={(e) => set('window', e.target.value === '24h' ? null : e.target.value)} className="mt-0.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm normal-case text-[var(--color-text)]">
              {windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
            </select>
          </label>
        </div>
      </header>

      <nav aria-label="Overview sections" className="mb-4 flex gap-1 border-b border-[var(--color-border)]">
        {([['overview', 'Overview'], ['insights', 'Insights']] as const).map(([id, label]) => (
          <button key={id} type="button" aria-current={tab === id ? 'page' : undefined} onClick={() => set('tab', id === 'overview' ? null : id)}
            className={`-mb-px border-b-2 px-3 py-2 text-sm font-semibold ${tab === id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>{label}</button>
        ))}
      </nav>

      {tab === 'insights' ? <InsightsTab provider={provider} /> : (
        <div className="space-y-4">
          <Attention provider={provider} />
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
            <Recovery provider={provider} window={window.id} label={window.label} />
            <Authority provider={provider} days={window.days} />
          </div>
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
            <Agents />
            <Capability />
          </div>
          <WhatChanged />
        </div>
      )}
    </section>
  )
}

const card = 'rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5'
const h2 = 'text-[15px] font-bold'

function Attention({ provider }: { provider?: CloudProvider }) {
  const pending = usePendingWork({ provider })
  if (!pending.data) return pending.isError ? <p role="alert" className={card}>ServiceHub couldn’t read what needs you.</p> : null
  const groups = (['approval', 'rule', 'agent'] as const).map((kind) => ({ kind, items: pending.data.items.filter((i) => i.kind === kind) })).filter((g) => g.items.length > 0)
  if (groups.length === 0) {
    return <p className={`${card} flex items-center gap-2 text-sm`}><ShieldCheck className="h-4 w-4 text-[var(--color-success)]" aria-hidden="true" /> Nothing needs a person right now.</p>
  }
  const words = { approval: ['approval waiting', 'approvals waiting', 'See them waiting →', '/advanced/ledger?state=Waiting'], rule: ['rule stopped itself', 'rules stopped themselves', 'Open the rule →', '/?panel=rules'], agent: ['agent needs you', 'agents need you', 'All agents →', '/advanced/agents'] } as const
  return (
    <section aria-label="Needs your attention" className="overflow-hidden rounded-2xl border border-[#fcd34d] bg-[#fffbeb]">
      <h2 className="flex items-center gap-2 border-b border-[#fde68a] px-5 py-3 text-[15px] font-bold text-[#92400e]"><AlertTriangle className="h-4 w-4" aria-hidden="true" /> Needs your attention <span className="ml-auto text-xs font-semibold">{pending.data.total} {pending.data.total === 1 ? 'thing' : 'things'}</span></h2>
      <ul className="divide-y divide-[#fde68a]">
        {groups.map(({ kind, items }) => {
          const [one, many, link, to] = words[kind]
          return (
            <li key={kind} className="flex items-center gap-4 px-5 py-3 text-sm">
              <span className="text-xl font-extrabold text-[#b45309]">{items.length}</span>
              <span className="min-w-0 flex-1"><b>{items.length === 1 ? one : many}</b><span className="block truncate font-mono text-xs text-[var(--color-text-muted)]">{items.slice(0, 3).map((i) => i.entity ?? i.ruleName ?? i.agentId).filter(Boolean).join(' · ')} — {items[0].reason}</span></span>
              <Link to={to} className="whitespace-nowrap font-semibold text-[var(--color-primary-700)] hover:underline">{link}</Link>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

const countOf = (s: readonly StateCount[], state: EntryState) => s.find((x) => x.state === state)?.count ?? 0

function Recovery({ provider, window, label }: { provider?: CloudProvider; window: RecoveryWindow; label: string }) {
  const summary = useRecoverySummary({ window, provider })
  const d = summary.data
  return (
    <section aria-label="Recovery" className={card}>
      <h2 className={`${h2} flex flex-wrap items-baseline gap-2`}>Recovery — {label.toLowerCase()}
        {d && <span className="ml-auto text-xs font-normal text-[var(--color-text-muted)]">{d.total} {d.total === 1 ? 'operation' : 'operations'}{d.stayedFixedRate !== null && <> · <b className="text-[var(--color-text)]">{Math.round(d.stayedFixedRate * 100)}% stayed fixed</b></>}</span>}
      </h2>
      {summary.isError && <p role="alert" className="mt-2 text-sm">ServiceHub couldn’t read the recovery summary.</p>}
      {d && d.total === 0 && <p className="mt-3 text-sm text-[var(--color-text-muted)]">No recoveries in this window.</p>}
      {d && d.total > 0 && (
        <>
          <div className="mt-3 flex h-3 overflow-hidden rounded-full" role="img" aria-label={states.filter((s) => countOf(d.states, s.state) > 0).map((s) => `${countOf(d.states, s.state)} ${s.label}`).join(', ')}>
            {states.map((s) => { const n = countOf(d.states, s.state); return n > 0 ? <span key={s.state} style={{ width: `${(n / d.total) * 100}%`, background: s.color }} /> : null })}
          </div>
          <ul className="mt-3 divide-y divide-[var(--color-border)]">
            {states.map((s) => {
              const n = countOf(d.states, s.state)
              if (n === 0) return null
              return (
                <li key={s.state} className="flex items-start gap-3 py-2 text-sm">
                  <span aria-hidden="true" className="mt-1 h-3 w-3 shrink-0 rounded" style={{ background: s.color }} />
                  <span className="w-8 text-right text-base font-extrabold tabular">{n}</span>
                  <span className="min-w-0 flex-1"><Link to={`/advanced/ledger?state=${s.state}&window=${window}${provider ? `&provider=${provider}` : ''}`} className="font-semibold hover:underline">{s.label}</Link><span className="block text-xs text-[var(--color-text-muted)]">{s.meaning}</span></span>
                </li>
              )
            })}
          </ul>
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">Zero-count states are left out of the list; the Ledger shows all of them.</p>
          {d.byProvider.length > 1 && (
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              {d.byProvider.map((p) => (
                <div key={p.provider} className="rounded-xl border border-[var(--color-border)] px-3 py-2 text-xs">
                  <b className="text-sm">{providerLabel[p.provider]}</b> · {p.total} ops
                  <span className="block text-[var(--color-text-muted)]">{countOf(p.states, 'Recovered')} proven · {countOf(p.states, 'Unverified')} unprovable · {countOf(p.states, 'Returned')} returned</span>
                </div>
              ))}
            </div>
          )}
          <p className="mt-3 flex gap-2 rounded-xl bg-[var(--color-primary-50)] px-3 py-2 text-xs"><Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /><span><b>“Recovered” means the failure did not come back</b> — not that the business transaction completed. Unverified replays were sent; what is unproven is the confirmation, not the replay.</span></p>
        </>
      )}
    </section>
  )
}

function Authority({ provider, days }: { provider?: CloudProvider; days: number }) {
  const q = useQuery({ queryKey: ['authority', provider, days], queryFn: () => fetchAuthority({ provider, days }) })
  const a = q.data
  const max = a ? Math.max(1, a.unattended, a.standing, a.approve) : 1
  const bar = (n: number, color: string) => <span aria-hidden="true" className="inline-block h-3 rounded" style={{ width: `${Math.max(6, (n / max) * 100)}px`, background: color }} />
  return (
    <section aria-label="Authority — and why" className={card}>
      <h2 className={h2}>Authority — and why</h2>
      <p className="text-xs text-[var(--color-text-muted)]">What each failure may do on its own — and what holds it there.{a ? ` ${a.total} signatures seen.` : ''}</p>
      {q.isPending && <p role="status" className="mt-2 text-sm text-[var(--color-text-muted)]">Counting…</p>}
      {q.isError && <p role="alert" className="mt-2 text-sm">ServiceHub couldn’t read authority.</p>}
      {a && (
        <dl className="mt-3 space-y-3 text-sm">
          <div className="flex items-center gap-3"><dt className="w-28 font-semibold">Unattended</dt><dd className="flex items-center gap-2">{bar(a.unattended, '#0284c7')} <b>{a.unattended}</b></dd></div>
          <div className="flex items-center gap-3"><dt className="w-28 font-semibold">Standing</dt><dd className="flex items-center gap-2">{bar(a.standing, '#38bdf8')} <b>{a.standing}</b></dd></div>
          <div className="flex items-start gap-3">
            <dt className="w-28 font-semibold">A person approves</dt>
            <dd className="min-w-0 flex-1">
              <span className="flex items-center gap-2">{bar(a.approve, '#f59e0b')} <b>{a.approve}</b></span>
              <ul className="mt-1 space-y-1 text-xs">
                {a.held.map((h) => (
                  <li key={h.reason} className="flex gap-2"><b className="text-[#b45309]">{h.count}</b> <span className="flex-1">{heldWords[h.reason].text}{h.reason === 'needs_evidence' ? ` — ${a.needs.sample} at ${Math.round(a.needs.rate * 100)}% to move up` : ''}</span>{heldWords[h.reason].link && <Link to={heldWords[h.reason].link!.to} className="font-semibold text-[var(--color-primary-700)]">{heldWords[h.reason].link!.label}</Link>}</li>
                ))}
              </ul>
              <p className="mt-2 inline-block rounded-full bg-[#fef3c7] px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-wide text-[#92400e]">A person approving is a floor — not a stage to graduate from</p>
            </dd>
          </div>
        </dl>
      )}
      {a?.capped && <p className="mt-2 text-xs text-[var(--color-text-muted)]">Counted over the first 500 signatures.</p>}
    </section>
  )
}

function Agents() {
  const agents = useAgents().data
  if (!agents) return null
  const acting = agents.filter((a) => a.canAct && !a.isPaused).length
  const paused = agents.filter((a) => a.isPaused).length
  return (
    <section aria-label="Agents" className={card}>
      <h2 className={h2}>Agents</h2>
      <p className="mt-2"><span className="text-3xl font-extrabold">{agents.length}</span> <span className="text-sm text-[var(--color-text-muted)]">agents registered in this build</span></p>
      <ul className="mt-2 space-y-1 text-sm">
        <li><b className="text-[#b45309]">{acting}</b> acting — the only ones that can change anything</li>
        <li><b>{agents.length - acting - paused}</b> watching</li>
        <li><b>{paused}</b> paused</li>
        {agents.some((a) => a.health === 'failing' || a.late) && <li className="text-[#b91c1c]"><Zap className="mr-1 inline h-3.5 w-3.5" aria-hidden="true" />{agents.filter((a) => a.health === 'failing' || a.late).map((a) => a.name).join(', ')} not reporting on schedule</li>}
      </ul>
      <div className="mt-3 flex gap-2">
        <Link to="/advanced/agents" className="inline-flex flex-1 items-center justify-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3 py-2 text-sm font-semibold text-white"><Bot className="h-4 w-4" aria-hidden="true" /> All agents →</Link>
        <Link to="/advanced/ledger" className="inline-flex flex-1 items-center justify-center rounded-lg border border-[var(--color-border)] px-3 py-2 text-sm font-semibold">Recovery Ledger →</Link>
      </div>
    </section>
  )
}

function Capability() {
  const namespaces = useNamespaces().data ?? []
  const clouds: readonly CloudProvider[] = ['azure', 'aws', 'gcp']
  return (
    <section aria-label="Capability" className={card}>
      <h2 className={h2}>Capability — what each cloud can prove</h2>
      <ul className="mt-2 divide-y divide-[var(--color-border)] text-sm">
        {clouds.map((c) => {
          const here = namespaces.filter((n) => n.provider === c)
          const proves = here.length > 0 && here.every((n) => n.capabilities?.canProveDlqAbsence)
          return (
            <li key={c} className="flex items-center gap-3 py-2.5">
              <b className="w-16">{providerLabel[c]}</b>
              <span className="flex-1 font-mono text-xs text-[var(--color-text-muted)]">{here.length === 0 ? 'not connected' : `${here.length} ns`}</span>
              {here.length > 0 && (proves
                ? <span className="rounded-full bg-[#ecfdf5] px-2 py-0.5 text-xs font-semibold text-[#047857]">✓ Can verify DLQ drain</span>
                : <span className="inline-flex items-center gap-1 rounded-full bg-[#fef3c7] px-2 py-0.5 text-xs font-semibold text-[#92400e]"><Lock className="h-3 w-3" aria-hidden="true" /> Can’t prove a fix held</span>)}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

/** Promotions and demotions together, as the Trust Evaluator recorded them in the ledger (unit 4.1). */
function WhatChanged() {
  const q = useQuery({ queryKey: ['agent-activity', 'autonomy-evaluation'], queryFn: () => fetchAgentActivity('autonomy-evaluation') })
  const changes = (q.data?.items ?? []).filter((i) => i.source === 'ledger')
  const now = new Date()
  return (
    <section aria-label="What changed" className={card}>
      <h2 className={h2}>What changed — autonomy transitions</h2>
      {q.isError && <p role="alert" className="mt-2 text-sm">ServiceHub couldn’t read the transitions.</p>}
      {q.data && changes.length === 0 && <p className="mt-2 text-sm text-[var(--color-text-muted)]">No failure has earned or lost automatic replay yet.</p>}
      {changes.length > 0 && (
        <ul className="mt-2 divide-y divide-[var(--color-border)] text-sm">
          {changes.slice(0, 10).map((c, i) => <li key={i} className="flex gap-3 py-2"><span className="flex-1">{c.text}</span><span className="whitespace-nowrap text-xs text-[var(--color-text-muted)]">{formatWhen(c.at, now)}</span></li>)}
        </ul>
      )}
    </section>
  )
}
