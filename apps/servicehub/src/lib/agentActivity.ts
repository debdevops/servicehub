import type { AgentActivityItem } from './api/agents'

export const cycleTone: Readonly<Record<string, string>> = {
  idle: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]',
  looked: 'bg-[var(--color-primary-50)] text-[var(--color-primary-700)]',
  acted: 'bg-[var(--color-success-light)] text-[#047857]',
  degraded: 'bg-[var(--color-warning-light)] text-[#92400e]',
  failed: 'bg-[var(--color-error-light)] text-[#b91c1c]',
  paused: 'bg-[var(--color-warning-light)] text-[#92400e]',
  resumed: 'bg-[var(--color-success-light)] text-[#047857]',
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
