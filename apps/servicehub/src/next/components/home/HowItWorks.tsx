import type { ConnectedComponent } from '../../../lib/api/components'
import type { CoverageRow } from '../../../lib/api/coverage'
import { cloudFacts } from '../../lib/words'
import { Tip } from '../Tip'

const dot = { good: 'bg-green-600', caution: 'bg-amber-500', none: 'bg-slate-300' } as const

/** "How ServiceHub works with <cloud>": three plain facts, from this cloud's components and what the catalogue says about them — never from its name. */
export function HowItWorks({ cloudName, components, covered }: { cloudName: string; components: readonly ConnectedComponent[]; covered: readonly Pick<CoverageRow, 'class' | 'status'>[] }) {
  const facts = cloudFacts(components, covered)
  const items = [
    { text: facts.watched.text, dot: facts.watched.good ? dot.good : dot.none },
    { text: facts.counts.text, dot: facts.counts.good ? dot.good : dot.none },
    { text: facts.evidence.text, dot: dot[facts.evidence.tone] },
  ]
  return (
    <div className="rounded-lg bg-[var(--color-surface-muted)] p-3">
      <h3 className="flex items-center text-sm font-semibold">How ServiceHub works with {cloudName}<Tip label="section:How ServiceHub works" /></h3>
      <ul className="mt-2 space-y-1.5">
        {items.map((i) => <li key={i.text} className="flex items-center gap-2 text-sm"><span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${i.dot}`} />{i.text}</li>)}
      </ul>
    </div>
  )
}
