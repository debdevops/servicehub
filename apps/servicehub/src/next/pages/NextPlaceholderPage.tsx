import { nextProgress, type ComponentTab, type NextEntry } from '../nav/navigation'

/**
 * What a 5.0.0 screen looks like before its unit builds it — the same idea as 4.1.0's `PlaceholderPage`.
 *
 * It lets the shell, the routes and the navigation array be real and tested before any screen has data. It says plainly that
 * it is not built, which build-plan unit builds it and which accepted design it must match — never a fake chart, never a zero
 * that looks like a measurement (rule R5).
 */
export function NextPlaceholderPage({ entry, tab }: { entry: NextEntry; tab?: ComponentTab }) {
  const { built, total } = nextProgress()
  const unit = tab?.unit ?? entry.unit
  const design = tab?.design ?? entry.design
  return (
    <section className="mx-auto max-w-2xl px-6 py-16 text-center">
      <entry.icon className="mx-auto mb-4 h-10 w-10 text-[var(--color-text-muted)]" />
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">{tab ? `${entry.label} · ${tab.label}` : entry.label}</h1>
      <p className="mt-2 text-[var(--color-text-muted)]">{entry.description}</p>
      <p className="mt-8 inline-block rounded-full bg-[var(--color-surface-muted)] px-4 py-1.5 text-sm text-[var(--color-text-muted)]">
        Not built yet — unit {unit}
      </p>
      <p className="mt-3 text-xs text-[var(--color-text-muted)]">
        Design: <span className="font-mono">{design}</span> · {built} of {total} screens built
      </p>
    </section>
  )
}
