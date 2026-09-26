import { Bot, Check, Pause, TriangleAlert, X } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Link, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { AgentRow, healthTone, healthWord } from '../../components/agent/AgentRow'
import { useAgentActivity, useAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { authorityWords, cadenceWords, type Agent, type AgentActivityItem } from '../../lib/api/agents'
import { formatWhen } from '../../lib/format'

const kindWord = { watch: 'Watch', decide: 'Decide', act: 'Act', maintain: 'Maintain' } as const

/**
 * Agents (Advanced, unit 4.5): the machinery that runs in the background — what each one does, what it may do on its
 * own, and how it is doing. Rendered entirely from `/agents`; there is no per-agent code here, so a newly registered
 * agent appears with zero changes to this page. It leads with the acting agents, because how few can change anything
 * is the safety story. Pause is the one control (ADR-0016 D3): it only takes authority away. Resume is on Home.
 */
export default function AgentsPage() {
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('agents')
  const agents = useAgents()
  const setPaused = useSetAgentsPaused()
  const now = new Date()
  const mayPause = permission(useMe().data, 'Operator', 'pause an agent')

  const all = agents.data ?? []
  const acting = all.filter((a) => a.canAct)
  const watching = all.filter((a) => !a.canAct)
  const selectedId = params.get('agent') ?? acting[0]?.id ?? all[0]?.id ?? null
  const selected = all.find((a) => a.id === selectedId) ?? null
  const select = (id: string) => setParams((p) => { const n = new URLSearchParams(p); n.set('agent', id); return n }, { replace: true })
  const pause = (a: Agent) => setPaused.mutate({ ids: [a.id], paused: true })

  return (
    <section className="px-6 py-6">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">
            <Bot className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Agents <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">The machinery that runs in the background — what each one does, what it may do on its own, and how it’s doing.</p>
        </div>
        {agents.data && (
          <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-2 text-right">
            <p className="text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Registered in this build</p>
            <p className="font-semibold">{all.length} {all.length === 1 ? 'agent' : 'agents'}</p>
          </div>
        )}
      </header>

      {explainer.shown && <ExplainerCard id="agents" onDismiss={explainer.dismiss} />}

      {agents.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the agents…</p>}
      {agents.isError && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read its agents.</p>
          <button type="button" onClick={() => void agents.refetch()} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
        </div>
      )}
      {setPaused.isError && <p role="alert" className="mb-3 text-sm text-[#b91c1c]">The pause didn’t go through. Nothing changed — try again.</p>}

      {agents.data && all.length === 0 && (
        <p className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-6 text-sm">No agents are registered in this build.</p>
      )}

      {agents.data && all.length > 0 && (
        <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_420px]">
          <div className="space-y-4">
            <Group title={`ACTING (${acting.length}) — the only ${acting.length === 1 ? 'one' : 'ones'} that can change anything`} tone="acting">
              {acting.map((a) => <AgentRow key={a.id} agent={a} now={now} selected={a.id === selectedId} onSelect={() => select(a.id)} onPause={() => pause(a)} pausing={setPaused.isPending || !mayPause.allowed} />)}
            </Group>
            <Group title={`WATCHING (${watching.length}) — look, record and decide, never change the cloud`} tone="watching">
              {watching.map((a) => <AgentRow key={a.id} agent={a} now={now} selected={a.id === selectedId} onSelect={() => select(a.id)} onPause={() => pause(a)} pausing={setPaused.isPending || !mayPause.allowed} />)}
            </Group>
            <p className="text-xs text-[var(--color-text-muted)]">This list is read from the running server. An agent appears here the moment it is registered, and never before.</p>
          </div>
          {selected && <Detail agent={selected} onPause={() => pause(selected)} pausing={setPaused.isPending || !mayPause.allowed} notAllowed={mayPause.reason} />}
        </div>
      )}
    </section>
  )
}

function Group({ title, tone, children }: { title: string; tone: 'acting' | 'watching'; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <h2 className={`px-5 py-3 text-[15px] font-bold ${tone === 'acting' ? 'bg-[#fffbeb] text-[#92400e]' : 'text-[var(--color-text)]'}`}>{title}</h2>
      <ul>{children}</ul>
    </section>
  )
}

const cycleTone: Readonly<Record<string, string>> = {
  idle: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]',
  looked: 'bg-[var(--color-primary-50)] text-[var(--color-primary-700)]',
  acted: 'bg-[var(--color-success-light)] text-[#047857]',
  degraded: 'bg-[var(--color-warning-light)] text-[#92400e]',
  failed: 'bg-[var(--color-error-light)] text-[#b91c1c]',
  paused: 'bg-[var(--color-warning-light)] text-[#92400e]',
  resumed: 'bg-[var(--color-success-light)] text-[#047857]',
}

