import type { ReactNode } from 'react'

/** The shell every Home insight shares, so the widgets read as one family on every cloud. */
export function InsightCard({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4 shadow-[var(--shadow-card)]">
      <header className="mb-3 flex items-baseline justify-between gap-2">
        <h2 className="text-[13.5px] font-bold text-[var(--color-text)]">{title}</h2>
        {note && <span className="text-[11px] text-[var(--color-text-muted)]">{note}</span>}
      </header>
      {children}
    </section>
  )
}
