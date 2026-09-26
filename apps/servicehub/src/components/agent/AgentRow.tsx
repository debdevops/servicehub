import { Bot, Clock, Pause } from 'lucide-react'
import { authorityWords, cadenceWords, type Agent } from '../../lib/api/agents'
import { formatAge } from '../../lib/format'

const kindWord = { watch: 'Watch', decide: 'Decide', act: 'Act', maintain: 'Maintain' } as const

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
  const when = `${formatAge(a.lastRunUtc, now)} ago`
  if (a.lastFailure) return `last cycle failed: ${a.lastFailure} · ${when}`
  return `last: ${a.lastResult?.summary ?? 'ran'} · ${when}${a.late ? ' — late' : ''}`
}

/**
 * One agent, drawn entirely from its descriptor and runtime state. There is no per-agent UI: a new agent appears here
 * with its words, health and pause by being registered (unit 4.5).
 */
export function AgentRow({ agent, now, selected, onSelect, onPause, pausing }: {
  agent: Agent; now: Date; selected: boolean; onSelect: () => void; onPause: () => void; pausing: boolean
}) {
  return (
    <li className={`flex items-start gap-3 border-t border-[var(--color-border)] px-5 py-4 first:border-t-0 ${selected ? 'border-l-4 border-l-[var(--color-primary-600)] bg-[var(--color-primary-50)]' : ''}`}>
      <span className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${agent.canAct ? 'bg-[var(--color-warning-light)] text-[#92400e]' : 'bg-[var(--color-primary-50)] text-[var(--color-primary-700)]'}`}>
        <Bot className="h-4 w-4" aria-hidden="true" />
      </span>
      <div className="min-w-0 flex-1">
        <button type="button" onClick={onSelect} className="flex flex-wrap items-center gap-2 text-left" aria-pressed={selected}>
          <span className="text-[15px] font-bold text-[var(--color-text)] hover:underline">{agent.name}</span>
          <span className="rounded-md bg-[var(--color-surface-muted)] px-2 py-0.5 text-[11px] font-semibold">{kindWord[agent.kind]}</span>
          <span className={`rounded-md px-2 py-0.5 text-[11px] font-semibold ${agent.canAct ? 'bg-[var(--color-warning-light)] text-[#92400e]' : 'bg-[var(--color-primary-50)] text-[var(--color-primary-700)]'}`}>
            {authorityWords(agent.authority)}
          </span>
        </button>
        <p className="mt-1 text-sm text-[var(--color-text)]">{agent.purpose}</p>
        <p className="mt-1 flex flex-wrap items-center gap-x-3 text-xs text-[var(--color-text-muted)]">
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
    </li>
  )
}
