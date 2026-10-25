import { useQueryClient } from '@tanstack/react-query'
import { Bot, Check, OctagonAlert, Pause as PauseIcon } from 'lucide-react'
import { useParams, useSearchParams } from 'react-router-dom'
import { Detail, type AgentLinks } from '../../components/agent/AgentDetail'
import { DetailWindow } from '../components/DetailWindow'
import { healthTone, healthWord } from '../../components/agent/AgentRow'
import { Pager } from '../../components/ui/Pager'
import { useAgents, useDormantAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { useMe } from '../../hooks/useIdentity'
import { usePagedList } from '../../hooks/usePagedList'
import { usePendingWork } from '../../hooks/usePendingWork'
import { authorityWords, cadenceWords, type Agent } from '../../lib/api/agents'
import { formatAgo, formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import { Th } from '../components/Tip'
import { StatTile as Tile } from '../components/StatTile'
import { Tip } from '../components/Tip'

const td = 'px-3 py-2.5 align-top text-sm'

/** An agent that needs a look: its cycles fail or degrade, or none has finished for well over its cadence. Paused is a choice, not a fault. */
const needsLook = (a: Agent) => !a.isPaused && (a.health === 'degraded' || a.health === 'failing' || a.late)

/**
 * Agents (build-plan unit E8; design `p13-agents`): the background workers that watch, replay, verify and learn, and what each may do
 * on its own. Agents are the server's, not a cloud's, so the list is the same under every scope. Every row is drawn from `/agents`
 * (never per-agent code, so a newly registered agent appears by being registered); health comes from its cycles, never its own word.
 * **Pause** takes authority away (Operator); **Resume** gives it back and is an **Admin's** (owner decision 2026-10-19: Resume lives on this
 * page; the 4.2.0 Home's agent bar is the other place until the cutover). Both are one agent, on a click, and both are recorded with the person's name.
 */
export default function AgentsPage() {
  const scope = parseScope(useParams().cloud)
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const agents = useAgents()
  const dormant = useDormantAgents().data ?? []
  const setPaused = useSetAgentsPaused()
  const pending = usePendingWork({})
  const me = useMe().data
  const mayPause = permission(me, 'Operator', 'pause an agent')
  // Giving authority back is an Admin's, as on the 4.2.0 Home's agent bar (owner decision 2026-10-19: it lives on this page).
  const mayResume = permission(me, 'Admin', 'resume an agent')

  const all = agents.data ?? []
  const { items: shown, pager } = usePagedList(all)
  const selectedId = params.get('agent')
  const selected = all.find((a) => a.id === selectedId) ?? null
  const select = (id: string | null) => setParams((p) => { const n = new URLSearchParams(p); if (id === null) n.delete('agent'); else n.set('agent', id); return n }, { replace: true })
  const pause = (a: Agent) => setPaused.mutate({ ids: [a.id], paused: true })
  const resume = (a: Agent) => setPaused.mutate({ ids: [a.id], paused: false })
  const now = new Date()
  // Agents that stopped reporting are raised as pending work of their own kind; they are not approvals, so they are named here.
  const stopped = (pending.data?.items ?? []).filter((i) => i.kind === 'agent')

  const healthy = all.filter((a) => a.health === 'healthy' && !a.late).length
  const links: AgentLinks = {
    ledger: nextHref(nextEntry('ledger'), { cloud: scope }),
  }

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Agents" updatedAtMs={agents.dataUpdatedAt} onRefresh={() => void client.invalidateQueries({ queryKey: ['agents'] })}
        lookingAt="The background workers that watch, replay, verify and learn. The ones that act on messages come first." />

      {agents.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">ServiceHub couldn’t read its agents. Nothing is shown rather than a list that may be wrong.</p>
      ) : agents.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : all.length === 0 ? (
        <p className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-6 text-sm">No agents are registered in this build.</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile icon={<Bot className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title="Agents" note="Registered in this build">{all.length}</Tile>
            <Tile icon={<Check className="h-5 w-5 text-green-700" aria-hidden="true" />} title="Healthy" note="Their last cycles went well">{healthy}</Tile>
            <Tile icon={<OctagonAlert className="h-5 w-5 text-red-600" aria-hidden="true" />} title="Need a look" note="Failing, degraded or late">{all.filter(needsLook).length}</Tile>
            <Tile icon={<PauseIcon className="h-5 w-5 text-amber-700" aria-hidden="true" />} title="Paused" note="Will not act until resumed">{all.filter((a) => a.isPaused).length}</Tile>
          </div>

          {stopped.map((i) => <p key={i.id} role="status" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950"><b>{all.find((a) => a.id === i.agentId)?.name ?? i.agentId ?? 'An agent'}:</b> {i.reason}</p>)}
          {setPaused.isError && <p role="alert" className="text-sm text-red-700">That change didn’t go through. Nothing changed — try again.</p>}

          <div className="grid gap-4">
            <section aria-labelledby="agents-title" className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
              <h2 id="agents-title" className="px-4 pt-4 text-base font-semibold">Agents<Tip label="section:Agents table" /></h2>
              <div className="overflow-x-auto">
                <table className="w-full">
                  <caption className="sr-only">Agents registered in this build</caption>
                  <thead><tr><Th context="agents">Agent</Th><Th context="agents">What it does</Th><Th context="agents">State</Th><Th context="agents">Last cycle</Th><Th context="agents">Acts on messages?</Th><Th context="agents">Actions</Th></tr></thead>
                  <tbody>
                    {shown.map((a) => (
                      <tr key={a.id} aria-selected={a.id === selectedId} className={`border-t border-[var(--color-border)] ${a.id === selectedId ? 'bg-[var(--color-primary-50)]' : ''}`}>
                        <td className={td}><span className="font-semibold">{a.name}</span><span className="block font-mono text-xs text-[var(--color-text-muted)]">{a.id}</span></td>
                        <td className={`${td} max-w-sm`}>{a.purpose}<span className="block text-xs text-[var(--color-text-muted)]">{cadenceWords(a.cadenceSeconds)}</span></td>
                        <td className={td}><span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold ${healthTone[a.health]}`}>{healthWord[a.health]}</span>{a.late && !a.isPaused && <span className="ml-1 text-xs font-semibold text-amber-800">Late</span>}</td>
                        <td className={td}>{a.lastRunUtc ? <>{formatWhen(a.lastRunUtc, now)}<span className="block text-xs text-[var(--color-text-muted)]">{a.lastFailure ? `Failed: ${a.lastFailure}` : a.lastResult?.summary ?? formatAgo(a.lastRunUtc, now)}</span></> : 'Not run yet'}</td>
                        <td className={td}>{a.canAct ? authorityWords(a.authority) : <span className="text-[var(--color-text-muted)]">{authorityWords(a.authority)}</span>}</td>
                        <td className={td}>
                          <span className="flex flex-wrap items-center gap-2">
                            <button type="button" onClick={() => select(a.id === selectedId ? null : a.id)} aria-pressed={a.id === selectedId} aria-label={`View ${a.name}`}
                              className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">View</button>
                            {a.isPaused
                              ? <button type="button" onClick={() => resume(a)} disabled={setPaused.isPending || !mayResume.allowed} title={mayResume.reason ?? undefined} aria-label={`Resume ${a.name}`}
                  className="rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] disabled:opacity-50">Resume</button>
                              : <button type="button" onClick={() => pause(a)} disabled={setPaused.isPending || !mayPause.allowed} title={mayPause.reason ?? undefined} aria-label={`Pause ${a.name}`}
                                  className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-amber-900 hover:bg-amber-50 disabled:opacity-50">Pause</button>}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pager {...pager} label="agents" />
              <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">Pause stops one agent. Emergency stop, in the header, stops every replay ServiceHub would do on its own. Neither deletes anything.</p>
              {dormant.length > 0 && (
                <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">
                  {dormant.length === 1 ? '1 agent is' : `${dormant.length} agents are`} off because none of your connected clouds needs {dormant.length === 1 ? 'it' : 'them'}: {dormant.map((d) => d.name).join(', ')}. {dormant.length === 1 ? 'It starts' : 'They start'} by themselves when a cloud that does is connected.
                </p>
              )}
            </section>
            {selected && (
              <DetailWindow title={selected.name} description="Agent" onClose={() => select(null)}>
                <Detail windowed agent={selected} onPause={() => pause(selected)} pausing={setPaused.isPending || !mayPause.allowed} notAllowed={mayPause.reason} links={links}
                  resume={{ onResume: () => resume(selected), busy: setPaused.isPending, notAllowed: mayResume.allowed ? null : mayResume.reason }} />
              </DetailWindow>
            )}
          </div>
        </>
      )}
    </div>
  )
}

