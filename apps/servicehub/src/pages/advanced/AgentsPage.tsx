import { useQueries } from '@tanstack/react-query'
import { Bot, Check, ChevronRight, Eye, Heart, Lightbulb, Pause, TriangleAlert, X } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Link, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { AgentRow, healthTone, healthWord } from '../../components/agent/AgentRow'
import { agentKeys, useAgentActivity, useAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { authorityWords, cadenceWords, fetchAgentActivity, type Agent, type AgentActivityItem } from '../../lib/api/agents'
import { formatWhen } from '../../lib/format'
import { Skeleton } from '../../components/ui/Skeleton'
import { sectionHelp } from '../../content/sections'
import { InfoTip } from '../../components/ui/InfoTip'

const kindWord = { watch: 'Watch', decide: 'Decide', act: 'Act', maintain: 'Maintain' } as const

/**
 * Agents (Advanced, unit 4.5): the machinery that runs in the background — what each one does, what it may do on its
 * own, and how it is doing. Rendered entirely from `/agents`; there is no per-agent code here, so a newly registered
 * agent appears with zero changes to this page. It leads with the acting agents, because how few can change anything
 * is the safety story. Pause is the one control (ADR-0016 D3): it only takes authority away. Resume is on Home.
 *
 * There is no "Add agent" button: an agent is code plus one registration line, so a button here would promise what the page cannot do.
 * The four tiles filter the lists (`?show=`); an agent's detail replaces the summary column (`?agent=`).
 */
export default function AgentsPage() {
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('agents')
  const agents = useAgents()
  const setPaused = useSetAgentsPaused()
  const now = new Date()
  const mayPause = permission(useMe().data, 'Operator', 'pause an agent')

  const all = agents.data ?? []
  const show = (['acting', 'watching', 'healthy', 'paused'] as const).find((v) => v === params.get('show')) ?? null
  const matches = (a: Agent) => show === null || (show === 'acting' ? a.canAct : show === 'watching' ? !a.canAct : show === 'healthy' ? a.health === 'healthy' : a.isPaused)
  const acting = all.filter((a) => a.canAct && matches(a))
  const watching = all.filter((a) => !a.canAct && matches(a))
  const selectedId = params.get('agent')
  const selected = all.find((a) => a.id === selectedId) ?? null
  const edit = (patch: Record<string, string | null>) => setParams((p) => { const n = new URLSearchParams(p); for (const [k, v] of Object.entries(patch)) { if (v === null) n.delete(k); else n.set(k, v) } return n }, { replace: true })
  const select = (id: string) => edit({ agent: id })
  const pause = (a: Agent) => setPaused.mutate({ ids: [a.id], paused: true })
  const count = (f: (a: Agent) => boolean) => all.filter(f).length
  const health = { healthy: count((a) => a.health === 'healthy'), degraded: count((a) => a.health === 'degraded'), unhealthy: count((a) => a.health === 'failing'), paused: count((a) => a.isPaused), unknown: count((a) => a.health === 'unknown') }
  const tile = (id: NonNullable<typeof show>, n: number, title: string, note: string, tone: string, Icon: typeof Bot) => (
    <button key={id} type="button" aria-pressed={show === id} onClick={() => edit({ show: show === id ? null : id, agent: null })}
      className={`flex items-center gap-4 rounded-2xl border bg-[var(--color-surface)] px-5 py-4 text-left hover:shadow-[var(--shadow-card)] ${show === id ? 'border-[var(--color-primary-600)] ring-1 ring-[var(--color-primary-600)]' : 'border-[var(--color-border)]'}`}>
      <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${tone}`}><Icon className="h-6 w-6" aria-hidden="true" /></span>
      <span className="min-w-0 flex-1"><span className="block text-2xl font-extrabold leading-none">{n}</span><span className="mt-1 block text-sm font-semibold">{title}</span><span className="block text-xs text-[var(--color-text-muted)]">{note}</span></span>
      <ChevronRight className="h-5 w-5 shrink-0 text-[var(--color-text-muted)]" aria-hidden="true" />
    </button>
  )

  return (
    <section className="px-6 py-6">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">
            <Bot className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Agents <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">The machinery that runs in the background — what each one does, what it may do on its own, and how it’s doing.</p>
        </div>
      </header>

      {explainer.shown && <ExplainerCard id="agents" onDismiss={explainer.dismiss} />}

      {agents.isPending && <Skeleton label="Reading the agents…" rows={4} />}
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
        <>
          <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {tile('acting', count((a) => a.canAct), 'Acting Agents', 'Can change the system', 'bg-[#dbeafe] text-[#1d4ed8]', Bot)}
            {tile('watching', count((a) => !a.canAct), 'Watching Agents', 'Observe and record only', 'bg-[#f3e8ff] text-[#7e22ce]', Eye)}
            {tile('healthy', health.healthy, 'Healthy', health.healthy === all.length ? 'All agents running normally' : `${all.length - health.healthy} not healthy`, 'bg-[#d1fae5] text-[#047857]', Heart)}
            {tile('paused', health.paused, 'Paused', health.paused === 0 ? 'No agents paused' : 'Will not act until resumed', 'bg-[#fee2e2] text-[#dc2626]', Pause)}
          </div>

          <div className="mb-4 flex flex-wrap items-center gap-3 rounded-2xl border border-[#bae6fd] bg-[#f0f9ff] px-5 py-4">
            <Lightbulb className="h-6 w-6 shrink-0 text-[#0284c7]" aria-hidden="true" />
            <div className="min-w-0 flex-1">
              <p className="font-semibold">How Agents Work</p>
              <p className="text-sm text-[var(--color-text-muted)]">Agents work in the background to keep your messaging systems healthy. Acting agents can make changes (like replaying messages) based on safety rules. Watching agents only observe and record. You can pause any agent at any time.</p>
            </div>
            <Link to="?panel=help" className="inline-flex items-center gap-1 rounded-lg border border-[#bae6fd] bg-white px-4 py-2 text-sm font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Learn more <ChevronRight className="h-4 w-4" aria-hidden="true" /></Link>
          </div>

          <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_400px]">
            <div className="space-y-4">
              {(show === null || show !== 'watching') && (
                <Group title={`Acting Agents (${acting.length})`} tone="acting" summary={groupHealth(acting)}>
                  {acting.map((a) => <AgentRow key={a.id} agent={a} now={now} selected={a.id === selectedId} onSelect={() => select(a.id)} onPause={() => pause(a)} pausing={setPaused.isPending || !mayPause.allowed} />)}
                </Group>
              )}
              {(show === null || show !== 'acting') && (
                <Group title={`Watching Agents (${watching.length})`} tone="watching" summary={groupHealth(watching)}>
                  {watching.map((a) => <AgentRow key={a.id} agent={a} now={now} selected={a.id === selectedId} onSelect={() => select(a.id)} onPause={() => pause(a)} pausing={setPaused.isPending || !mayPause.allowed} />)}
                </Group>
              )}
              <p className="text-xs text-[var(--color-text-muted)]">This list is read from the running server. An agent appears here the moment it is registered, and never before.</p>
            </div>
            {selected ? (
              <div className="space-y-2">
                <button type="button" onClick={() => edit({ agent: null })} className="text-sm font-semibold text-[var(--color-primary-700)] hover:underline">‹ All agents</button>
                <Detail agent={selected} onPause={() => pause(selected)} pausing={setPaused.isPending || !mayPause.allowed} notAllowed={mayPause.reason} />
              </div>
            ) : (
              <Summary agents={all} health={health} />
            )}
          </div>
        </>
      )}
    </section>
  )
}

/** "All healthy" or "2 need a look" for a group's header. */
function groupHealth(list: readonly Agent[]): { text: string; ok: boolean } {
  const bad = list.filter((a) => a.health !== 'healthy' && !a.isPaused).length
  return bad === 0 ? { text: list.length === 0 ? 'None' : 'All healthy', ok: true } : { text: `${bad} need a look`, ok: false }
}

/** The right-hand column when no agent is open: the registry count, a health ring, what agents may do, and what just happened. */
function Summary({ agents, health }: { agents: readonly Agent[]; health: { healthy: number; degraded: number; unhealthy: number; paused: number; unknown: number } }) {
  const total = agents.length
  const parts = [
    { key: 'Healthy', n: health.healthy, color: '#10b981' },
    { key: 'Degraded', n: health.degraded, color: '#f59e0b' },
    { key: 'Unhealthy', n: health.unhealthy, color: '#ef4444' },
    { key: 'Paused', n: health.paused, color: '#6b7280' },
    { key: 'Not run yet', n: health.unknown, color: '#cbd5e1' },
  ]
  const R = 52
  const C = 2 * Math.PI * R
  let offset = 0
  const acting = agents.filter((a) => a.canAct)
  const may = [...new Set(acting.flatMap((a) => a.may))]
  const mayNot = [...new Set(acting.flatMap((a) => a.mayNot))]
  const activity = useQueries({ queries: agents.map((a) => ({ queryKey: agentKeys.activity(a.id), queryFn: () => fetchAgentActivity(a.id), refetchInterval: 30_000 })) })
  const names = new Map(agents.map((a) => [a.id, a.name]))
  const recent = activity
    .flatMap((q) => (q.data?.items ?? []).filter((i) => i.kind !== 'idle').map((i) => ({ ...i, agentId: q.data!.agentId })))
    .sort((x, y) => y.at.localeCompare(x.at)).slice(0, 5)
  const now = new Date()
  const card = 'rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5'

  return (
    <aside aria-label="Agents at a glance" className="space-y-4">
      <div className={card}>
        <p className="text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Registered in this build</p>
        <p className="mt-1 text-lg font-semibold">{total} {total === 1 ? 'agent' : 'agents'}</p>
      </div>

      <section className={card} aria-label="Agent health">
        <h2 className="flex items-center text-base font-bold">Agent Health<InfoTip help={sectionHelp.agents.health} /></h2>
        <div className="mt-3 flex items-center gap-6">
          <svg viewBox="0 0 140 140" className="h-36 w-36 shrink-0 -rotate-90" role="img" aria-label={parts.filter((p) => p.n > 0).map((p) => `${p.n} ${p.key}`).join(', ')}>
            <circle cx="70" cy="70" r={R} fill="none" stroke="var(--color-surface-muted)" strokeWidth="16" />
            {parts.filter((p) => p.n > 0).map((p) => {
              const len = (p.n / total) * C
              const el = <circle key={p.key} cx="70" cy="70" r={R} fill="none" stroke={p.color} strokeWidth="16" strokeDasharray={`${len} ${C - len}`} strokeDashoffset={-offset} />
              offset += len
              return el
            })}
            <text x="70" y="70" transform="rotate(90 70 70)" textAnchor="middle" dominantBaseline="central" className="fill-current text-[28px] font-extrabold">{total}</text>
          </svg>
          <ul className="flex-1 space-y-1.5 text-sm">
            {parts.map((p) => <li key={p.key} className="flex items-center gap-2"><span aria-hidden="true" className="h-2.5 w-2.5 rounded-full" style={{ background: p.color }} />{p.key}<b className="tabular ml-auto">{p.n}</b></li>)}
          </ul>
        </div>
      </section>

      {(may.length > 0 || mayNot.length > 0) && (
        <section className={card} aria-label="What agents do">
          <h2 className="flex items-center text-base font-bold">What Agents Do<InfoTip help={sectionHelp.agents.contract} /></h2>
          <ul className="mt-2 space-y-1.5 text-sm">
            {may.map((m) => <li key={m} className="flex items-start gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]" aria-label="May" />{m}</li>)}
            {mayNot.map((m) => <li key={m} className="flex items-start gap-2"><X className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-label="Never" />{m}</li>)}
          </ul>
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">Taken from the acting agents’ own contracts.</p>
        </section>
      )}

      <section className={card} aria-label="Recent activity">
        <h2 className="flex items-center text-base font-bold">Recent Activity<InfoTip help={sectionHelp.agents.activity} /></h2>
        {recent.length === 0 ? (
          <p className="mt-2 text-sm text-[var(--color-text-muted)]">Nothing but idle cycles since the server started.</p>
        ) : (
          <ul className="mt-2 divide-y divide-[var(--color-border)]">
            {recent.map((i, n) => (
              <li key={`${i.agentId}-${i.at}-${n}`} className="flex items-start gap-3 py-2 text-sm">
                <span className="w-12 shrink-0 font-mono text-xs text-[var(--color-text-muted)]">{formatWhen(i.at, now)}</span>
                <span className="min-w-0 flex-1"><b>{names.get(i.agentId) ?? i.agentId}</b><span className="block text-xs text-[var(--color-text-muted)]">{i.text}</span></span>
                <span className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-semibold ${cycleTone[i.kind] ?? cycleTone.looked}`}>{i.kind}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </aside>
  )
}

