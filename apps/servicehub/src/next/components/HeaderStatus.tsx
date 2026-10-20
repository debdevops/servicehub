import { CircleHelp, OctagonAlert, Search } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { useAgents } from '../../hooks/useAgents'
import { useMe } from '../../hooks/useIdentity'
import { nextEntry, nextHref } from '../nav/navigation'
import { useStopState } from '../lib/useStopState'
import { Tip } from './Tip'
import { agentState, roleWords } from '../lib/words'

const dot = { running: 'bg-green-400', paused: 'bg-amber-400', 'partly-paused': 'bg-amber-400', unknown: 'bg-slate-400' } as const

/**
 * The right-hand end of the header (build-plan unit B3): the Agent's state, the Emergency stop button, and the caller's role.
 * Each is read from the same hooks the 4.2.0 pages use — the role from `/me` (the recovery role, which is what Review and Replay
 * need), the Agent from the running agents, the stop from `/settings/emergency-stop`.
 */
export function HeaderStatus() {
  const { pathname } = useLocation()
  const me = useMe()
  const agents = useAgents()
  const stop = useStopState()
  const agent = agentState(agents.isError ? undefined : agents.data)
  const role = me.isError ? 'unknown' : roleWords(me.data)
  const on = stop.kind === 'on' || stop.kind === 'unreadable'

  return (
    <div className="ml-auto flex items-center gap-3">
      <div className="flex items-center gap-2" role="status" aria-label="Agent" title={agent.detail}>
        <span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${dot[agent.status]}`} />
        <span className="leading-tight">
          <span className="block text-xs font-semibold">{agent.title}</span>
          <span className="block text-[11px] text-white/75">{agent.detail}</span>
        </span>
        <Tip label="header:Agent" className="text-white/80 hover:text-white" />
      </div>

      <Link
        to={nextHref(nextEntry('emergency-stop'), {}, pathname)}
        className={`inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-bold ${on ? 'border-white bg-red-700 text-white' : 'border-red-300/60 bg-red-800 text-white hover:bg-red-700'}`}
      >
        <OctagonAlert className="h-3.5 w-3.5" aria-hidden="true" />
        {on ? 'Emergency stop is ON' : 'Emergency stop'}
      </Link>
      <Tip label="header:Emergency stop" className="text-white/80 hover:text-white" />

      <Link to={nextHref(nextEntry('search'), {}, pathname)} aria-label="Search" className="inline-flex items-center gap-1.5 rounded-md bg-white/15 px-2.5 py-1.5 text-xs font-semibold hover:bg-white/25">
        <Search className="h-3.5 w-3.5" aria-hidden="true" /> Search <kbd className="text-[10px]">⌘K</kbd>
      </Link>
      <Tip label="header:Search" className="text-white/80 hover:text-white" />

      <Link to={nextHref(nextEntry('help'), {}, pathname)} className="inline-flex items-center gap-1.5 rounded-md bg-white/15 px-2.5 py-1.5 text-xs font-semibold hover:bg-white/25">
        <CircleHelp className="h-3.5 w-3.5" aria-hidden="true" /> Help
      </Link>
      <Tip label="header:Help" className="text-white/80 hover:text-white" />

      <span className="rounded-md bg-white/15 px-2.5 py-1.5 text-xs font-semibold">Role: {role ?? '…'}</span>
      <Tip label="header:Role" className="text-white/80 hover:text-white" />
    </div>
  )
}
