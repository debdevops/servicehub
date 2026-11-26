import type { ReactNode } from 'react'
import { Tip } from './Tip'

/**
 * One number with its meaning: an icon, the title with its (i), the value, and a line saying what it covers. The (i) says what the number
 * is and where it comes from. `tip` names the dictionary entry when the title is built (for example “Active on Azure”).
 */
export function StatTile({ icon, title, note, value, tip, context, children }: {
  icon: ReactNode; title: string; note: string; value?: ReactNode; tip?: string; context?: string; children?: ReactNode
}) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className="rounded-full bg-[var(--color-surface-muted)] p-2.5">{icon}</span>
      <div>
        <p className="flex items-center text-sm font-semibold">{title}<Tip label={tip ?? title} context={context} /></p>
        <p className="text-2xl font-bold">{value ?? children}</p>
        <p className="text-xs text-[var(--color-text-muted)]">{note}</p>
      </div>
    </div>
  )
}
