import { PauseCircle } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useAgents } from '../../hooks/useAgents'
import { agentState } from '../lib/words'

/**
 * Above every page while every acting agent is paused (4.2.0 SafetyBanners, moved). Shown only from server state, never local state, and
 * only when ALL acting agents are paused: a partly paused Agent is named in the header instead, because something is still acting.
 * Resume lives on the Agents page, for Admins (owner decision 2026-10-19), so this links there and holds no control of its own.
 */
export function PausedBand({ agentsHref }: { agentsHref: string }) {
  const agents = useAgents()
  if (agentState(agents.data).status !== 'paused') return null
  return (
    <div role="status" className="flex flex-wrap items-center gap-3 border-b border-amber-300 bg-amber-50 px-5 py-2.5 text-sm text-amber-900">
      <PauseCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
      <p className="min-w-0 flex-1">
        <strong>The Agent will not act: it is paused.</strong> It is still watching and recording; nothing it did is undone.
      </p>
      <Link to={agentsHref} className="font-semibold underline">Resume on Agents</Link>
    </div>
  )
}
