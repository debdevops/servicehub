import { OctagonAlert, PauseCircle } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { useAgents, useSetAgentsPaused } from '../../hooks/useAgents'
import { useMe } from '../../hooks/useIdentity'
import { useEmergencyStop } from '../../hooks/useSettings'
import { formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'

/**
 * The banners above everything (unit 6.10) — rendered ONLY while true, from server state, never local state.
 *
 * - Emergency stop (red): ServiceHub will not act on its own. Worded to what the gate really does: rules and agents are
 *   refused; a replay a person starts still goes through its own checks. (The design's "replays are all refused" would be
 *   untrue — the copied gate stops automatic actions only.) "Review and lift…" opens Settings, where an Admin lifts it.
 * - Paused (amber): the acting agents will not act; they still watch and record. Resume only on Simple — Advanced may take
 *   authority away, never give it back (ADR-0016 D3).
 */
export function SafetyBanners() {
  const stop = useEmergencyStop()
  const agents = useAgents()
  const me = useMe().data
  const resume = useSetAgentsPaused()
  const { pathname, search } = useLocation()
  const onAdvanced = pathname.startsWith('/advanced')
  const settingsHref = (() => { const q = new URLSearchParams(search); q.set('modal', 'settings'); return `${pathname}?${q}#settings-access` })()

  const acting = (agents.data ?? []).filter((a) => a.canAct)
  const pausedActing = acting.filter((a) => a.isPaused)
  const mayResume = permission(me, 'Admin', 'resume the Agent')
  const now = new Date()

  return (
    <>
      {stop.data?.active && (
        <div role="alert" className="flex flex-wrap items-center gap-3 bg-[#b91c1c] px-5 py-2.5 text-sm text-white">
          <OctagonAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
          <p className="min-w-0 flex-1">
            <b>Emergency stop is on — ServiceHub will not act on its own</b>
            {stop.data.at ? ` · since ${formatWhen(stop.data.at, now)}` : ''}{stop.data.by ? ` by ${stop.data.by}` : ''}{stop.data.reason ? ` — “${stop.data.reason}”` : ''}.
            {' '}Rules and the Agent are refused; a replay a person starts still goes through its checks. Numbers keep updating.
          </p>
          <Link to={settingsHref} className="rounded-lg bg-white/15 px-3 py-1.5 font-semibold hover:bg-white/25">Review and lift…</Link>
        </div>
      )}
      {stop.isError && !stop.data && (
        <div role="status" className="flex items-center gap-3 bg-[#fef3c7] px-5 py-2 text-sm text-[#78350f]">
          <OctagonAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
          <p className="min-w-0 flex-1">ServiceHub couldn’t check whether emergency stop is on. It will keep trying — until then, don’t assume it is off.</p>
        </div>
      )}
      {pausedActing.length > 0 && pausedActing.length === acting.length && (
        <div role="status" className="flex flex-wrap items-center gap-3 bg-[#fef3c7] px-5 py-2.5 text-sm text-[#78350f]">
          <PauseCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
          <p className="min-w-0 flex-1">
            <b>The Agent will not act — paused.</b> It is still watching and recording; nothing it did is undone.
          </p>
          {onAdvanced ? (
            <Link to="/" className="font-semibold underline">Resume on Home</Link>
          ) : (
            <button
              type="button"
              disabled={!mayResume.allowed || resume.isPending}
              title={mayResume.reason ?? undefined}
              onClick={() => resume.mutate({ ids: pausedActing.map((a) => a.id), paused: false })}
              className="rounded-lg bg-[#92400e] px-3 py-1.5 font-semibold text-white disabled:opacity-50"
            >
              Resume
            </button>
          )}
        </div>
      )}
    </>
  )
}
