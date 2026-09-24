import { CircleHelp } from 'lucide-react'
import { explanations, type ExplanationId } from '../../content/explanations'

/**
 * "What you're looking at" — the shared card that opens every working view: a few short definitions
 * and *Got it*. After it is dismissed a small (?) beside the title brings it back. Never required,
 * never blocking. Its words come from `content/explanations.ts`; the state lives in `useExplainer`.
 */
export function ExplainerCard({
  id,
  onDismiss,
  onLearnMore,
}: {
  id: ExplanationId
  onDismiss: () => void
  /** Opens the longer answer in Help. Omitted until Help exists (unit 6.4), so no dead link is shown. */
  onLearnMore?: () => void
}) {
  const explanation = explanations[id]
  return (
    <section
      aria-label={explanation.title}
      className="mb-5 flex items-start gap-3 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-4"
    >
      <CircleHelp className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <h2 className="text-sm font-semibold text-[var(--color-text)]">{explanation.title}</h2>
        <dl className="mt-1.5 space-y-1 text-sm text-[var(--color-text)]">
          {explanation.terms.map(({ term, meaning }) => (
            <div key={term}>
              <dt className="inline font-semibold">{term}</dt>
              <dd className="inline"> — {meaning}</dd>
            </div>
          ))}
        </dl>
      </div>
      <div className="flex shrink-0 flex-col items-end gap-1.5 text-sm">
        <button
          type="button"
          onClick={onDismiss}
          className="rounded-lg bg-[var(--color-primary-600)] px-3 py-1 font-medium text-white hover:bg-[var(--color-primary-700)]"
        >
          Got it
        </button>
        {onLearnMore && (
          <button type="button" onClick={onLearnMore} className="text-[var(--color-primary-700)] hover:underline">
            {explanation.learnMore} ›
          </button>
        )}
      </div>
    </section>
  )
}

/** The small (?) beside a view's title. Shown only after the card was dismissed. */
export function ExplainerToggle({ visible, onShow }: { visible: boolean; onShow: () => void }) {
  if (!visible) return null
  return (
    <button
      type="button"
      onClick={onShow}
      aria-label="What am I looking at?"
      title="What am I looking at?"
      className="rounded-full p-1 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"
    >
      <CircleHelp className="h-4 w-4" />
    </button>
  )
}