function Detail({ agent, onPause, pausing, notAllowed }: { agent: Agent; onPause: () => void; pausing: boolean; notAllowed: string | null }) {
  const activity = useAgentActivity(agent.id)
  const now = new Date()
  // Idle cycles are folded: a run of "nothing to do" is one line, not twenty.
  const items = fold(activity.data?.items ?? [])

  return (
    <aside aria-label={`${agent.name} details`} className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="border-b border-[var(--color-border)] px-5 py-4">
        <div className="flex items-center justify-between gap-2">
          <h2 className="text-lg font-bold">{agent.name}</h2>
          <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${healthTone[agent.health]}`}>{healthWord[agent.health]}</span>
        </div>
        <p className="text-sm text-[var(--color-text-muted)]">{kindWord[agent.kind]} · {authorityWords(agent.authority).toLowerCase()} · {cadenceWords(agent.cadenceSeconds)}</p>
      </div>
      <div className="space-y-4 px-5 py-4 text-sm">
        {agent.isPaused ? (
          <div className="rounded-xl border border-[#fcd34d] bg-[#fffbeb] p-4">
            <p className="font-bold text-[#92400e]">{agent.name} is paused</p>
            <p className="mt-1 text-[#92400e]">Its cycles are skipped, so it will not act. Nothing it already did is undone. Resume it from Home’s agent bar.</p>
            <Link to="/" className="mt-2 inline-block font-semibold text-[var(--color-primary-700)] hover:underline">Go to Home to resume ›</Link>
          </div>
        ) : (
          <div className="flex items-center gap-3 rounded-xl border border-[#fcd34d] bg-[#fffbeb] p-4">
            <div className="flex-1">
              <p className="font-bold text-[#92400e]">Pause {agent.name}</p>
              <p className="mt-1 text-[#92400e]">
                {agent.canAct
                  ? <>It will <b>not act</b> until someone resumes it. Nothing it already did is undone.</>
                  : <>It will stop running its cycles — it records nothing new until resumed. Nothing it already did is undone.</>}
              </p>
            </div>
            <button type="button" onClick={onPause} disabled={pausing} className="inline-flex items-center gap-1.5 rounded-lg bg-[#d97706] px-3.5 py-2 font-semibold text-white hover:bg-[#b45309] disabled:opacity-60">
              <Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pause
            </button>
          </div>
        )}
        {!agent.isPaused && <NotAllowed reason={notAllowed} />}

        {agent.notes && <p className="rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-[13px]">{agent.notes}</p>}

        {(agent.may.length > 0 || agent.mayNot.length > 0) && (
          <section>
            <h3 className="mb-1.5 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">What it may and may not do</h3>
            <ul className="divide-y divide-[var(--color-border)]">
              {agent.may.map((m) => <li key={m} className="flex items-start gap-2 py-1.5"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]" aria-label="May" />{m}</li>)}
              {agent.mayNot.map((m) => <li key={m} className="flex items-start gap-2 py-1.5"><X className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-label="Never" />{m}</li>)}
            </ul>
          </section>
        )}

        <section>
          <h3 className="mb-1.5 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
            Recent activity <span className="font-normal normal-case tracking-normal">— idle cycles folded</span>
          </h3>
          {activity.isPending && <p role="status" className="text-[var(--color-text-muted)]">Reading its activity…</p>}
          {activity.isError && <p className="text-[var(--color-text-muted)]">ServiceHub couldn’t read its activity.</p>}
          {activity.data && items.length === 0 && <p className="text-[var(--color-text-muted)]">Nothing yet since the server started.</p>}
          <ul className="divide-y divide-[var(--color-border)]">
            {items.map((i, n) => (
              <li key={`${i.at}-${n}`} className="flex items-start gap-3 py-2">
                <span className="w-14 shrink-0 font-mono text-[12px] text-[var(--color-text-muted)]">{formatWhen(i.at, now)}</span>
                <span className="min-w-0 flex-1">{i.text}{i.by ? ` — ${i.by}` : ''}</span>
                <span className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-semibold ${cycleTone[i.kind] ?? cycleTone.looked}`}>{i.source === 'ledger' ? 'recorded' : i.kind}</span>
              </li>
            ))}
          </ul>
          {activity.data?.cyclesSinceUtc && <p className="mt-1 text-xs text-[var(--color-text-muted)]">Cycles shown are those since the server last started.</p>}
        </section>
        {agent.canAct && <Link to="/advanced/ledger" className="inline-block font-semibold text-[var(--color-primary-700)] hover:underline">Everything it did, in the Ledger ›</Link>}
      </div>
    </aside>
  )
}

/**
 * Folds runs of cycles so the timeline shows what changed: consecutive idle cycles become one line that counts them,
 * and consecutive cycles that said exactly the same thing become one line marked "×N".
 */
export function fold(items: readonly AgentActivityItem[]): AgentActivityItem[] {
  const out: AgentActivityItem[] = []
  let run = 0
  let base: AgentActivityItem | null = null
  for (const i of items) {
    const prev = out[out.length - 1]
    const same = base && prev && i.source === 'cycle' && base.source === 'cycle' && i.kind === base.kind && (i.kind === 'idle' || i.text === base.text)
    if (same) {
      run++
      out[out.length - 1] = { ...prev, text: base!.kind === 'idle' ? `${run} cycles with nothing to do` : `${base!.text} ×${run}` }
    } else {
      run = 1
      base = i
      out.push(i)
    }
  }
  return out
}
