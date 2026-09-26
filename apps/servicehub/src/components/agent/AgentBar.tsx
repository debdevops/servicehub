import { Bot, Pause, Play } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'
import { Link } from 'react-router-dom'
import { useAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import { usePendingWork } from '../../hooks/usePendingWork'
import { agentLine } from '../../lib/agentLine'
import type { CloudProvider, EnvironmentKind, Namespace } from '../../lib/api/namespaces'

const count = (states: readonly { state: string; count: number }[] | undefined, name: string) => states?.find((s) => s.state === name)?.count ?? 0

/**
 * The Agent, where the person already is (unit 4.6): one line about THIS cloud, three numbers, one control.
 *
 * - The line is computed from the namespaces' capabilities, never a cloud's name.
 * - The numbers read the same 24-hour recovery summary the Replayed tab and the Ledger read (2.12) — nothing recomputed —
 *   and "need you" is the same pending-work query as the bell and the Needs-you strip above it (5.1).
 * - Pause pauses the acting agents only, so the words are true: it <b>will not act</b>, and it keeps watching.
 *   Resume is here too — Simple may give authority back; Advanced only takes it away (ADR-0016 D3). It resumes every
 *   paused agent, including a watching one paused on the Agents page, so nothing can be paused with no way back.
 */
export function AgentBar({ cloud, provider, namespaces, queues, namespaceId, environment }: {
  cloud: string; provider: CloudProvider; namespaces: readonly Namespace[]; queues: number | null; namespaceId?: string; environment?: EnvironmentKind
}) {
  const agents = useAgents()
  const summary = useRecoverySummary({ window: '24h', provider, namespaceId, environment })
  const setPaused = useSetAgentsPaused()
  const pending = usePendingWork({ provider, namespaceId, environment })
  const me = useMe().data

  if (!agents.data) return null
  const line = agentLine(cloud, namespaces, queues, agents.data)
  const pausedIds = agents.data.filter((a) => a.isPaused).map((a) => a.id)
  const actingIds = agents.data.filter((a) => a.canAct && !a.isPaused).map((a) => a.id)
  const rate = summary.data?.stayedFixedRate ?? null
  const mayPause = permission(me, 'Operator', 'pause the Agent')
  const mayResume = permission(me, 'Admin', 'resume the Agent')
  const chip = line.status === 'will-not-act'
    ? { text: 'WILL NOT ACT', cls: 'bg-[rgba(245,158,11,.22)] text-[#fcd34d] border-[rgba(252,211,77,.4)]' }
    : { text: 'WATCHING', cls: 'bg-[rgba(16,185,129,.22)] text-[#6ee7b7] border-[rgba(110,231,183,.35)]' }

  return (
    <section
      aria-label="ServiceHub Agent"
      className="relative flex flex-wrap items-center gap-4 overflow-hidden rounded-xl px-[18px] py-[13px] text-white shadow-[0_4px_14px_rgba(3,105,161,.28)]"
      style={{ background: 'linear-gradient(105deg,#0c4a6e 0%,#075985 42%,#0369a1 100%)' }}
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[11px] border border-white/25 bg-white/15">
        <Bot className="h-5 w-5" aria-hidden="true" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="flex items-center gap-2 text-[13.5px] font-extrabold">
          ServiceHub Agent
          <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[9.5px] font-extrabold tracking-[.4px] ${chip.cls}`}>{chip.text}</span>
        </p>
        <p className="mt-0.5 text-xs text-[#bae6fd]">{line.text}</p>
      </div>
      <dl className="z-[1] flex gap-[26px]" aria-label="Agent in the last 24 hours">
        <Stat value={summary.data ? count(summary.data.states, 'Recovered').toLocaleString() : '—'} label="verified, 24 h" />
        <Stat value={summary.data ? count(summary.data.states, 'Observing').toLocaleString() : '—'} label="being watched now" />
        <Stat value={pending.data ? pending.data.total.toLocaleString() : '—'} label="need you — above" warn={(pending.data?.total ?? 0) > 0} />
        <Stat value={rate === null ? '—' : `${Math.round(rate * 100)}%`} label={rate === null ? 'nothing checked yet' : 'stayed fixed'} />
      </dl>
      <Link to="/advanced/agents" className="z-[1] whitespace-nowrap text-[11.5px] font-bold text-[#bae6fd] hover:underline">All agents →</Link>
      {pausedIds.length > 0 ? (
        <button
          type="button"
          disabled={setPaused.isPending || !mayResume.allowed}
          title={mayResume.reason ?? undefined}
          onClick={() => setPaused.mutate({ ids: pausedIds, paused: false })}
          className="z-[1] inline-flex items-center gap-1.5 rounded-[9px] border border-white/30 bg-white/15 px-3.5 py-2 text-xs font-bold hover:bg-white/25 disabled:opacity-60"
        >
          <Play className="h-3.5 w-3.5" aria-hidden="true" /> Resume{pausedIds.length > 1 ? ` all ${pausedIds.length}` : ''}
        </button>
      ) : (
        actingIds.length > 0 && (
          <button
            type="button"
            disabled={setPaused.isPending || !mayPause.allowed}
            onClick={() => setPaused.mutate({ ids: actingIds, paused: true })}
            title={mayPause.reason ?? 'The Agent will not act until you resume it. It keeps watching, and nothing it already did is undone.'}
            className="z-[1] inline-flex items-center gap-1.5 rounded-[9px] border border-white/30 bg-white/15 px-3.5 py-2 text-xs font-bold hover:bg-white/25 disabled:opacity-60"
          >
            <Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pause
          </button>
        )
      )}
      {setPaused.isError && <p role="alert" className="w-full text-xs text-[#fcd34d]">That didn’t go through. Nothing changed — try again.</p>}
      {pausedIds.length > 0 ? mayResume.reason && <div className="w-full"><NotAllowed reason={mayResume.reason} tone="dark" /></div> : mayPause.reason && <div className="w-full"><NotAllowed reason={mayPause.reason} tone="dark" /></div>}
    </section>
  )
}

function Stat({ value, label, warn = false }: { value: string; label: string; warn?: boolean }) {
  return (
    <div>
      <dd className={`tabular text-[19px] font-extrabold leading-tight ${warn ? 'text-[#fcd34d]' : ''}`}>{value}</dd>
      <dt className="text-[10px] font-semibold text-[#bae6fd]">{label}</dt>
    </div>
  )
}