function Group({ title, tone, summary, children }: { title: string; tone: 'acting' | 'watching'; summary: { text: string; ok: boolean }; children: React.ReactNode }) {
  const help = tone === 'acting' ? sectionHelp.agents.acting : sectionHelp.agents.watching
  return (
    <section aria-label={title} className="overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className={`flex items-center px-5 py-3 ${tone === 'acting' ? 'bg-[#fff7ed]' : 'bg-[#eff6ff]'}`}>
        <h2 className={`flex items-center text-[15px] font-bold ${tone === 'acting' ? 'text-[#9a3412]' : 'text-[#1e40af]'}`}>{title}<InfoTip help={help} /></h2>
        <span className={`ml-auto rounded-full px-3 py-0.5 text-xs font-bold ${summary.ok ? 'bg-[#d1fae5] text-[#047857]' : 'bg-[#fef3c7] text-[#92400e]'}`}>{summary.text}</span>
      </div>
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
            <button type="button" onClick={onPause} disabled={pausing} className="inline-flex items-center gap-1.5 rounded-lg bg-[#b45309] px-3.5 py-2 font-semibold text-white hover:bg-[#92400e] disabled:opacity-60">
              <Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pause
            </button>
          </div>
        )}
        {!agent.isPaused && <NotAllowed reason={notAllowed} />}

        {agent.notes && <p className="rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-[13px]">{agent.notes}</p>}

        {(agent.may.length > 0 || agent.mayNot.length > 0) && (
          <section>
            <h3 className="mb-1.5 flex items-center text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">What it may and may not do<InfoTip help={sectionHelp.agents.contract} /></h3>
            <ul className="divide-y divide-[var(--color-border)]">
              {agent.may.map((m) => <li key={m} className="flex items-start gap-2 py-1.5"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]" aria-label="May" />{m}</li>)}
              {agent.mayNot.map((m) => <li key={m} className="flex items-start gap-2 py-1.5"><X className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-label="Never" />{m}</li>)}
            </ul>
          </section>
        )}

        <section>
          <h3 className="mb-1.5 flex items-center text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
            Recent activity <span className="ml-1 font-normal normal-case tracking-normal">— idle cycles folded</span><InfoTip help={sectionHelp.agents.activity} />
          </h3>
          {activity.isPending && <Skeleton label="Reading its activity…" rows={3} />}
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
