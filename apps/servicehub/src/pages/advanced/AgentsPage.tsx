import { useQueries } from '@tanstack/react-query'
import { Bot, Check, ChevronRight, Eye, Heart, Lightbulb, Pause, TriangleAlert, X } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { Link, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { AgentRow } from '../../components/agent/AgentRow'
import { Detail } from '../../components/agent/AgentDetail'
import { cycleTone, fold } from '../../lib/agentActivity'
import { agentKeys, useAgents, useDormantAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { fetchAgentActivity, type Agent } from '../../lib/api/agents'
import { formatWhen } from '../../lib/format'
import { Skeleton } from '../../components/ui/Skeleton'
import { sectionHelp } from '../../content/sections'
import { InfoTip } from '../../components/ui/InfoTip'
import PageHelpLink from '../../components/help/PageHelpLink'

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
  const dormant = useDormantAgents().data ?? []
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
            <Bot className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Agents <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} /> <PageHelpLink page="advanced" />
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

          <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-[minmax(0,1fr)_400px]">
            <div className="min-w-0 space-y-4">
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
              {dormant.length > 0 && (
                <p className="text-xs text-[var(--color-text-muted)]">
                  {dormant.length === 1 ? '1 agent is' : `${dormant.length} agents are`} off because none of your connected clouds needs {dormant.length === 1 ? 'it' : 'them'}: {dormant.map((d) => d.name).join(', ')}. {dormant.length === 1 ? 'It starts' : 'They start'} by themselves when a cloud that does is connected.
                </p>
              )}
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

/** Re-exported: the Agents page's tests (and its activity folding) have always imported it from here. */
export { fold }
