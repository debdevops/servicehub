import { Check, Pause, X } from 'lucide-react'
import { Link } from 'react-router-dom'
import { sectionHelp } from '../../content/sections'
import { useAgentActivity } from '../../hooks/useAgents'
import { authorityWords, cadenceWords, type Agent } from '../../lib/api/agents'
import { cycleTone, fold } from '../../lib/agentActivity'
import { formatWhen } from '../../lib/format'
import { InfoTip } from '../ui/InfoTip'
import { NotAllowed } from '../ui/NotAllowed'
import { Skeleton } from '../ui/Skeleton'
import { healthTone, healthWord } from './AgentRow'

const kindWord = { watch: 'Watch', decide: 'Decide', act: 'Act', maintain: 'Maintain' } as const

/**
 * Where an agent's detail sends you. The 4.2.0 Advanced page passes nothing and keeps its own addresses (Home, the 4.2.0 ledger); the
 * 5.0.0 shell passes its own. `resume` is where a paused agent is given its authority back: pausing only takes authority away, and
 * this panel never gives it back (ADR-0016 D3), so it names a place instead of offering a button.
 */
export interface AgentLinks {
  readonly resume: { readonly to: string; readonly label: string; readonly where: string }
  readonly ledger: string
}

export function Detail({ agent, onPause, pausing, notAllowed, links }: { agent: Agent; onPause: () => void; pausing: boolean; notAllowed: string | null; links?: AgentLinks }) {
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
            <p className="mt-1 text-[#92400e]">Its cycles are skipped, so it will not act. Nothing it already did is undone. Resume it from {links ? links.resume.where : 'Home’s agent bar'}.</p>
            <Link to={links ? links.resume.to : '/'} className="mt-2 inline-block font-semibold text-[var(--color-primary-700)] hover:underline">{links ? links.resume.label : 'Go to Home to resume ›'}</Link>
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
        {agent.canAct && <Link to={links ? links.ledger : '/advanced/ledger'} className="inline-block font-semibold text-[var(--color-primary-700)] hover:underline">Everything it did, in the Ledger ›</Link>}
      </div>
    </aside>
  )
}
