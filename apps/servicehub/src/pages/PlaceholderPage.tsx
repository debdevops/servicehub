import type { NavEntry } from '../nav/navigation'

/**
 * What a screen looks like before its wave builds it.
 *
 * It exists so the shell, the routing and the navigation array can be real and tested in Wave 0
 * without any screen pretending to have data. It states plainly that it is not built yet and which
 * wave builds it — <b>never a fake chart, never a zero that looks like a measurement</b> (rule R5).
 */
export function PlaceholderPage({ entry }: { entry: NavEntry }) {
  return (
    <section className="mx-auto max-w-2xl px-6 py-16 text-center">
      <entry.icon className="mx-auto mb-4 h-10 w-10 text-[var(--color-text-muted)]" />
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">{entry.label}</h1>
      <p className="mt-2 text-[var(--color-text-muted)]">{entry.description}</p>
      <p className="mt-8 inline-block rounded-full bg-[var(--color-surface-muted)] px-4 py-1.5 text-sm text-[var(--color-text-muted)]">
        Not built yet — Wave {entry.wave}
      </p>
    </section>
  )
}
