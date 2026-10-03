import { Bot, ChevronRight, Clock, Eye, Pause, Play, Settings2 } from 'lucide-react'
import { cadenceWords, type Agent } from '../../lib/api/agents'
import { formatAgo } from '../../lib/format'

export const healthTone: Readonly<Record<Agent['health'], string>> = {
  healthy: 'bg-[var(--color-success-light)] text-[#047857]',
  degraded: 'bg-[var(--color-warning-light)] text-[#92400e]',
  failing: 'bg-[var(--color-error-light)] text-[#b91c1c]',
  paused: 'bg-[var(--color-warning-light)] text-[#92400e]',
  unknown: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]',
}

export const healthWord: Readonly<Record<Agent['health'], string>> = {
  healthy: 'Healthy', degraded: 'Degraded', failing: 'Failing', paused: 'Paused — will not act', unknown: 'Not run yet',
}

/** "last: looked at 18 queues … · 20 s ago" — what its last cycle said, in its own words. */
export function lastLine(a: Agent, now: Date): string {
  if (a.isPaused) return 'paused — its cycles are skipped until someone resumes it on Home'
  if (!a.lastRunUtc) return 'has not finished a cycle since the server started'
  const when = formatAgo(a.lastRunUtc, now)
  if (a.lastFailure) return `last cycle failed: ${a.lastFailure} · ${when}`
  return `last: ${a.lastResult?.summary ?? 'ran'} · ${when}${a.late ? ' — late' : ''}`
}

/** The two small badges under an agent's name: what kind of job it does, and how far it is trusted — in the words a person uses. */
function badges(a: Agent): { label: string; cls: string }[] {
  const blue = 'bg-[#e0f2fe] text-[#0369a1]'
  const purple = 'bg-[#f3e8ff] text-[#7e22ce]'
  const amber = 'bg-[#ffedd5] text-[#c2410c]'
  const grey = 'bg-[var(--color-surface-muted)] text-[var(--color-text)]'
  if (a.canAct) {
    return [{ label: 'Acting', cls: amber }, a.authority === 'actsAutonomously' ? { label: 'Autonomous', cls: purple } : { label: 'User triggered', cls: blue }]
  }
  return [
    { label: a.kind === 'maintain' ? 'Maintenance' : 'Watching', cls: grey },
    a.authority === 'proposes' ? { label: 'Decides, never acts', cls: blue } : { label: 'Observes only', cls: blue },
  ]
}

/**
 * One agent, drawn entirely from its descriptor and runtime state. There is no per-agent UI: a new agent appears here
 * with its words, health and pause by being registered (unit 4.5). The chevron opens its detail.
 */
export function AgentRow({ agent, now, selected, onSelect, onPause, pausing }: {
  agent: Agent; now: Date; selected: boolean; onSelect: () => void; onPause: () => void; pausing: boolean
}) {
  const Icon = agent.canAct ? (agent.authority === 'actsAutonomously' ? Settings2 : Play) : agent.kind === 'decide' ? Bot : Eye
  const tile = agent.canAct ? (agent.authority === 'actsAutonomously' ? 'bg-[#f3e8ff] text-[#7e22ce]' : 'bg-[#ffedd5] text-[#c2410c]') : 'bg-[var(--color-primary-50)] text-[var(--color-primary-700)]'
  return (
    <li className={`flex items-start gap-4 border-t border-[var(--color-border)] px-5 py-4 first:border-t-0 ${selected ? 'border-l-4 border-l-[var(--color-primary-600)] bg-[var(--color-primary-50)]' : ''}`}>
      <span className={`mt-0.5 flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${tile}`}>
        <Icon className="h-5 w-5" aria-hidden="true" />
      </span>
      <div className="min-w-0 flex-1">
        <button type="button" onClick={onSelect} className="flex flex-wrap items-center gap-2 text-left" aria-pressed={selected}>
          <span className="text-[15px] font-bold text-[var(--color-text)] hover:underline">{agent.name}</span>
          {badges(agent).map((b) => <span key={b.label} className={`rounded-md px-2 py-0.5 text-[11px] font-semibold ${b.cls}`}>{b.label}</span>)}
        </button>
        <p className="mt-1 text-sm text-[var(--color-text)]">{agent.purpose}</p>
        <p className="mt-1.5 flex flex-wrap items-center gap-x-4 text-xs text-[var(--color-text-muted)]">
          <span className="inline-flex items-center gap-1"><Clock className="h-3 w-3" aria-hidden="true" />{cadenceWords(agent.cadenceSeconds)}</span>
          <span>{lastLine(agent, now)}</span>
        </p>
      </div>
      <span className={`shrink-0 rounded-full px-2.5 py-0.5 text-[11px] font-bold ${healthTone[agent.health]}`}>{healthWord[agent.health]}</span>
      {!agent.isPaused && (
        <button
          type="button"
          onClick={onPause}
          disabled={pausing}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-60"
          aria-label={`Pause ${agent.name}`}
        >
          <Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pause
        </button>
      )}
      <button type="button" onClick={onSelect} aria-label={`Open ${agent.name}`} className="shrink-0 self-center rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
        <ChevronRight className="h-5 w-5" aria-hidden="true" />
      </button>
    </li>
  )
}
