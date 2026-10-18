import type { CoverageRow } from '../../../lib/api/coverage'
import { evidenceSentence } from '../../lib/words'

const dot = { good: 'bg-green-600', caution: 'bg-amber-500', none: 'bg-slate-300' } as const

/**
 * "Evidence and replay outcome": one sentence per component this cloud covers, chosen from what the catalogue says about it
 * (its class and note) — never from the cloud's name, so a new component gets its sentence without a new line here.
 */
export function EvidencePanel({ cloudName, rows, heading }: { cloudName: string; rows: readonly CoverageRow[]; heading?: string }) {
  return (
    <section aria-labelledby="evidence-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <h2 id="evidence-title" className="mb-3 text-base font-semibold">{heading ?? `Evidence and replay outcome (${cloudName})`}</h2>
      {rows.length === 0 ? (
        <p className="text-sm text-[var(--color-text-muted)]">Nothing is covered here yet.</p>
      ) : (
        <dl className="space-y-3">
          {rows.map((row) => {
            const said = evidenceSentence(row)
            return (
              <div key={row.componentKey} className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5">
                <dt className="flex items-center gap-2 text-sm font-semibold"><span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${dot[said.tone]}`} />{row.name}</dt>
                <dd className="text-sm text-[var(--color-text-muted)]">{said.text}</dd>
              </div>
            )
          })}
        </dl>
      )}
    </section>
  )
}
