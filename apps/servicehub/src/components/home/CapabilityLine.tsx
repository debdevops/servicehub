import { capabilityWords } from '../../content/capabilities'
import type { CloudTraits } from '../../lib/home/traits'

/**
 * The ✓ / — row that says what a cloud can and can't do, read from its traits (never its name, R4).
 * `compact` drops to the four that fit a cloud card; the full line (a cloud's own header) adds
 * scheduled messages.
 */
export function CapabilityLine({ traits, full = false }: { traits: CloudTraits; full?: boolean }) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {capabilityWords(traits, full).map((w) => (
        <span
          key={w.text}
          className={`inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-[10.5px] font-semibold ${
            w.ok
              ? 'border-[#a7f3d0] bg-[#f0fdf4] text-[#047857]'
              : 'border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'
          }`}
        >
          {w.ok ? '✓' : '—'} {w.text}
        </span>
      ))}
    </div>
  )
}
